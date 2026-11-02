using System.Globalization;
using Elocaro.Application.Metering;
using Elocaro.Application.Properties;
using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SkiaSharp;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Operating the interactive meter pages: creating, readings with photo, warnings, reading round, replacement.</summary>
public class MeteringBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    private static string German(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

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

    [Fact]
    public async Task Meter_is_created_and_a_reading_with_photo_is_saved_after_confirming_a_backward_reading()
    {
        var (property, apartment) = await PropertyAsync();
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/objekte/{property}/zaehler");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zähler hinzufügen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await SelectAsync(page, "Art", "Kaltwasser");
            await dialog.GetByLabel("Bezeichnung").FillAsync("Kaltwasser Bad");
            await dialog.GetByLabel("Zählernummer").FillAsync("WZ-4711");
            await SelectAsync(page, "Zuordnung", "Wohnung 1");
            await dialog.GetByLabel("Eingebaut am").FillAsync(German(Today.AddDays(-60)));
            await dialog.GetByLabel("Stand an diesem Tag").FillAsync("100,5");
            await dialog.GetByLabel("Stand an diesem Tag").PressAsync("Tab"); // the value is taken over when the field is left
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Zähler anlegen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Zähler wurde angelegt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("100,5 m³");

            await page.GetByRole(AriaRole.Link, new() { Name = "Kaltwasser Bad" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/zaehler/*");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            // Without a value the dialog reports it; a normal reading with a photo is saved.
            await page.GetByRole(AriaRole.Button, new() { Name = "Stand erfassen" }).ClickAsync();
            var entry = page.Locator(".mud-dialog");
            var save = entry.GetByRole(AriaRole.Button, new() { Name = "Stand speichern" });
            await save.ClickAsync();
            await Assertions.Expect(entry).ToContainTextAsync("Bitte den Zählerstand angeben.");
            await entry.GetByLabel("Datum", new() { Exact = true }).FillAsync(German(Today.AddDays(-30)));
            await entry.GetByLabel("Zählerstand").FillAsync("130");
            await entry.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = "meter.jpg", MimeType = "image/jpeg", Buffer = Jpeg() });
            await save.ClickAsync();
            await ExpectSnackbarAsync(page, "Der Zählerstand wurde gespeichert.");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Foto" })).ToBeVisibleAsync();

            // A reading below the previous one needs confirmation.
            await page.GetByRole(AriaRole.Button, new() { Name = "Stand erfassen" }).ClickAsync();
            await entry.GetByLabel("Datum", new() { Exact = true }).FillAsync(German(Today));
            await entry.GetByLabel("Zählerstand").FillAsync("120");
            await save.ClickAsync();
            await Assertions.Expect(entry).ToContainTextAsync("Rückwärtslauf");
            await entry.GetByRole(AriaRole.Button, new() { Name = "Trotzdem speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("section", new() { Has = page.Locator("h2", new() { HasText = "Stände" }) }).Locator("tbody")).ToContainTextAsync("120");
        });
        _ = apartment;
    }

    [Fact]
    public async Task Reading_round_goes_step_by_step_with_skipping_and_a_summary()
    {
        var (property, apartment) = await PropertyAsync();
        await MeterAsync(property, null, "HWZ", 1000m);
        await MeterAsync(property, apartment, "WZ-1", 10m);
        await MeterAsync(property, apartment, "WZ-2", 20m);
        var page = await SignedInAsAdminAsync();

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/objekte/{property}/ablesung");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Zähler 1 von 3");
            await page.GetByLabel("Zählerstand").FillAsync("1010");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern & weiter" }).ClickAsync();

            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Zähler 2 von 3");
            await page.GetByRole(AriaRole.Button, new() { Name = "Überspringen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Zähler 3 von 3");
            await page.GetByLabel("Zählerstand").FillAsync("21,5");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern & weiter" }).ClickAsync();

            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Runde abgeschlossen: 2 von 3 Zählern erfasst.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("nicht erfasst");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("21,5 m³");
        });
    }

    [Fact]
    public async Task Interim_reading_is_saved_with_its_type()
    {
        var (property, apartment) = await PropertyAsync();
        await MeterAsync(property, null, "HWZ", 1000m);
        var meter = await MeterAsync(property, apartment, "WZ-1", 10m);
        var page = await SignedInAsAdminAsync();

        await GotoInteractiveAsync(page, $"/verwaltung/objekte/{property}/ablesung?einheit={apartment}&datum={Today:yyyy-MM-dd}&art=InterimReading");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Zähler 1 von 1");
        await page.GetByLabel("Zählerstand").FillAsync("12");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern & weiter" }).ClickAsync();
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Runde abgeschlossen");

        var reading = (await App.WithScopeAsync(sp => sp.GetRequiredService<IMeterService>().GetAsync(meter, TestContext.Current.CancellationToken)))!.Readings[0];
        Assert.Equal(ReadingType.InterimReading, reading.Kind);
    }

    [Fact]
    public async Task Meter_is_replaced_and_the_new_meter_page_shows_the_metering_point()
    {
        var (property, apartment) = await PropertyAsync();
        var meter = await MeterAsync(property, apartment, "WZ-ALT", 100m);
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/zaehler/{meter}");
            await OpenTabAsync(page, "Austausch & Ausbau");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zähler tauschen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await dialog.GetByLabel("Tauschdatum").FillAsync(German(Today));
            await dialog.GetByLabel("Endstand alter Zähler").FillAsync("150");
            await dialog.GetByLabel("Nummer neuer Zähler").FillAsync("WZ-NEU");
            await dialog.GetByLabel("Anfangsstand neuer Zähler").FillAsync("0");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Tausch erfassen" }).ClickAsync();

            await page.WaitForURLAsync(url => url.Contains("/verwaltung/zaehler/", StringComparison.Ordinal) && !url.Contains(meter.ToString(), StringComparison.Ordinal));
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Nr. WZ-NEU");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("WZ-ALT");
        });
    }
}
