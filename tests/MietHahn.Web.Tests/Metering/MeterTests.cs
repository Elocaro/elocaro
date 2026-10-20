using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Metering;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Web.Tests.Account;
using SkiaSharp;

namespace MietHahn.Web.Tests.Metering;

public class MeterTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Data goes through the services; operating the interactive pages is covered by MeteringBrowserTests.
    [Fact]
    public async Task Meter_list_shows_assignment_and_last_reading_and_a_photo_is_served_with_the_reading()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment) = await PropertyAsync();
        var meter = await MeterAsync(property, apartment, "WZ-4711", 100.5m);

        var list = await admin.GetAsync($"/verwaltung/objekte/{property}/zaehler");
        Assert.Contains("Wohnung 1", list.Text, StringComparison.Ordinal);
        Assert.Contains("100,5 m³", list.Text, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            await using var stream = new MemoryStream(Jpeg());
            var file = await sp.GetRequiredService<MietHahn.Application.Files.IFileStore>().SaveAsync(stream, "meter.jpg", ct);
            var result = await sp.GetRequiredService<IMeterService>().RecordReadingAsync(meter, new ReadingInput { Date = Today.AddDays(-1), Value = 130m }, file.Id, ct);
            Assert.True(result.Succeeded);
            return 0;
        });
        var page = await admin.GetAsync($"/verwaltung/zaehler/{meter}");
        var photo = page.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Foto").GetAttribute("href")!;
        using var response = await admin.Http.GetAsync(new Uri(photo, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Reading_round_starts_at_the_first_meter_and_an_interim_round_covers_only_one_unit()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment) = await PropertyAsync();
        await MeterAsync(property, null, "HWZ", 1000m);
        await MeterAsync(property, apartment, "WZ-1", 10m);
        await MeterAsync(property, apartment, "WZ-2", 20m);

        var step = await admin.GetAsync($"/verwaltung/objekte/{property}/ablesung");
        Assert.Contains("Zähler 1 von 3", step.Text, StringComparison.Ordinal);
        Assert.Contains("Nr. HWZ", step.Text, StringComparison.Ordinal);

        var interim = await admin.GetAsync($"/verwaltung/objekte/{property}/ablesung?einheit={apartment}&datum={Iso(Today)}&art=InterimReading");
        Assert.Contains("Zähler 1 von 2", interim.Text, StringComparison.Ordinal);
        Assert.Contains("Zwischenablesung", interim.Text, StringComparison.Ordinal);
    }

    private async Task<(Guid Property, Guid Apartment)> PropertyAsync() =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            return (property, apartment);
        });

    private async Task<Guid> MeterAsync(Guid property, Guid? assignment, string number, decimal start) =>
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IMeterService>().CreateAsync(property, new MeterCreation
        {
            Data = new MeterInput { Type = MeterType.ColdWater, Name = $"Zähler {number}", Number = number, AssignmentId = assignment },
            InstalledOn = Today.AddDays(-30),
            InitialReading = start,
        }, TestContext.Current.CancellationToken)).Id!.Value);

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }
}
