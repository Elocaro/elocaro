using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Documents;
using MietHahn.Domain.Common;

namespace MietHahn.Web.Tests.Browser;

/// <summary>
/// The first hour of a new installation as a landlord lives it: setup wizard, person, lease, contract document, invitation, bank import and
/// assigning a payment. No step may end in an error message; this guards the work of epic V1 ("die erste Stunde").
/// </summary>
public class FirstHourBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    protected override bool SetupCompleted => false;

    protected override void ConfigureServices(IServiceCollection services) => services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    private static string German(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>No error box (Notice with level Error) in the content of the current page.</summary>
    private static async Task NoErrorAsync(IPage page)
    {
        await page.WaitForTimeoutAsync(300);
        var errors = await page.Locator("main .message.message-error").AllInnerTextsAsync();
        Assert.True(errors.Count == 0, $"Fehlermeldung auf {page.Url}: {string.Join(" | ", errors)}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task From_the_setup_wizard_to_an_assigned_rent_payment_without_a_single_error(bool mobile)
    {
        var page = await SignedInAsAdminAsync(mobile);
        var firstOfLastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // 1. Setup wizard: profile, first property with area, books from last month, default templates.
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            var next = page.GetByRole(AriaRole.Button, new() { Name = "Weiter", Exact = true });
            await next.ClickAsync(); // welcome -> modules
            await next.ClickAsync(); // modules -> options
            await next.ClickAsync(); // options -> sender
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Hausverwaltung Beispiel");
            await page.GetByLabel("Straße und Hausnummer", new() { Exact = true }).FillAsync("Weg 1");
            await page.GetByLabel("PLZ", new() { Exact = true }).FillAsync("50667");
            await page.GetByLabel("Ort", new() { Exact = true }).FillAsync("Köln");
            await next.ClickAsync();
            await page.GetByLabel("Bezeichnung", new() { Exact = true }).FillAsync("Lindenweg 3");
            await page.GetByLabel("Straße und Hausnummer", new() { Exact = true }).FillAsync("Lindenweg 3");
            await page.GetByLabel("PLZ", new() { Exact = true }).FillAsync("50667");
            await page.GetByLabel("Ort", new() { Exact = true }).FillAsync("Köln");
            await page.GetByLabel("Wohnung 1", new() { Exact = true }).FillAsync("Wohnung 1");
            await page.GetByLabel("Wohnfläche 1").FillAsync("55");
            await next.ClickAsync();
            await page.GetByLabel("Buchhaltung ab (Monat)").FillAsync($"{firstOfLastMonth:MM}.{firstOfLastMonth:yyyy}");
            await page.GetByLabel("Buchhaltung ab (Monat)").PressAsync("Tab");
            await next.ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Einrichtung abschließen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung");

            // 2. The dashboard names the next step; follow it: the first tenant.
            var main = page.Locator("main");
            await Assertions.Expect(main).ToContainTextAsync("Ersten Mieter anlegen");
            await Assertions.Expect(main).Not.ToContainTextAsync("Alles erledigt");
            await main.GetByRole(AriaRole.Link, new() { Name = "Ersten Mieter anlegen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/personen/neu");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await page.GetByLabel("Vorname").FillAsync("Anna");
            await page.GetByLabel("Nachname bzw. Name").FillAsync("Beispiel");
            await page.GetByLabel("E-Mail").FillAsync("anna@example.de");
            await page.GetByRole(AriaRole.Button, new() { Name = "Person anlegen", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/personen/*?meldung=angelegt");
            await NoErrorAsync(page);

            // 3. Next step: the first lease, typed the way people type dates.
            await GotoInteractiveAsync(page, "/verwaltung/vertraege/neu");
            await SelectAsync(page, "Mietobjekt", "Wohnung 1", exact: false);
            await SelectAsync(page, "Mieter", "Anna Beispiel");
            var start = page.GetByLabel("Mietbeginn");
            await start.FillAsync(firstOfLastMonth.ToString("ddMMyyyy", CultureInfo.InvariantCulture));
            await start.PressAsync("Tab");
            await Assertions.Expect(start).ToHaveValueAsync(German(firstOfLastMonth));
            await page.GetByLabel("Kaltmiete").FillAsync("650");
            await page.GetByRole(AriaRole.Button, new() { Name = "Vertrag anlegen", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/vertraege/*?meldung=angelegt");
            await NoErrorAsync(page);
            var leaseUrl = new Uri(page.Url).AbsolutePath;

            // 4. Monthly rents exist from the accounting start on.
            await GotoInteractiveAsync(page, leaseUrl + "/konto");
            await page.GetByRole(AriaRole.Button, new() { Name = "Monatsmieten aktualisieren" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Sollstellungen angelegt");

            // 5. Contract document: nothing missing, so the draft can be created at once.
            await GotoInteractiveAsync(page, leaseUrl + "/vertragsdokument");
            await Assertions.Expect(page.Locator("#readiness-landlordprofile")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#readiness-template")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#readiness-livingarea")).ToHaveCountAsync(0);
            await page.GetByRole(AriaRole.Button, new() { Name = "Entwurf als PDF erstellen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Entwurf");
            await NoErrorAsync(page);

            // 6. Invite the tenant to the portal (with the confirmation) and see the state.
            Guid tenant = Guid.Empty;
            await App.WithDbAsync(async db =>
                tenant = (await db.Set<MietHahn.Domain.People.Person>().SingleAsync(p => p.LastName == "Beispiel", TestContext.Current.CancellationToken)).Id);
            await GotoInteractiveAsync(page, $"/verwaltung/personen/{tenant}");
            await OpenTabAsync(page, "Zugang");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zugang einrichten und einladen" }).ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, einladen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Zugang wurde eingerichtet.");
            await Assertions.Expect(page.Locator("#access-state")).ToContainTextAsync("Einladung wird versendet");

            // 7. Bank: account, statement as CSV with preview, import.
            await GotoInteractiveAsync(page, "/verwaltung/bank/konten/neu");
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Mietkonto Lindenweg");
            await page.GetByLabel("IBAN").FillAsync("DE89370400440532013000");
            await page.GetByRole(AriaRole.Button, new() { Name = "Anlegen", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/konten/*?meldung=angelegt");
            var csv = $"Buchungstag;Valuta;Beguenstigter/Zahlungspflichtiger;Verwendungszweck;Betrag;Waehrung\n{German(Today)};{German(Today)};Anna Beispiel;Miete {firstOfLastMonth:MM}/{firstOfLastMonth:yyyy} Wohnung 1 Lindenweg;650,00;EUR\n";
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            // The file input only reacts once the interactive connection is fully up; offer the file again until the page shows its name.
            for (var attempt = 0; attempt < 10 && await page.Locator(".file-upload-name").CountAsync() == 0; attempt++)
            {
                await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = "umsaetze.csv", MimeType = "text/csv", Buffer = Encoding.UTF8.GetBytes(csv) });
                await page.WaitForTimeoutAsync(500);
            }

            await Assertions.Expect(page.Locator(".file-upload-name")).ToHaveTextAsync("umsaetze.csv");
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschau anzeigen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("650,00 €");
            await page.GetByRole(AriaRole.Button, new() { Name = "1 Umsätze importieren" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/umsaetze**");
            await NoErrorAsync(page);

            // 8. The payment gets an explained suggestion; confirming it clears the inbox.
            await GotoInteractiveAsync(page, "/verwaltung/bank/zuordnung");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Betrag entspricht der offenen Forderung");
            await page.GetByRole(AriaRole.Button, new() { Name = "Bestätigen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Alle Umsätze sind zugeordnet");
            await NoErrorAsync(page);

            // 9. Nothing of the setup is left on the dashboard but the bank-free steps are done: the checklist is gone.
            await page.GotoAsync(BaseUrl + "/verwaltung");
            await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("So geht es los");
        });
    }
}
