using System.Net;
using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;
using Elocaro.Infrastructure.Heating;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elocaro.Integration.Tests.OperatingCosts;

/// <summary>Monthly degree days of the Deutscher Wetterdienst: parsing, import and the table of a billing period.</summary>
public class DegreeDayTests(PostgresFixture postgres)
{
    private const string January = """
        #Monatsgradtage VDI 3807; erstellt: DWD, 20260203
        #Messnetz_id 4; mittlere Raumtemperatur (Grad Celsius): 20
        #ID; geogr. Breite; geogr. Laenge;Station; Monat; Anzahl Tage; Monatsgradtage; Anzahl Heiztage; Mittelwert 2009-2018
        00044;52.93; 8.24;            GROSSENKNETEN;202601;31;458.4;31;504.9
        20098;48.57; 8.23;    SEEBACH (SCHWARZWALD);202601;31;-999.9;31;-999.9
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Csv_is_parsed_and_missing_values_are_skipped()
    {
        var rows = DegreeDayCsv.Parse(January).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("00044", row.StationId);
        Assert.Equal("GROSSENKNETEN", row.StationName);
        Assert.Equal((52.93m, 8.24m), (row.Latitude, row.Longitude));
        Assert.Equal(new DateOnly(2026, 1, 1), row.Month);
        Assert.Equal(458.4m, row.DegreeDays);
        Assert.Equal(504.9m, row.LongTermMean);
    }

    [Fact]
    public async Task Import_fetches_the_published_months_and_updates_instead_of_duplicating()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero));
        var handler = new FakeDwd(new Dictionary<string, string> { ["gradtage_202601.csv"] = January });
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var importer = new DegreeDayImporter(db, new HttpClient(handler), environment.Clock,
            Options.Create(new DegreeDayOptions { BaseUrl = new Uri("https://dwd.test/"), YearsBack = 1 }), NullLogger<DegreeDayImporter>.Instance);

        Assert.Equal(1, await importer.ImportAsync(Ct));
        handler.Files["gradtage_202601.csv"] = January.Replace("458.4", "460.0", StringComparison.Ordinal);
        Assert.Equal(1, await importer.ImportAsync(Ct));

        var value = await db.Set<DegreeDayValue>().SingleAsync(Ct);
        Assert.Equal(460.0m, value.DegreeDays);
        Assert.Equal(504.9m, value.LongTermMean);
    }

    [Fact]
    public async Task A_slow_server_ends_the_import_quietly_for_the_next_run()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var importer = new DegreeDayImporter(db, new HttpClient(new TimingOut()), environment.Clock,
            Options.Create(new DegreeDayOptions { BaseUrl = new Uri("https://dwd.test/"), YearsBack = 1 }), NullLogger<DegreeDayImporter>.Instance);

        Assert.Equal(0, await importer.ImportAsync(Ct)); // no exception: the job does not fail because the DWD is slow
    }

    [Fact]
    public async Task Import_does_nothing_when_switched_off()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var handler = new FakeDwd(new Dictionary<string, string> { ["gradtage_202601.csv"] = January });
        var importer = new DegreeDayImporter(db, new HttpClient(handler), environment.Clock,
            Options.Create(new DegreeDayOptions { Enabled = false, BaseUrl = new Uri("https://dwd.test/") }), NullLogger<DegreeDayImporter>.Instance);

        Assert.Equal(0, await importer.ImportAsync(Ct));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public void Table_uses_the_measured_months_when_all_are_published()
    {
        var values = Enumerable.Range(1, 12).Select(m => new DegreeDayValue("00044", "GROSSENKNETEN", new DateOnly(2026, m, 1), m * 10m, 100m)).ToList();

        var table = DegreeDayStationTable.Build(values, Period.CalendarYear(2026))!;

        Assert.NotNull(table.Measured);
        Assert.True(table.Verified);
        Assert.Equal("DWD 00044", table.Version);
        Assert.Equal(60m, table.Weight(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public void Table_falls_back_to_the_long_term_mean_when_months_are_missing()
    {
        // Months of 2025 are known (with means), the billing year 2026 is not published yet.
        var values = Enumerable.Range(1, 12).Select(m => new DegreeDayValue("00044", "GROSSENKNETEN", new DateOnly(2025, m, 1), 50m, m == 1 ? 200m : 100m)).ToList();

        var table = DegreeDayStationTable.Build(values, Period.CalendarYear(2026))!;

        Assert.Null(table.Measured);
        Assert.Equal(1000, table.PermillePerMonth.Sum());
        Assert.Contains("noch nicht veröffentlicht", table.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_is_missing_without_values_or_means()
    {
        Assert.Null(DegreeDayStationTable.Build([], Period.CalendarYear(2026)));
        var onlyOneMonth = new[] { new DegreeDayValue("00044", "GROSSENKNETEN", new DateOnly(2025, 1, 1), 50m, null) };

        Assert.Null(DegreeDayStationTable.Build(onlyOneMonth, Period.CalendarYear(2026)));
    }

    /// <summary>Like the client timeout: the request is cancelled without the caller having asked for it.</summary>
    private sealed class TimingOut : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("timeout", new TimeoutException());
    }

    private sealed class FakeDwd(Dictionary<string, string> files) : HttpMessageHandler
    {
        public Dictionary<string, string> Files { get; } = files;

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var name = request.RequestUri!.Segments[^1];
            return Task.FromResult(Files.TryGetValue(name, out var text)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

public class PostalCodesTests
{
    [Fact]
    public void A_postal_code_has_a_position_and_distances_are_plausible()
    {
        var cologne = PostalCodes.Find("50667")!.Value;
        var berlin = PostalCodes.Find("10115")!.Value;

        Assert.InRange(cologne.Latitude, 50.8, 51.1);
        Assert.InRange(PostalCodes.DistanceKm(cologne, berlin), 470, 490); // as the crow flies Köln–Berlin
        Assert.Null(PostalCodes.Find("00000"));
        Assert.Null(PostalCodes.Find(null));
        Assert.Null(PostalCodes.Find("5066"));
    }
}
