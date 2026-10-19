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

    [Fact]
    public async Task Create_meter_add_reading_with_photo_and_confirm_backward_reading()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment) = await PropertyAsync();

        var list = await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/objekte/{property}/zaehler"), "meter", new Dictionary<string, string>
        {
            ["Data.Type"] = "ColdWater",
            ["Data.Name"] = "Kaltwasser Bad",
            ["Data.Number"] = "WZ-4711",
            ["Data.AssignmentId"] = apartment.ToString(),
            ["Creation.InstalledOn"] = Iso(Today.AddDays(-60)),
            ["Creation.InitialReading"] = "100.5",
        });
        Assert.Contains("Der Zähler wurde angelegt.", list.Text, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1", list.Text, StringComparison.Ordinal);
        Assert.Contains("100,5 m³", list.Text, StringComparison.Ordinal);

        var page = await admin.GetAsync(list.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Kaltwasser Bad").GetAttribute("href")!);
        page = await admin.SubmitWithFileAsync(page, "entry", "Photo", Jpeg(), "meter.jpg");
        Assert.Contains("Bitte den Zählerstand angeben.", page.Text, StringComparison.Ordinal);

        var fields = new Dictionary<string, string> { ["Entry.Date"] = Iso(Today.AddDays(-30)), ["Entry.Value"] = "130" };
        page = await admin.SubmitAsync(await admin.GetAsync(page.Path), "entry", fields);
        Assert.Contains("Der Zählerstand wurde gespeichert.", page.Text, StringComparison.Ordinal);

        var warning = await admin.SubmitAsync(page, "entry", new Dictionary<string, string> { ["Entry.Date"] = Iso(Today), ["Entry.Value"] = "120" });
        Assert.Contains("Rückwärtslauf", warning.Text, StringComparison.Ordinal);
        Assert.Contains("Trotzdem speichern", warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("120 ", warning.Document.QuerySelector("table.data-table")!.TextContent, StringComparison.Ordinal);

        var confirmed = await admin.SubmitAsync(warning, "entry", new Dictionary<string, string> { ["Entry.Date"] = Iso(Today), ["Entry.Value"] = "120" });
        Assert.Contains("Der Zählerstand wurde gespeichert.", confirmed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Photo_is_saved_with_the_reading()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment) = await PropertyAsync();
        var meter = await MeterAsync(property, apartment, "WZ-1", 100m);
        var page = await admin.GetAsync($"/verwaltung/zaehler/{meter}");

        // Set form fields and attach a photo: the test browser submits the inputs along
        page.Form("entry").Elements.OfType<AngleSharp.Html.Dom.IHtmlInputElement>().Single(e => e.Name == "Entry.Value").Value = "150";
        var saved = await admin.SubmitWithFileAsync(page, "entry", "Photo", Jpeg(), "meter.jpg");

        Assert.Contains("Der Zählerstand wurde gespeichert.", saved.Text, StringComparison.Ordinal);
        var photo = saved.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Foto").GetAttribute("href")!;
        using var response = await admin.Http.GetAsync(new Uri(photo, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Reading_round_step_by_step_with_skipping_and_summary()
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

        step = await admin.SubmitAsync(step, "reading", new Dictionary<string, string> { ["Entry.Value"] = "1010" });
        Assert.Contains("Zähler 2 von 3", step.Text, StringComparison.Ordinal);
        Assert.Contains("gespeichert.", step.Text, StringComparison.Ordinal);

        step = await admin.GetAsync(step.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Überspringen").GetAttribute("href")!);
        Assert.Contains("Zähler 3 von 3", step.Text, StringComparison.Ordinal);

        var end = await admin.SubmitAsync(step, "reading", new Dictionary<string, string> { ["Entry.Value"] = "21.5" });
        Assert.Contains("Runde abgeschlossen: 2 von 3 Zählern erfasst.", end.Text, StringComparison.Ordinal);
        Assert.Contains("nicht erfasst", end.Text, StringComparison.Ordinal);
        Assert.Contains("21,5 m³", end.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interim_reading_only_for_the_meters_of_a_unit()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment) = await PropertyAsync();
        await MeterAsync(property, null, "HWZ", 1000m);
        var waterMeter = await MeterAsync(property, apartment, "WZ-1", 10m);

        var step = await admin.GetAsync($"/verwaltung/objekte/{property}/ablesung?einheit={apartment}&datum={Iso(Today)}&art=InterimReading");
        Assert.Contains("Zähler 1 von 1", step.Text, StringComparison.Ordinal);
        Assert.Contains("Zwischenablesung", step.Text, StringComparison.Ordinal);
        await admin.SubmitAsync(step, "reading", new Dictionary<string, string> { ["Entry.Value"] = "12" });

        var entry = (await App.WithScopeAsync(sp => sp.GetRequiredService<IMeterService>().GetAsync(waterMeter, TestContext.Current.CancellationToken)))!.Readings[0];
        Assert.Equal(ReadingType.InterimReading, entry.Kind);
    }

    [Fact]
    public async Task Meter_replacement_via_the_UI()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment) = await PropertyAsync();
        var meter = await MeterAsync(property, apartment, "WZ-ALT", 100m);

        var newItem = await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/zaehler/{meter}"), "replacement", new Dictionary<string, string>
        {
            ["Replacement.Date"] = Iso(Today),
            ["Replacement.FinalReading"] = "150",
            ["Replacement.NewNumber"] = "WZ-NEU",
            ["Replacement.InitialReading"] = "0",
        });

        Assert.Contains("Der Zähler wurde getauscht.", newItem.Text, StringComparison.Ordinal);
        Assert.Contains("Nr. WZ-NEU", newItem.Text, StringComparison.Ordinal);
        Assert.Contains("WZ-ALT", newItem.Text, StringComparison.Ordinal); // metering point
        Assert.DoesNotContain(meter.ToString(), newItem.Path, StringComparison.Ordinal);
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
