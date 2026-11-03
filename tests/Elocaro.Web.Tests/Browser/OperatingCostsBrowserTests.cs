using Elocaro.Application.Documents;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Admin;
using Elocaro.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Operating the interactive operating cost pages: keys, agreed cost types, heating, wizard and dispatch.</summary>
public class OperatingCostsBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid house;
    private Guid lease;

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    private async Task SeedAsync()
    {
        await App.WithScopeAsync<bool>(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(apartment, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct);
            var anna = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de" }, null, Ct)).Id!.Value;
            lease = (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2024, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)).Id!.Value;
            await sp.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "G-2", InvoiceDate = new DateOnly(2026, 2, 1), Gross = 1500m },
                [],
                new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
                Ct);
            var db = sp.GetRequiredService<ElocaroDbContext>();
            db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            await db.SaveChangesAsync(Ct);
            return true;
        });
    }

    /// <summary>Screenshot of the page in light and dark when <c>ELOCARO_TOUR_DIR</c> is set (to look at the design).</summary>
    private static async Task SnapAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("ELOCARO_TOUR_DIR") is not { } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await page.EmulateMediaAsync(new() { ColorScheme = scheme });
            await page.WaitForTimeoutAsync(400);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}-{scheme}.png"), FullPage = true });
        }

        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
    }

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, path);
        return page;
    }

    [Fact]
    public async Task Key_is_set_and_ended_and_a_subtype_is_added_and_removed()
    {
        await SeedAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{house}/umlageschluessel");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.Locator("#key-betrkv-01").GetByRole(AriaRole.Button, new() { Name = "Festlegen" }).ClickAsync();
            var rule = page.Locator(".mud-dialog");
            await Assertions.Expect(rule).ToContainTextAsync("Grundsteuer");
            await SelectAsync(page, "Schlüssel", "Einheiten", rule);
            await rule.GetByRole(AriaRole.Button, new() { Name = "Schlüssel speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Umlageschlüssel wurde gespeichert.");
            await Assertions.Expect(page.Locator("#key-betrkv-01")).ToContainTextAsync("Einheiten");

            await page.Locator("#key-betrkv-01").GetByRole(AriaRole.Button, new() { Name = "Beenden" }).ClickAsync();
            await rule.GetByRole(AriaRole.Button, new() { Name = "Schlüssel beenden" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Umlageschlüssel wurde beendet.");

            await page.GetByRole(AriaRole.Tab, new() { Name = "Unterarten" }).ClickAsync();
            var subtypes = page.Locator("#subtypes");
            await subtypes.GetByRole(AriaRole.Button, new() { Name = "Unterart anlegen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await SelectAsync(page, "Kostenart", "13.", dialog, exact: false);
            await dialog.GetByLabel("Name der Unterart").FillAsync("Gebäudeversicherung");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Unterart anlegen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Unterart wurde angelegt.");

            await page.GetByRole(AriaRole.Button, new() { Name = "Gebäudeversicherung entfernen" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Ja, entfernen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Unterart wurde entfernt.");
        });
    }

    [Fact]
    public async Task One_click_sends_the_statement_to_all_tenants_after_asking_once()
    {
        await SeedAsync();
        await App.WithScopeAsync<bool>(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var second = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(second, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 50m }, Ct);
            var ben = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber", Email = "ben@example.de" }, null, Ct)).Id!.Value;
            await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = second, Start = new DateOnly(2024, 1, 1), TenantIds = [ben], BaseRent = 450m, OperatingCostPrepayment = 90m }, Ct);
            return true;
        });
        var page = await OpenAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Weiter" }).ClickAsync();
            }

            await Assertions.Expect(page.Locator("#step-6")).ToBeVisibleAsync();
            await page.GetByLabel("Ich habe die Hinweise geprüft.").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Abrechnung freigeben" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/abrechnungen/*?meldung=freigegeben");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            await Assertions.Expect(page.Locator("#dispatch-progress")).ToContainTextAsync("0 von 2 zugestellt");
            await page.Locator("#dispatch-all").ClickAsync();
            await Assertions.Expect(page.Locator(".mud-message-box")).ToContainTextAsync("2 Mietverhältnisse");
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, an alle versenden" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Abrechnung wurde an 2 Mietverhältnisse versendet.");
            await Assertions.Expect(page.Locator("#dispatches").GetByText("versendet", new() { Exact = true })).ToHaveCountAsync(2);
            await Assertions.Expect(page.Locator("#dispatch-all")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task Agreed_cost_type_is_unticked_and_saved()
    {
        await SeedAsync();
        var page = await OpenAsync($"/verwaltung/vertraege/{lease}/kostenarten");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var box = page.GetByLabel("(Grundsteuer)");
            await box.UncheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die vereinbarten Kostenarten wurden gespeichert.");
        });

        await GotoInteractiveAsync(page, $"/verwaltung/vertraege/{lease}/kostenarten");
        await Assertions.Expect(page.GetByLabel("(Grundsteuer)")).Not.ToBeCheckedAsync();
    }

    [Fact]
    public async Task Heating_settings_are_saved()
    {
        await SeedAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{house}/heizung");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Anteil nach Verbrauch").FillAsync("60");
            await page.GetByLabel("Anteil nach Verbrauch").PressAsync("Tab");
            await page.GetByRole(AriaRole.Button, new() { Name = "Einstellungen speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Heizungseinstellungen wurden gespeichert.");
            await Assertions.Expect(page.Locator("#defaults-notice")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task Wizard_leads_through_the_steps_releases_and_the_statement_is_sent_and_confirmed()
    {
        await SeedAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{house}/abrechnung?jahr=2026");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("#step-1")).ToContainTextAsync("Lindenweg 3");
            await SnapAsync(page, "statement-1");
            await page.GetByRole(AriaRole.Button, new() { Name = "Weiter" }).ClickAsync();
            await Assertions.Expect(page.Locator("#step-2")).ToBeVisibleAsync();
            for (var i = 0; i < 3; i++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Weiter" }).ClickAsync();
            }

            await Assertions.Expect(page.Locator("#totals")).ToContainTextAsync("Anna Schmidt");
            await SnapAsync(page, "statement-5");
            await page.GetByRole(AriaRole.Button, new() { Name = "Weiter" }).ClickAsync();

            await Assertions.Expect(page.Locator("#step-6")).ToBeVisibleAsync();
            await SnapAsync(page, "statement-6");
            await page.GetByLabel("Ich habe die Hinweise geprüft.").CheckAsync();

            await page.GetByLabel("Bemerkung (optional)").FillAsync("Erstfassung");
            await page.GetByRole(AriaRole.Button, new() { Name = "Abrechnung freigeben" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/abrechnungen/*?meldung=freigegeben");
            await Assertions.Expect(page.Locator("#frozen")).ToContainTextAsync("Diese Fassung ist unveränderlich");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            await page.GetByRole(AriaRole.Button, new() { Name = "Versenden: " }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Abrechnung wurde versendet");
            await Assertions.Expect(page.Locator("#dispatches")).ToContainTextAsync("per E-Mail am");

            await page.GetByRole(AriaRole.Button, new() { Name = "Zugang bestätigen: " }).ClickAsync();
            await page.Locator(".mud-dialog").GetByLabel("Nachweis").FillAsync("Lesebestätigung");
            await page.Locator(".mud-dialog").GetByRole(AriaRole.Button, new() { Name = "Zugang bestätigen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Zugang wurde vermerkt.");
            await Assertions.Expect(page.Locator("#dispatches")).ToContainTextAsync("Lesebestätigung");

            await page.GetByRole(AriaRole.Button, new() { Name = "Übernehmen" }).First.ClickAsync();
            await ExpectSnackbarAsync(page, "Die neue Vorauszahlung wurde übernommen.");
            await Assertions.Expect(page.Locator("#proposals")).ToContainTextAsync("übernommen am");
        });
    }

    [Fact]
    public async Task Preview_recalculates_when_the_year_changes()
    {
        await SeedAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{house}/nebenkosten?jahr=2026");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("#type-betrkv-01")).ToContainTextAsync("1.500,00 €");
            await SelectAsync(page, "Abrechnungsjahr", "2025");
            await Assertions.Expect(page.Locator("#type-betrkv-01")).ToHaveCountAsync(0);
        });
    }
}
