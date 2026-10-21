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

    // Operating the interactive pages (confirm, split, rules, import) is covered by BankBrowserTests.
    [Fact]
    public async Task Inbox_shows_the_suggestion_with_its_reasons_and_the_assignment_page_lists_the_booking()
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
        Assert.Contains("/verwaltung/bank/zuordnung", (await admin.GetAsync("/verwaltung")).Document.Body!.InnerHtml, StringComparison.Ordinal);

        Assert.True((await App.WithScopeAsync(sp => sp.GetRequiredService<IPaymentAssignment>().AcceptAsync(id, 0, TestContext.Current.CancellationToken))).Succeeded);
        Assert.Contains("Alle Umsätze sind zugeordnet.", (await admin.GetAsync("/verwaltung/bank/zuordnung")).Text, StringComparison.Ordinal);

        var page = await admin.GetAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen");
        Assert.Contains("zugeordnet", page.Text, StringComparison.Ordinal);
        Assert.Contains("Zahlung", page.Document.QuerySelector("table.data-table")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Änderungshistorie", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Payments_without_receipt_are_filtered_and_an_assigned_expense_creates_a_receipt()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var setup = await SetupAsync();
        var id = await TransactionAsync(setup.Account, new DateOnly(2026, 3, 5), -300.50m, "Stadtwerke Musterstadt", null, "Abrechnung Wasser Abwasser");

        var result = await App.WithScopeAsync(sp => sp.GetRequiredService<IPaymentAssignment>().AssignAsync(id,
        [
            new AssignmentPartInput { Amount = 200.33m, CostType = "betrkv-02", PropertyId = setup.Property, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) },
            new AssignmentPartInput { CostType = "betrkv-03", PropertyId = setup.Property, Allocable = false },
        ], TestContext.Current.CancellationToken));
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));

        var table = (await admin.GetAsync($"/verwaltung/bank/umsaetze/{id}/zuordnen")).Document.QuerySelector("table.data-table")!.TextContent;
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
