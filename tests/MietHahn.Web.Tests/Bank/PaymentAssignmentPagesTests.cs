using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Bank;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Bank;
using MietHahn.Domain.Common;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Bank;

public class PaymentAssignmentPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string TenantIban = "DE89370400440532013000";

    [Fact]
    public async Task Inbox_confirms_suggestion_shows_assignment_and_undo_reopens_the_transaction()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var setup = await SetupAsync();
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var id = await TransactionAsync(setup.Account, today, 650m, "SCHMIDT, ANNA", TenantIban, $"Miete {today:MM}/{today:yy} Wohnung 1");

        var inbox = await admin.GetAsync("/verwaltung/bank/zuordnung");
        Assert.Contains("SCHMIDT, ANNA", inbox.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt", inbox.Text, StringComparison.Ordinal);
        Assert.Contains("Gegen-IBAN gehört zum Mieter", inbox.Text, StringComparison.Ordinal);
        Assert.Contains("Umsätze zuordnen", (await admin.GetAsync("/verwaltung")).Text, StringComparison.Ordinal);

        inbox = await admin.SubmitAsync(inbox, "inbox", new Dictionary<string, string> { ["Action"] = $"bestaetigen:{id}" });
        Assert.Contains("Der Vorschlag wurde übernommen.", inbox.Text, StringComparison.Ordinal);
        Assert.Contains("Alle Umsätze sind zugeordnet.", inbox.Text, StringComparison.Ordinal);

        var page = await admin.GetAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");
        Assert.Contains("zugeordnet", page.Text, StringComparison.Ordinal);
        Assert.Contains("Zahlung", page.Document.QuerySelector("table.data-table")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Änderungshistorie", page.Text, StringComparison.Ordinal);

        page = await admin.SubmitAsync(page, "unassign");
        Assert.Contains("Die Zuordnung wurde rückgängig gemacht.", page.Text, StringComparison.Ordinal);
        Assert.Contains("offen", page.Text, StringComparison.Ordinal);
        Assert.Contains("Manuell zuordnen oder aufteilen", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expense_is_split_into_two_cost_types_exact_sum_is_validated_and_filter_shows_payments_without_receipt()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var setup = await SetupAsync();
        var id = await TransactionAsync(setup.Account, new DateOnly(2026, 3, 5), -300.50m, "Stadtwerke Musterstadt", null, "Abrechnung Wasser Abwasser");
        var page = await admin.GetAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");
        Assert.Contains("Kostenart", page.Text, StringComparison.Ordinal);

        // Add a second part (keeps what was entered), then entries
        page = await admin.SubmitAsync(page, "assign", new Dictionary<string, string> { ["Action"] = "row", ["Parts[0].Amount"] = "200.33" });
        Assert.NotNull(page.Document.QuerySelector("[name='Parts[1].CostType']"));

        var fields = new Dictionary<string, string>
        {
            ["Action"] = "assign",
            ["Parts[0].Amount"] = "200.33",
            ["Parts[0].CostType"] = "betrkv-02",
            ["Parts[0].PropertyId"] = setup.Property.ToString(),
            ["Parts[0].From"] = "2025-01-01",
            ["Parts[0].To"] = "2025-12-31",
            ["Parts[1].Amount"] = "100.16",
            ["Parts[1].CostType"] = "betrkv-03",
            ["Parts[1].PropertyId"] = setup.Property.ToString(),
            ["Parts[1].Allocable"] = "false",
        };
        var wrong = await admin.SubmitAsync(page, "assign", fields);
        Assert.Contains("Die Teilbeträge ergeben 300,49 €, der Umsatz beträgt 300,50 € (Differenz 0,01 €)", wrong.Text, StringComparison.Ordinal);
        Assert.Contains("Manuell zuordnen oder aufteilen", wrong.Text, StringComparison.Ordinal); // not assigned

        fields["Parts[1].Amount"] = ""; // remainder
        var done = await admin.SubmitAsync(page, "assign", fields);
        Assert.Contains("Der Umsatz wurde zugeordnet.", done.Text, StringComparison.Ordinal);
        var table = done.Document.QuerySelector("table.data-table")!.TextContent;
        Assert.Contains("Wasserversorgung", table, StringComparison.Ordinal);
        Assert.Contains("Entwässerung", table, StringComparison.Ordinal);
        Assert.Contains("200,33 €", table, StringComparison.Ordinal);
        Assert.Contains("100,17 €", table, StringComparison.Ordinal);
        Assert.Contains("Beleg ohne Datei", table, StringComparison.Ordinal);

        var filtered = await admin.GetAsync($"/verwaltung/bank/umsaetze?konto={setup.Account}&ohneBeleg=true");
        Assert.Contains("Stadtwerke Musterstadt", filtered.Text, StringComparison.Ordinal);
        Assert.Single(filtered.Document.QuerySelectorAll("tbody tr"));

        var receipts = await admin.GetAsync("/verwaltung/belege");
        Assert.Contains("Stadtwerke Musterstadt", receipts.Text, StringComparison.Ordinal);
        Assert.Contains("300,50 €", receipts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collective_payment_is_assigned_to_two_charges_via_manual_form()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var setup = await SetupAsync();
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var id = await TransactionAsync(setup.Account, today, 1300m, "Anna Schmidt", null, "Miete zwei Monate");

        var page = await admin.GetAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");
        var options = page.Document.QuerySelectorAll("#part-0-target option").Where(o => o.GetAttribute("value")!.StartsWith("C:", StringComparison.Ordinal)).ToList();
        Assert.True(options.Count >= 2, "Mindestens zwei offene Forderungen erwartet.");

        page = await admin.SubmitAsync(page, "assign", new Dictionary<string, string> { ["Action"] = "row" });
        var done = await admin.SubmitAsync(page, "assign", new Dictionary<string, string>
        {
            ["Action"] = "assign",
            ["Parts[0].Amount"] = "650",
            ["Parts[0].Target"] = options[0].GetAttribute("value")!,
            ["Parts[1].Target"] = options[1].GetAttribute("value")!,
        });
        Assert.Contains("Der Umsatz wurde zugeordnet.", done.Text, StringComparison.Ordinal);
        Assert.Contains("auf Forderung", done.Text, StringComparison.Ordinal);
        Assert.Contains("1.300,00 €", done.Document.QuerySelector("tfoot")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rule_is_created_from_the_transaction_applied_in_the_inbox_and_deleted()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var setup = await SetupAsync();
        var id = await TransactionAsync(setup.Account, new DateOnly(2026, 3, 5), -88m, "Stadtwerke Musterstadt", "DE02120300000000202051", "Abschlag Allgemeinstrom");

        var transaction = await admin.GetAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");
        var link = transaction.Document.QuerySelectorAll("a").Single(a => a.TextContent.Contains("Regel aus diesem Umsatz", StringComparison.Ordinal)).GetAttribute("href")!;
        var form = await admin.GetAsync(link);
        Assert.Equal("DE02120300000000202051", ((AngleSharp.Html.Dom.IHtmlInputElement)form.Document.QuerySelector("#rule-iban")!).Value);

        var invalid = await admin.SubmitAsync(form, "rule", new Dictionary<string, string> { ["Data.Name"] = "Strom", ["Data.Action"] = "Expense" });
        Assert.Contains("Bitte die Kostenart wählen.", invalid.Text, StringComparison.Ordinal);

        var saved = await admin.SubmitAsync(form, "rule", new Dictionary<string, string>
        {
            ["Data.Name"] = "Stadtwerke Allgemeinstrom",
            ["Data.Action"] = "Expense",
            ["Data.CostType"] = "betrkv-11",
            ["Data.PropertyId"] = setup.Property.ToString(),
        });
        Assert.Contains("Die Regel wurde gespeichert.", saved.Text, StringComparison.Ordinal);
        Assert.Contains("Stadtwerke Allgemeinstrom", saved.Text, StringComparison.Ordinal);
        Assert.Contains("Beleuchtung (Allgemeinstrom)", saved.Text, StringComparison.Ordinal);

        var inbox = await admin.GetAsync("/verwaltung/bank/zuordnung");
        Assert.Contains("Regel „Stadtwerke Allgemeinstrom“", inbox.Text, StringComparison.Ordinal);
        Assert.Contains("Beleuchtung (Allgemeinstrom) · Lindenweg 3", inbox.Text, StringComparison.Ordinal);
        inbox = await admin.SubmitAsync(inbox, "inbox", new Dictionary<string, string> { ["Action"] = $"bestaetigen:{id}" });
        Assert.Contains("Der Vorschlag wurde übernommen.", inbox.Text, StringComparison.Ordinal);

        var rules = await admin.GetAsync("/verwaltung/bank/regeln");
        var button = rules.Form("delete").QuerySelector("button[name=DeleteId]")!;
        rules = await admin.SubmitAsync(rules, "delete", new Dictionary<string, string> { ["DeleteId"] = button.GetAttribute("value")! });
        Assert.Contains("Die Regel wurde gelöscht.", rules.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Regeln.", rules.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_finance_permission_no_assignment_pages()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;

        foreach (var url in new[] { "/verwaltung/bank/zuordnung", "/verwaltung/bank/regeln", "/verwaltung/bank/regeln/neu" })
        {
            Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync(url, followRedirects: false)).RedirectLocation!.AbsolutePath);
        }
    }

    private async Task<(Guid Property, Guid Account, Guid Lease)> SetupAsync()
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
            var lease = (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = previousMonth, TenantIds = [tenant], BaseRent = 650m }, ct)).Id!.Value;
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = previousMonth }, ct);
            await sp.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, ct);
            var account = (await sp.GetRequiredService<IBankService>().CreateAccountAsync(
                new BankAccountInput { Name = "Mietkonto", Iban = "DE12500105170648489890", PropertyIds = [property] }, ct)).Id!.Value;
            return (property, account, lease);
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
}
