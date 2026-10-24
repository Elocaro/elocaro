using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Bank;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Bank;
using MietHahn.Domain.Common;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Operating the interactive bank pages: accounts, import with preview, ignoring, assigning, rules, CSV profiles.</summary>
public class BankBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string TenantIban = "DE89370400440532013000";

    private static byte[] TestFile(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Bank", "TestFiles", name));

    private async Task<(Guid Property, Guid Account)> SetupAsync()
    {
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var previousMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var people = sp.GetRequiredService<IPersonService>();
            var tenant = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            await people.AddBankDetailsAsync(tenant, new BankDetailsInput { Iban = TenantIban }, ct);
            await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = previousMonth, TenantIds = [tenant], BaseRent = 650m }, ct);
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = previousMonth }, ct);
            await sp.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, ct);
            var account = (await sp.GetRequiredService<IBankService>().CreateAccountAsync(
                new BankAccountInput { Name = "Mietkonto", Iban = "DE12500105170648489890", PropertyIds = [property] }, ct)).Id!.Value;
            return (property, account);
        });
    }

    private async Task<Guid> TransactionAsync(Guid account, DateOnly date, decimal amount, string? name, string? iban, string purpose)
    {
        var id = Guid.Empty;
        await App.WithDbAsync(async db =>
        {
            var transaction = new Transaction(account, null, new ParsedTransaction(date, amount, purpose, CounterpartyName: name, CounterpartyIban: iban), Guid.NewGuid().ToString("N"));
            db.Add(transaction);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = transaction.Id;
        });
        return id;
    }

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, path);
        return page;
    }

    [Fact]
    public async Task Account_is_created_a_statement_imported_with_preview_a_transaction_ignored_and_a_duplicate_discarded()
    {
        var page = await OpenAsync("/verwaltung/bank/konten/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Mietkonto");
            await page.GetByLabel("IBAN").FillAsync("DE89 3704 0044 0532 0130 00");
            await SelectAsync(page, "Art", "Mietkonto");
            await page.GetByRole(AriaRole.Button, new() { Name = "Anlegen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/konten/*?meldung=angelegt");
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Mietkonto"); // the new page, not the old one
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            await UploadFileAsync(page, "februar.xml", TestFile("camt053_v02.xml"), "application/xml");
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschau anzeigen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/importe/*");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("4 neue Umsätze");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Nicht gebuchter Umsatz übersprungen");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Miete Februar 2026 Wohnung 1 Lindenweg 3");

            await page.GetByRole(AriaRole.Button, new() { Name = "4 Umsätze importieren" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/umsaetze?konto=*");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await ExpectSnackbarAsync(page, "Die Umsätze wurden importiert.");
            await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Stadtwerke Musterstadt");

            // Ignore one transaction with a note.
            await page.Locator("button[aria-label*='05.02.2026'][aria-label$='ignorieren']").ClickAsync();
            await page.Locator(".mud-dialog").GetByLabel("Notiz").FillAsync("über Belege");
            await page.Locator(".mud-dialog").GetByRole(AriaRole.Button, new() { Name = "Ignorieren" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Umsatz wird ignoriert.");
            await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Notiz: über Belege");
            await SelectAsync(page, "Status", "offen");
            await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);

            // The same file again: nothing new, the import is blocked, discarding works.
            await page.Locator(".tab-bar").GetByRole(AriaRole.Link, new() { Name = "Konten" }).ClickAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Mietkonto" }).First.ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/konten/*");
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Mietkonto");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await UploadFileAsync(page, "februar.xml", TestFile("camt053_v02.xml"), "application/xml");
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschau anzeigen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/importe/*");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("0 neue Umsätze, 4 schon vorhanden");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "0 Umsätze importieren" })).ToBeDisabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Verwerfen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/konten/*?meldung=verworfen");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("februar.xml"); // import log
        });
    }

    [Fact]
    public async Task Unknown_file_is_reported_and_a_custom_CSV_profile_makes_it_readable()
    {
        var (_, account) = await SetupAsync();
        var page = await OpenAsync($"/verwaltung/bank/konten/{account}");
        var csv = "Date,Payee,Memo,Amount\n2026-02-02,Anna Schmidt,Miete,770.00\n"u8.ToArray();

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await UploadFileAsync(page, "export.csv", csv);
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschau anzeigen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Das Dateiformat wurde nicht erkannt.");

            await GotoInteractiveAsync(page, "/verwaltung/bank/csv-profile");
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Hausbank");
            await page.GetByLabel("Trennzeichen", new() { Exact = true }).FillAsync(",");
            await SelectAsync(page, "Dezimaltrennzeichen", "Punkt (1,234.56)");
            await page.GetByLabel("Datumsformat").FillAsync("yyyy-MM-dd");
            await page.GetByLabel("Buchungsdatum").FillAsync("Date");
            await page.GetByLabel("Betrag", new() { Exact = true }).FillAsync("Amount");
            await page.GetByLabel("Name Gegenseite").FillAsync("Payee");
            await page.GetByLabel("Verwendungszweck").FillAsync("Memo");
            await page.GetByRole(AriaRole.Button, new() { Name = "Profil anlegen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Das Profil wurde angelegt.");

            await GotoInteractiveAsync(page, $"/verwaltung/bank/konten/{account}");
            await UploadFileAsync(page, "export.csv", csv);
            await SelectAsync(page, "CSV-Profil", "Hausbank");
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschau anzeigen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/importe/*");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("1 neue Umsätze");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("CSV (Hausbank)");
        });
    }

    [Fact]
    public async Task Inbox_confirms_a_suggestion_and_the_assignment_can_be_undone()
    {
        var (_, account) = await SetupAsync();
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var id = await TransactionAsync(account, today, 650m, "SCHMIDT, ANNA", TenantIban, $"Miete {today:MM}/{today:yy} Wohnung 1");
        var page = await OpenAsync("/verwaltung/bank/zuordnung");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Gegen-IBAN gehört zum Mieter");
            await page.GetByRole(AriaRole.Button, new() { Name = "bestätigen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Vorschlag wurde übernommen.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Alle Umsätze sind zugeordnet.");

            await GotoInteractiveAsync(page, $"/verwaltung/bank/umsaetze/{id}/zuordnen");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Zahlung");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zuordnung rückgängig machen" }).ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, rückgängig machen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Zuordnung wurde rückgängig gemacht.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Manuell zuordnen oder aufteilen");
        });
    }

    [Fact]
    public async Task Expense_is_split_into_two_cost_types_and_the_sum_must_match_exactly()
    {
        var (property, account) = await SetupAsync();
        var id = await TransactionAsync(account, new DateOnly(2026, 3, 5), -300.50m, "Stadtwerke Musterstadt", null, "Abrechnung Wasser Abwasser");
        var page = await OpenAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");
        _ = property;

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Betrag", new() { Exact = true }).FillAsync("200,33");
            await SelectAsync(page, "Kostenart", "2. Wasserversorgung");
            await SelectAsync(page, "Objekt", "Lindenweg 3");
            await page.GetByRole(AriaRole.Button, new() { Name = "Teil hinzufügen" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Teil 2")).ToBeVisibleAsync();

            var part2 = page.Locator("fieldset", new() { HasText = "Teil 2" });
            await part2.GetByLabel("Betrag", new() { Exact = true }).FillAsync("100,16");
            await SelectAsync(page, "Kostenart", "3. Entwässerung", part2);
            await SelectAsync(page, "Objekt", "Lindenweg 3", part2);
            await page.GetByRole(AriaRole.Button, new() { Name = "Zuordnen", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Die Teilbeträge ergeben 300,49 €, der Umsatz beträgt 300,50 € (Differenz 0,01 €)");

            await part2.GetByLabel("Betrag", new() { Exact = true }).FillAsync("");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zuordnen", Exact = true }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Umsatz wurde zugeordnet.");
            await Assertions.Expect(page.Locator("table.data-table").First).ToContainTextAsync("Wasserversorgung");
            await Assertions.Expect(page.Locator("table.data-table").First).ToContainTextAsync("100,17 €");
        });
    }

    [Fact]
    public async Task Collective_payment_is_assigned_to_two_charges()
    {
        var (_, account) = await SetupAsync();
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var id = await TransactionAsync(account, today, 1300m, "Anna Schmidt", null, "Miete zwei Monate");
        var page = await OpenAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Teil hinzufügen" }).ClickAsync();
            var first = page.Locator("fieldset", new() { HasText = "Teil 1" });
            var second = page.Locator("fieldset", new() { HasText = "Teil 2" });
            await first.GetByLabel("Betrag", new() { Exact = true }).FillAsync("650");
            await first.Locator(".mud-select", new() { Has = page.Locator("[aria-label='Mietverhältnis oder Forderung']") }).Locator(".mud-input").First.ClickAsync();
            await page.Locator(".mud-popover-open .mud-list-item", new() { HasText = "Forderung" }).First.ClickAsync();
            await second.Locator(".mud-select", new() { Has = page.Locator("[aria-label='Mietverhältnis oder Forderung']") }).Locator(".mud-input").First.ClickAsync();
            await page.Locator(".mud-popover-open .mud-list-item", new() { HasText = "Forderung" }).Nth(1).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Zuordnen", Exact = true }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Umsatz wurde zugeordnet.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("auf Forderung");
            await Assertions.Expect(page.Locator("tfoot")).ToContainTextAsync("1.300,00 €");
        });
    }

    [Fact]
    public async Task Rule_is_created_from_a_transaction_applied_in_the_inbox_and_deleted()
    {
        var (property, account) = await SetupAsync();
        var id = await TransactionAsync(account, new DateOnly(2026, 3, 5), -88m, "Stadtwerke Musterstadt", "DE02120300000000202051", "Abschlag Allgemeinstrom");
        var page = await OpenAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");
        _ = property;

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "Regel aus diesem Umsatz anlegen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/regeln/neu?*");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.GetByLabel("Gegen-IBAN")).ToHaveValueAsync("DE02120300000000202051");

            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte die Kostenart wählen.");

            await page.GetByLabel("Name der Regel").FillAsync("Stadtwerke Allgemeinstrom");
            await SelectAsync(page, "Kostenart (Ausgabe)", "11. Beleuchtung (Allgemeinstrom)");
            await SelectAsync(page, "Objekt (Ausgabe)", "Lindenweg 3");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/bank/regeln?meldung=gespeichert");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Stadtwerke Allgemeinstrom");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Beleuchtung (Allgemeinstrom)");

            await GotoInteractiveAsync(page, "/verwaltung/bank/zuordnung");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Regel „Stadtwerke Allgemeinstrom“");
            await page.GetByRole(AriaRole.Button, new() { Name = "bestätigen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Der Vorschlag wurde übernommen.");

            await GotoInteractiveAsync(page, "/verwaltung/bank/regeln");
            await page.Locator("button[aria-label$='löschen']").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Regel wurde gelöscht.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Regeln.");
        });
    }
}
