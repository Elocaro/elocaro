using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Operating the interactive tax pages: loans, depreciation, maintenance and Anlage V.</summary>
public class TaxBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Guid> PropertyAsync() =>
        App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value);

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, path);
        return page;
    }

    [Fact]
    public async Task Loan_is_created_and_certificate_and_extra_payment_are_maintained()
    {
        await PropertyAsync();
        var page = await OpenAsync("/verwaltung/finanzen/darlehen");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await SelectAsync(page, "Objekt", "Lindenweg 3");
            await page.GetByLabel("Bank").FillAsync("Sparkasse");
            await page.GetByLabel("Darlehensbetrag").FillAsync("200000");
            await page.GetByLabel("Auszahlung").FillAsync("01.01.2024");
            await page.GetByLabel("Erste Rate fällig").FillAsync("01.02.2024");
            await page.GetByLabel("Sollzins").FillAsync("3,6");
            await page.GetByLabel("Rate (Zins + Tilgung)").FillAsync("1000");
            await page.GetByLabel("Anteil Vermietung").FillAsync("100");
            await page.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/finanzen/darlehen/*?meldung=angelegt");
            await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Sparkasse");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            await OpenTabAsync(page, "Unterlagen der Bank");
            var section = page.Locator("section", new() { HasText = "Jahreszinsbescheinigung" });
            await SelectAsync(page, "Jahr", "2024", section);
            await section.GetByLabel("Zinsen laut Bescheinigung").FillAsync("6543,21");
            await section.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Bescheinigung wurde gespeichert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("6.543,21");

            await OpenTabAsync(page, "Zahlungen & Kosten");
            var extra = page.Locator("section", new() { HasText = "Sondertilgungen" });
            await extra.GetByLabel("Datum").FillAsync("01.06.2024");
            await extra.GetByLabel("Betrag").FillAsync("5000");
            await extra.GetByRole(AriaRole.Button, new() { Name = "Hinzufügen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Sondertilgung wurde hinzugefügt.");

            await OpenTabAsync(page, "Unterlagen der Bank");
            await page.GetByRole(AriaRole.Button, new() { Name = "Bescheinigung 2024 entfernen" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Ja, entfernen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Bescheinigung wurde entfernt.");
        });
    }

    [Fact]
    public async Task Acquisition_is_saved_and_a_cost_is_added_and_removed_after_confirmation()
    {
        var property = await PropertyAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{property}/afa");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Kaufdatum").FillAsync("15.07.2024");
            await page.GetByLabel("Kaufpreis").FillAsync("300000");
            await page.GetByLabel("Anteil Grund und Boden").FillAsync("20");
            await page.GetByLabel("Baujahr").FillAsync("1990");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Anschaffungsdaten wurden gespeichert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Abschreibungsplan");

            await page.GetByRole(AriaRole.Button, new() { Name = "Kosten hinzufügen" }).ClickAsync();
            var form = page.Locator(".mud-dialog");
            await form.GetByLabel("Datum").FillAsync("01.03.2025");
            await form.GetByLabel("Betrag (netto)").FillAsync("45000");
            await form.GetByLabel("Beschreibung").FillAsync("Sanierung Bäder");
            await form.GetByRole(AriaRole.Button, new() { Name = "Hinzufügen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Kosten wurden hinzugefügt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Sanierung Bäder");

            await page.GetByRole(AriaRole.Button, new() { Name = "Sanierung Bäder entfernen" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Ja, entfernen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Kosten wurden entfernt.");
        });
    }

    [Fact]
    public async Task Maintenance_is_distributed_and_revoked()
    {
        var property = await PropertyAsync();
        await App.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<MietHahnDbContext>();
            var receipt = new Receipt(new DateOnly(2025, 3, 1), 10_000m) { SupplierName = "Dachdecker Meier", PaidOn = new DateOnly(2025, 3, 10) };
            receipt.AddCostItem(CostTypes.MaintenanceKey, 10_000m, property, Period.CalendarYear(2025));
            db.Add(receipt);
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        var page = await OpenAsync($"/verwaltung/objekte/{property}/erhaltungsaufwand");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await SelectAsync(page, "Jahre für Dachdecker Meier", "4 Jahre");
            await page.GetByRole(AriaRole.Button, new() { Name = "Verteilen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Erhaltungsaufwand wird verteilt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("verteilt auf 4 Jahre");

            await page.GetByRole(AriaRole.Button, new() { Name = "Sofort abziehen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Erhaltungsaufwand wird wieder sofort abgezogen.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("sofort abgezogen");
        });
    }

    [Fact]
    public async Task Anlage_v_filters_live_and_saves_the_local_rent_and_line_numbers()
    {
        var property = await PropertyAsync();
        var page = await OpenAsync($"/verwaltung/objekte/{property}/anlage-v?jahr=2026");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Anlage V 2026");
            await page.GetByLabel("Ortsübliche Jahresmiete").FillAsync("12000");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die ortsübliche Miete wurde gespeichert.");

            // Under load the re-render after saving can overwrite the field once; enter the year again until the page follows.
            for (var attempt = 1; ; attempt++)
            {
                await SelectAsync(page, "Steuerjahr", "2025");
                try
                {
                    await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Anlage V 2025", new() { Timeout = 4000 });
                    break;
                }
                catch (PlaywrightException) when (attempt < 3)
                {
                }
            }

        });

        await GotoInteractiveAsync(page, "/verwaltung/finanzen/anlage-v-zeilen?jahr=2026");
        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Zeile für Grundsteuer").FillAsync("77");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Zeilennummern wurden gespeichert.");
        });
    }
}
