using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Operating the interactive lease pages: creating, rent components, parties, termination, rent models, move-out, CPI.</summary>
public class LeasesBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    private static string German(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private async Task<(Guid Apartment, Guid Anna, Guid Ben)> MasterDataAsync() =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var people = sp.GetRequiredService<IPersonService>();
            var anna = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            var ben = (await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, ct)).Id!.Value;
            return (apartment, anna, ben);
        });

    private async Task<Guid> LeaseAsync(Guid apartment, Guid tenant, DateOnly start) =>
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = apartment, Start = start, TenantIds = [tenant], BaseRent = 650m }, TestContext.Current.CancellationToken)).Id!.Value);

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, path);
        return page;
    }

    [Fact]
    public async Task Lease_is_created_with_validation_two_tenants_and_the_preselected_rental_unit()
    {
        var (apartment, anna, ben) = await MasterDataAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1);
        var page = await OpenAsync($"/verwaltung/vertraege/neu?mietobjekt={apartment}&beginn={start:yyyy-MM-dd}");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.GetByLabel("Mietobjekt", new() { Exact = true }).First).ToHaveValueAsync("Lindenweg 3 – Wohnung 1 (frei)");

            await page.GetByRole(AriaRole.Button, new() { Name = "Vertrag anlegen", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte den Mieter wählen.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte die Kaltmiete angeben.");

            await SelectAsync(page, "Mieter", "Anna Schmidt");
            await SelectAsync(page, "Weiterer Mieter", "Ben Weber");
            await page.GetByLabel("Kaltmiete (€/Monat)").FillAsync("650");
            await page.GetByLabel("Betriebskosten-Vorauszahlung").FillAsync("180");
            await page.GetByLabel("Heizkosten-Vorauszahlung").FillAsync("90,5");
            await page.GetByLabel("Kaution", new() { Exact = true }).FillAsync("1950");
            await page.GetByRole(AriaRole.Button, new() { Name = "Vertrag anlegen", Exact = true }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/vertraege/*?meldung=angelegt");
            await ExpectSnackbarAsync(page, "Der Eintrag wurde angelegt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Anna Schmidt, Ben Weber");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("920,50 €");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("1.950,00 €");
        });
        _ = (anna, ben);
    }

    [Fact]
    public async Task Rent_component_party_and_termination_are_maintained_on_the_lease_page()
    {
        var (apartment, anna, ben) = await MasterDataAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1);
        var id = await LeaseAsync(apartment, anna, start);
        var page = await OpenAsync($"/verwaltung/vertraege/{id}");
        var increase = start.AddMonths(2);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // Rent increase from a cut-off date; the old amount stays in the history.
            await page.Locator("summary", new() { HasText = "Betrag ändern oder hinzufügen" }).ClickAsync();
            await page.GetByLabel("Gültig ab").First.FillAsync(German(increase));
            await page.GetByLabel("Betrag (€/Monat)").FillAsync("700");
            await page.GetByRole(AriaRole.Button, new() { Name = "Betrag speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Betrag wurde gespeichert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Verlauf");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("700,00 €");

            // Guarantor: add, remove after confirmation; the only tenant cannot be removed.
            await Assertions.Expect(page.Locator("button[aria-label$='entfernen']")).ToHaveCountAsync(0);
            await OpenTabAsync(page, "Parteien & Haushalt");
            await page.Locator("summary", new() { HasText = "Mieter oder Bürgen hinzufügen" }).ClickAsync();
            await SelectAsync(page, "Person", "Ben Weber");
            await SelectAsync(page, "Rolle", "Bürge");
            await page.GetByRole(AriaRole.Button, new() { Name = "Hinzufügen", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bürge");
            await page.Locator("button[aria-label='Ben Weber entfernen']").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, entfernen" }).ClickAsync();
            await Assertions.Expect(page.Locator("button[aria-label='Ben Weber entfernen']")).ToHaveCountAsync(0);

            // Termination by the tenant, then revoked.
            await OpenTabAsync(page, "Kündigung");
            var incoming = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddDays(14);
            await page.GetByLabel("Eingegangen am").FillAsync(German(incoming));
            await page.GetByRole(AriaRole.Button, new() { Name = "Kündigung erfassen" }).ClickAsync();
            var ends = MietHahn.Domain.Leases.Lease.TerminationDateForTenant(incoming);
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync($"endet am {German(ends)}");

            await page.GetByRole(AriaRole.Button, new() { Name = "Kündigung aufheben" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("unbefristet");
        });
        _ = ben;
    }

    [Fact]
    public async Task Graduated_rent_is_entered_validated_and_removed_after_confirmation()
    {
        var (apartment, anna, _) = await MasterDataAsync();
        var id = await LeaseAsync(apartment, anna, Today);
        var page = await OpenAsync($"/verwaltung/vertraege/{id}");
        var from = Today.AddYears(1);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var model = page.Locator("section", new() { Has = page.Locator("h2", new() { HasText = "Mietmodell" }) });
            await model.Locator("summary", new() { HasText = "Mietmodell ändern" }).ClickAsync();
            await SelectAsync(page, "Mietmodell", "Staffelmiete");
            await page.GetByRole(AriaRole.Button, new() { Name = "Mietmodell speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Mietmodell: Staffelmiete");

            await model.GetByLabel("Ab", new() { Exact = true }).FillAsync(German(from));
            await model.GetByLabel("Neue Kaltmiete").FillAsync("700");
            await page.GetByRole(AriaRole.Button, new() { Name = "Staffel speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("geplant");

            await model.Locator("summary", new() { HasText = "Staffel hinzufügen" }).ClickAsync(); // closes after saving: reopen
            await model.GetByLabel("Ab", new() { Exact = true }).FillAsync(German(from.AddMonths(6)));
            await model.GetByLabel("Neue Kaltmiete").FillAsync("720");
            await page.GetByRole(AriaRole.Button, new() { Name = "Staffel speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("mindestens ein Jahr unverändert");

            await page.Locator("button[aria-label^='Staffel'][aria-label$='entfernen']").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, entfernen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Staffeln erfasst.");
        });
    }

    [Fact]
    public async Task Cpi_value_is_entered_by_hand_and_imported_and_adjusts_an_indexed_rent()
    {
        var (apartment, anna, _) = await MasterDataAsync();
        var start = Today.AddMonths(-14);
        var id = await LeaseAsync(apartment, anna, start);
        var baseMonth = new DateOnly(start.Year, start.Month, 1);
        var newMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2);
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<ILeaseService>().SetRentModelAsync(
            id, new RentModelInput { Model = MietHahn.Domain.Leases.RentModel.Index, IndexBaseMonth = baseMonth, IndexBaseValue = 117.4m }, TestContext.Current.CancellationToken)).Succeeded);
        var page = await OpenAsync("/verwaltung/vpi");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Monat").First.FillAsync("09.2026");
            await page.GetByLabel("Indexwert").FillAsync("122,1");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Indexwert wurde gespeichert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("September 2026");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("122,1");

            await page.GetByLabel("CSV-Inhalt").FillAsync($"Jahr;Monat;Index\n{baseMonth:yyyy-MM};117,4\n{newMonth:yyyy-MM};121,7\n");
            await page.GetByRole(AriaRole.Button, new() { Name = "Importieren" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Import: 2 neu");

            await GotoInteractiveAsync(page, $"/verwaltung/vertraege/{id}");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("neue Kaltmiete 676,02 €"); // 650 × 122,1 / 117,4 (the September value entered by hand is the newest)
            await page.GetByLabel("Erklärung geht zu am").FillAsync(German(Today));
            await page.GetByLabel("Erklärung geht zu am").PressAsync("Tab");
            await page.GetByRole(AriaRole.Button, new() { Name = "Anpassung übernehmen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Indexanpassung wurde übernommen.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("676,02 €");
        });
    }

    [Fact]
    public async Task Move_out_records_the_new_address_and_leads_on_to_the_new_lease()
    {
        var (apartment, anna, _) = await MasterDataAsync();
        var id = await LeaseAsync(apartment, anna, Today.AddMonths(-12));
        var page = await OpenAsync($"/verwaltung/vertraege/{id}/auszug");
        var end = Today.AddDays(3);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Letzter Tag des Mietverhältnisses").FillAsync(German(end));
            await page.GetByLabel("Straße und Hausnummer").FillAsync("Neue Straße 5");
            await page.GetByLabel("PLZ").FillAsync("10115");
            await page.GetByLabel("Ort").FillAsync("Berlin");
            await page.GetByRole(AriaRole.Button, new() { Name = "Auszug erfassen" }).ClickAsync();

            await Assertions.Expect(page.Locator("main")).ToContainTextAsync($"endet am {German(end)}");
            var link = await page.GetByRole(AriaRole.Link, new() { Name = "Neuen Vertrag anlegen" }).GetAttributeAsync("href");
            Assert.Contains($"beginn={end.AddDays(1):yyyy-MM-dd}", link, StringComparison.Ordinal);
        });
    }
}
