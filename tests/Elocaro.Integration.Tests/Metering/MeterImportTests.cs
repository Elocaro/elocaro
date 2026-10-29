using System.Text;
using Elocaro.Application.Common;
using Elocaro.Application.Metering;
using Elocaro.Application.Properties;
using Elocaro.Domain.Metering;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Metering;

public class MeterImportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;
    private Guid meter;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct));
        meter = Validate(await scope.ServiceProvider.GetRequiredService<IMeterService>().CreateAsync(propertyId, new MeterCreation
        {
            Data = new MeterInput { Type = MeterType.Heat, Name = "Wärme Wohnung 1", Number = "WMZ-4711", AssignmentId = unit },
            InstalledOn = new DateOnly(2025, 12, 31),
            InitialReading = 1000m,
        }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");

    [Fact]
    public async Task Readings_of_a_csv_export_are_previewed_and_imported_and_a_second_import_changes_nothing()
    {
        var csv = Csv(
            "Zählernummer;Datum;Zählerstand;Einheit",
            "WMZ-4711;31.01.2026;1.250,5;kWh",
            "WMZ-4711;28.02.2026;1.480,0;kWh",
            "WMZ-4711;31.03.2026 23:59;1.650,25;kWh",
            "WMZ-9999;31.01.2026;5,0;kWh",
            "WMZ-4711;15.02.2026;1.200,0;kWh");

        var preview = await WithAsync<ReadingImportPreview>(i => i.PreviewAsync(propertyId, csv, Ct));

        Assert.Equal(3, preview.New);
        Assert.Equal([ImportRowStatus.New, ImportRowStatus.New, ImportRowStatus.New, ImportRowStatus.UnknownMeter, ImportRowStatus.Rejected], preview.Rows.Select(r => r.Status));
        Assert.Contains("Rückwärtslauf", preview.Rows[4].Note, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 3, 31), preview.Rows[2].Date);
        Assert.Equal(1650.25m, preview.Rows[2].Value);

        var outcome = await WithAsync<ReadingImportOutcome>(i => i.ImportAsync(propertyId, csv, Ct));
        Assert.True(outcome.Succeeded);
        Assert.Equal((3, 2), (outcome.Imported, outcome.Skipped));

        var readings = (await WithMeterAsync(s => s.GetAsync(meter, Ct)))!.Readings;
        Assert.Equal([1000m, 1250.5m, 1480m, 1650.25m], readings.OrderBy(r => r.Date).Select(r => r.Value));

        var again = await WithAsync<ReadingImportPreview>(i => i.PreviewAsync(propertyId, csv, Ct));
        Assert.Equal(0, again.New);
        Assert.Equal(ImportRowStatus.Duplicate, again.Rows[0].Status);
        Assert.False((await WithAsync<ReadingImportOutcome>(i => i.ImportAsync(propertyId, csv, Ct))).Succeeded);
    }

    [Fact]
    public async Task A_different_value_on_a_known_day_and_a_day_before_installation_are_not_taken_over()
    {
        var csv = Csv(
            "Zählernummer;Datum;Zählerstand",
            "WMZ-4711;31.12.2025;999,0",
            "WMZ-4711;01.12.2025;900,0",
            "WMZ-4711;31.01.2026;1100,0");

        var preview = await WithAsync<ReadingImportPreview>(i => i.PreviewAsync(propertyId, csv, Ct));

        Assert.Equal([ImportRowStatus.Rejected, ImportRowStatus.Rejected, ImportRowStatus.New], preview.Rows.Select(r => r.Status));
        Assert.Contains("anderer Stand", preview.Rows[0].Note, StringComparison.Ordinal);
        Assert.Contains("nicht eingebaut", preview.Rows[1].Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comma_separated_english_files_and_missing_columns_are_handled()
    {
        var english = Csv("Meter,Date,Value", "WMZ-4711,2026-01-31,1100.5");
        var preview = await WithAsync<ReadingImportPreview>(i => i.PreviewAsync(propertyId, english, Ct));
        Assert.Equal(1100.5m, Assert.Single(preview.Rows).Value);
        Assert.Equal(ImportRowStatus.New, preview.Rows[0].Status);

        var broken = Csv("Name;Wert", "A;1");
        var refused = await WithAsync<ReadingImportPreview>(i => i.PreviewAsync(propertyId, broken, Ct));
        Assert.Contains("Zählernummer", Assert.Single(refused.Errors), StringComparison.Ordinal);
        Assert.False(refused.CanImport);

        var unreadable = await WithAsync<ReadingImportPreview>(i => i.PreviewAsync(propertyId, Csv("Zählernummer;Datum;Zählerstand", "WMZ-4711;gestern;abc"), Ct));
        Assert.Equal(ImportRowStatus.Rejected, Assert.Single(unreadable.Rows).Status);
    }

    private async Task<T> WithAsync<T>(Func<IMeterImport, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMeterImport>());
    }

    private async Task<T> WithMeterAsync<T>(Func<IMeterService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMeterService>());
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
