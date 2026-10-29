using System.Text;
using Elocaro.Application.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>The spreadsheet import as a landlord uses it: download the template, check a file, fix errors, import.</summary>
public class DataImportBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string Header = "Objekt;Straße;PLZ;Ort;Einheit;Lage;Wohnfläche;Vorname;Nachname;E-Mail;Mietbeginn;Kaltmiete;Betriebskosten;Heizkosten;Kaution;Saldo\r\n";

    [Fact]
    public async Task A_file_with_errors_is_reported_per_row_and_a_corrected_one_is_imported_after_confirmation()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/einstellungen/import");
            var template = await page.APIRequest.GetAsync(BaseUrl + "/verwaltung/einstellungen/import/vorlage.csv");
            Assert.True(template.Ok);
            Assert.Contains("Wohnfläche", await template.TextAsync(), StringComparison.Ordinal);

            // First a file with a typo in the rent: reported, nothing to import.
            var broken = Header + "Lindenweg 3;Lindenweg 3;50667;Köln;Wohnung 1;EG;55,5;Anna;Beispiel;anna@example.de;01.01.2024;sechshundert;150;80;1950;\r\n";
            await UploadFileAsync(page, "bestand.csv", Encoding.UTF8.GetBytes(broken));
            await page.GetByRole(AriaRole.Button, new() { Name = "Prüfen" }).ClickAsync();
            await Assertions.Expect(page.Locator("#import-preview")).ToContainTextAsync("Kaltmiete „sechshundert“ ist keine Zahl");
            await Assertions.Expect(page.Locator("#import-preview")).ToContainTextAsync("1 Zeile(n) mit Fehlern");
            await Assertions.Expect(page.Locator("#import-start")).ToHaveCountAsync(0);
        });

        // The corrected file (a new page load, as the user would pick another file).
        await GotoInteractiveAsync(page, "/verwaltung/einstellungen/import");
        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var fixedFile = Header + "Lindenweg 3;Lindenweg 3;50667;Köln;Wohnung 1;EG;55,5;Anna;Beispiel;anna@example.de;01.01.2024;650;150;80;1950;\r\n" +
                            "Lindenweg 3;;;;Wohnung 2;1. OG;62;;;;;;;;;\r\n";
            await UploadFileAsync(page, "bestand.csv", Encoding.UTF8.GetBytes(fixedFile));
            await page.GetByRole(AriaRole.Button, new() { Name = "Prüfen" }).ClickAsync();
            await Assertions.Expect(page.Locator("#import-preview")).ToContainTextAsync("Objekt neu · Einheit neu · Fläche 55,5 m² · Mieter neu · Vertrag neu ab 01.01.2024");
            await Assertions.Expect(page.Locator("#import-preview")).ToContainTextAsync("2 Einheit(en) neu");

            await page.Locator("#import-start").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, importieren" }).ClickAsync();
            await Assertions.Expect(page.Locator("#import-done")).ToContainTextAsync("1 Objekt(e), 2 Einheit(en), 1 Person(en), 1 Vertrag/Verträge");
        });

        var houses = await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyService>().ListAsync(null, TestContext.Current.CancellationToken));
        var house = Assert.Single(houses);
        Assert.Equal(("Lindenweg 3", 2, 117.5m), (house.Name, house.Units, house.LivingArea ?? 0));
    }

    [Fact]
    public async Task Example_data_can_be_loaded_once_and_fill_the_dashboard()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/einstellungen/import");
            await page.Locator("#demo-start").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, laden" }).ClickAsync();
            // Loading the example data takes a while on a slow runner.
            await Assertions.Expect(page.Locator("#import-done")).ToContainTextAsync("1 Objekt(e), 4 Einheit(en), 3 Person(en), 3 Vertrag/Verträge", new() { Timeout = 90000 });
            await Assertions.Expect(page.Locator("#demo-data")).ToHaveCountAsync(0);

            await page.GotoAsync(BaseUrl + "/verwaltung");
            await Assertions.Expect(page.Locator("#kpis")).ToContainTextAsync("3 von 4 vermietet");
            await Assertions.Expect(page.Locator("#kpis")).Not.ToContainTextAsync("Rückstände0,00");
        });
    }
}
