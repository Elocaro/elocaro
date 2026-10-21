using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Application.Bank;
using MietHahn.Application.Common;
using MietHahn.Application.Files;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Bank;
using MietHahn.Domain.Bank.Matching;
using MietHahn.Domain.Finance;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Bank;

public class PaymentAssignmentTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TenantIban = "DE89 3704 0044 0532 0130 00";
    private const string SupplierIban = "DE02120300000000202051";

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid lease;
    private Guid account;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        Validate(await people.AddBankDetailsAsync(anna, new BankDetailsInput { Iban = TenantIban }, Ct));
        lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 1, 1),
            TenantIds = [anna],
            BaseRent = 620m,
            OperatingCostPrepayment = 150m,
        }, Ct));
        await scope.ServiceProvider.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 1, 1) }, Ct);
        await scope.ServiceProvider.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, Ct);
        account = Validate(await scope.ServiceProvider.GetRequiredService<IBankService>().CreateAccountAsync(
            new BankAccountInput { Name = "Mietkonto", Iban = "DE12500105170648489890", PropertyIds = [house] }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Inbox_suggests_the_lease_and_accepting_creates_the_payment_and_undo_reverts_it_with_audit_entries()
    {
        var id = await TransactionAsync(new DateOnly(2026, 1, 3), 770m, "SCHMIDT, ANNA", TenantIban, "Miete 01/26 Wohnung 1");

        var row = Assert.Single(await WithAsync<IPaymentAssignment, IReadOnlyList<InboxRow>>(a => a.InboxAsync(null, Ct)), r => r.Transaction.Id == id);
        Assert.NotNull(row.Suggestion);
        Assert.Equal(SuggestionKind.Lease, row.Suggestion.Kind);
        Assert.Equal(100, row.Suggestion.Score);
        Assert.Contains("Anna Schmidt", row.Suggestion.Target, StringComparison.Ordinal);
        Assert.Contains(row.Suggestion.Reasons, r => r.StartsWith("Gegen-IBAN", StringComparison.Ordinal));

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AcceptAsync(id, 0, Ct)));

        Assert.Equal(TransactionStatus.Assigned, await StatusAsync(id));
        var entries = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries;
        var payment = Assert.Single(entries, e => e.EntryType == EntryType.Payment);
        Assert.Equal((770m, PaymentOrigin.Bank, new DateOnly(2026, 1, 3)), (payment.Credit, payment.Origin, payment.Date));
        var assignment = Assert.Single(await WithAsync<IPaymentAssignment, IReadOnlyList<AssignmentView>>(a => a.AssignmentsAsync(id, Ct)));
        Assert.Equal(("Zahlung", 770m), (assignment.Kind, assignment.Amount));
        Assert.False((await WithAsync<IPaymentAssignment, Result>(a => a.AcceptAsync(id, 0, Ct))).Succeeded); // already assigned

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.UnassignAsync(id, Ct)));

        Assert.Equal(TransactionStatus.Open, await StatusAsync(id));
        Assert.DoesNotContain((await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries, e => e.EntryType == EntryType.Payment);
        Assert.Empty(await WithAsync<IPaymentAssignment, IReadOnlyList<AssignmentView>>(a => a.AssignmentsAsync(id, Ct)));
        Assert.False((await WithAsync<IPaymentAssignment, Result>(a => a.UnassignAsync(id, Ct))).Succeeded);

        var history = await WithAsync<IChangeHistory, IReadOnlyList<AuditEntryView>>(h => h.GetAsync("Transaction", id, Ct));
        Assert.Contains(history, h => h.Changes.Any(c => c is { Field: "Status", New: "Assigned" }));
        Assert.Contains(history, h => h.Changes.Any(c => c is { Field: "Status", Old: "Assigned", New: "Open" }));
        var log = await WithAsync<IChangeHistory, IReadOnlyList<AuditEntryView>>(h => h.GetAsync("Payment", payment.Id, Ct));
        Assert.Equal([AuditAction.Created, AuditAction.Deleted], log.Select(h => h.Action).OrderBy(a => a));
        var assignments = await WithAsync<IChangeHistory, IReadOnlyList<AuditEntryView>>(h => h.GetAsync("TransactionAssignment", assignment.Id, Ct));
        Assert.Contains(assignments, h => h.Action == AuditAction.Deleted);
    }

    [Fact]
    public async Task Partial_payment_stays_open_and_overpayment_becomes_credit()
    {
        var partial = await TransactionAsync(new DateOnly(2026, 1, 3), 400m, "Anna Schmidt", TenantIban, "Miete Teilzahlung");
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AcceptAsync(partial, 0, Ct)));

        var account1 = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(4 * 770m - 400m, account1.Balance);
        Assert.Equal(370m, (await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct))).Where(p => p.Due.Month == 1).Sum(p => p.Open));

        // Pay everything and 100 € too much: the surplus is a credit.
        var rest = await TransactionAsync(new DateOnly(2026, 4, 3), 4 * 770m - 400m + 100m, "Anna Schmidt", TenantIban, "Ausgleich");
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(rest, [new AssignmentPartInput { LeaseId = lease }], Ct)));

        var account2 = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!;
        Assert.Equal(-100m, account2.Balance);
        Assert.Empty(await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct)));
    }

    [Fact]
    public async Task Collective_payment_for_three_months_settles_the_charges_one_by_one()
    {
        var id = await TransactionAsync(new DateOnly(2026, 3, 20), 3 * 770m, "Anna Schmidt", TenantIban, "Miete Januar bis März 2026");

        var proposal = (await WithAsync<IPaymentAssignment, AssignmentProposal?>(a => a.ProposalAsync(id, Ct)))!;
        var best = proposal.Suggestions[0];
        Assert.Equal(6, best.Parts.Count); // base rent and prepayment per month
        Assert.Equal(3 * 770m, best.Parts.Sum(p => p.Amount));

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AcceptAsync(id, 0, Ct)));

        var open = await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct));
        Assert.All(open, p => Assert.Equal(4, p.Due.Month)); // only April is open
        Assert.Equal(6, (await WithAsync<IPaymentAssignment, IReadOnlyList<AssignmentView>>(a => a.AssignmentsAsync(id, Ct))).Count);

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.UnassignAsync(id, Ct)));
        Assert.Equal(8, (await WithAsync<ITenantAccounts, IReadOnlyList<OpenItem>>(k => k.OpenItemsAsync(Ct))).Count);
    }

    [Fact]
    public async Task Income_split_to_specific_charges_must_add_up_exactly_and_changes_nothing_otherwise()
    {
        var id = await TransactionAsync(new DateOnly(2026, 2, 10), 1000m, "Anna Schmidt", TenantIban, "Zahlung");
        var charges = (await WithAsync<IPaymentAssignment, AssignmentOptions>(a => a.OptionsAsync(Ct))).Charges.Where(c => c.LeaseId == lease).ToList();
        var january = charges.Single(c => c.Text.Contains("Kaltmiete 01/2026", StringComparison.Ordinal));
        var february = charges.Single(c => c.Text.Contains("Kaltmiete 02/2026", StringComparison.Ordinal));

        var wrong = await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id,
            [new AssignmentPartInput { ChargeId = january.Id, Amount = 620m }, new AssignmentPartInput { ChargeId = february.Id, Amount = 379.99m }], Ct));
        Assert.False(wrong.Succeeded);
        Assert.Contains("Differenz 0,01 €", wrong.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(TransactionStatus.Open, await StatusAsync(id));
        Assert.DoesNotContain((await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries, e => e.EntryType == EntryType.Payment);

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id,
            [new AssignmentPartInput { ChargeId = january.Id, Amount = 620m }, new AssignmentPartInput { ChargeId = february.Id }], Ct)));

        var payments = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries.Where(e => e.EntryType == EntryType.Payment).ToList();
        Assert.Equal([380m, 620m], payments.Select(p => p.Credit).Order());
        var entries = (await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries;
        Assert.Equal(0m, entries.Single(e => e.Text == "Kaltmiete 01/2026").Open);
        Assert.Equal(240m, entries.Single(e => e.Text == "Kaltmiete 02/2026").Open);
    }

    [Fact]
    public async Task Expense_split_into_two_cost_types_creates_receipt_with_items_that_add_up_and_can_be_undone()
    {
        var id = await TransactionAsync(new DateOnly(2026, 3, 5), -300.50m, "Stadtwerke Musterstadt", SupplierIban, "Abrechnung Wasser Abwasser");

        var result = await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id,
        [
            new AssignmentPartInput { Amount = 200.33m, CostType = "betrkv-02", PropertyId = house, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) },
            new AssignmentPartInput { CostType = "betrkv-03", PropertyId = house, Allocable = false, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) },
        ], Ct));
        Validate(result);

        var assignments = await WithAsync<IPaymentAssignment, IReadOnlyList<AssignmentView>>(a => a.AssignmentsAsync(id, Ct));
        Assert.Equal([-200.33m, -100.17m], assignments.Select(a => a.Amount));
        Assert.Equal(-300.50m, assignments.Sum(a => a.Amount));
        Assert.All(assignments, a => Assert.True(a.ReceiptWithoutFile));
        var receiptLink = assignments[0].Link!;
        Assert.Equal(receiptLink, assignments[1].Link);
        var receiptId = Guid.Parse(receiptLink[(receiptLink.LastIndexOf('/') + 1)..]);

        var receipt = (await WithAsync<IReceiptService, ReceiptDetails?>(b => b.GetAsync(receiptId, Ct)))!;
        Assert.Equal((300.50m, 300.50m, 0m), (receipt.Data.Gross, receipt.Allocated, receipt.Open));
        Assert.Equal(new DateOnly(2026, 3, 5), receipt.Data.PaidOn);
        Assert.Equal("Stadtwerke Musterstadt", receipt.Supplier);
        Assert.Equal([200.33m, 100.17m], receipt.Items.Select(p => p.Amount));
        Assert.Equal([true, false], receipt.Items.Select(p => p.Allocable));

        // "Zahlungen ohne Beleg": the new receipt has no file yet
        var without = await WithAsync<IBankService, IReadOnlyList<TransactionRow>>(b => b.TransactionsAsync(new TransactionQuery(WithoutReceipt: true), Ct));
        Assert.Equal(id, Assert.Single(without).Id);
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var file = (await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf("rechnung.pdf")), "rechnung.pdf", Ct)).Id;
            Validate(await scope.ServiceProvider.GetRequiredService<IReceiptService>().AddFilesAsync(receiptId, [file], Ct));
        }

        Assert.Empty(await WithAsync<IBankService, IReadOnlyList<TransactionRow>>(b => b.TransactionsAsync(new TransactionQuery(WithoutReceipt: true), Ct)));

        // Undo: items go; the receipt stays because a file was added in the meantime
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.UnassignAsync(id, Ct)));
        var kept = (await WithAsync<IReceiptService, ReceiptDetails?>(b => b.GetAsync(receiptId, Ct)))!;
        Assert.Empty(kept.Items);
        Assert.Equal(TransactionStatus.Open, await StatusAsync(id));
    }

    [Fact]
    public async Task Loan_instalment_is_split_into_interest_from_the_plan_and_repayment_and_can_be_undone()
    {
        var loan = (await WithAsync<MietHahn.Application.Tax.ILoanService, Result>(s => s.CreateAsync(new MietHahn.Application.Tax.LoanInput
        {
            PropertyId = house,
            Bank = "Sparkasse",
            Principal = 120_000m,
            DisbursementDate = new DateOnly(2026, 1, 1),
            FirstPaymentDate = new DateOnly(2026, 2, 1),
            InterestRatePercent = 3m,
            Instalment = 1_000m,
        }, Ct))).Id!.Value;
        var id = await TransactionAsync(new DateOnly(2026, 3, 2), -1_000m, "Sparkasse", SupplierIban, "Darlehen Rate");

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id, [new AssignmentPartInput { LoanId = loan }], Ct)));

        var view = Assert.Single(await WithAsync<IPaymentAssignment, IReadOnlyList<AssignmentView>>(a => a.AssignmentsAsync(id, Ct)));
        Assert.Equal("Darlehensrate", view.Kind);
        Assert.Contains("Sparkasse", view.Target, StringComparison.Ordinal);
        var booked = Assert.Single((await WithAsync<MietHahn.Application.Tax.ILoanService, MietHahn.Application.Tax.LoanDetail?>(s => s.GetAsync(loan, Ct)))!.Booked);
        Assert.Equal(1_000m, booked.Amount);
        Assert.Equal(1_000m, booked.Interest + booked.Repayment);
        Assert.InRange(booked.Interest, 290m, 300m);

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.UnassignAsync(id, Ct)));
        Assert.Empty((await WithAsync<MietHahn.Application.Tax.ILoanService, MietHahn.Application.Tax.LoanDetail?>(s => s.GetAsync(loan, Ct)))!.Booked);
    }

    [Fact]
    public async Task Loan_instalment_takes_the_given_interest_and_rejects_impossible_ones()
    {
        var loan = (await WithAsync<MietHahn.Application.Tax.ILoanService, Result>(s => s.CreateAsync(new MietHahn.Application.Tax.LoanInput
        {
            PropertyId = house,
            Bank = "Sparkasse",
            Principal = 120_000m,
            DisbursementDate = new DateOnly(2026, 1, 1),
            FirstPaymentDate = new DateOnly(2026, 2, 1),
            InterestRatePercent = 3m,
            Instalment = 1_000m,
        }, Ct))).Id!.Value;
        var far = await TransactionAsync(new DateOnly(2055, 7, 15), -1_000m, "Sparkasse", SupplierIban, "Rate");
        var income = await TransactionAsync(new DateOnly(2026, 3, 2), 500m, "Sparkasse", SupplierIban, "Gutschrift");

        Assert.Contains("Zinsanteil", (await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(far, [new AssignmentPartInput { LoanId = loan }], Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.False((await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(far, [new AssignmentPartInput { LoanId = loan, LoanInterest = 1_500m }], Ct))).Succeeded);
        Assert.False((await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(income, [new AssignmentPartInput { LoanId = loan, LoanInterest = 10m }], Ct))).Succeeded);

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(far, [new AssignmentPartInput { LoanId = loan, LoanInterest = 250m }], Ct)));
        var booked = Assert.Single((await WithAsync<MietHahn.Application.Tax.ILoanService, MietHahn.Application.Tax.LoanDetail?>(s => s.GetAsync(loan, Ct)))!.Booked);
        Assert.Equal((250m, 750m), (booked.Interest, booked.Repayment));
    }

    [Fact]
    public async Task Undo_removes_the_receipt_created_with_the_assignment()
    {
        var id = await TransactionAsync(new DateOnly(2026, 3, 5), -85m, "Stadtwerke Musterstadt", SupplierIban, "Abschlag Strom");
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id, [new AssignmentPartInput { CostType = "betrkv-11", PropertyId = house }], Ct)));
        Assert.Single(await WithAsync<IReceiptService, IReadOnlyList<ReceiptRow>>(b => b.SearchAsync(new ReceiptQuery(), Ct)));

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.UnassignAsync(id, Ct)));

        Assert.Empty(await WithAsync<IReceiptService, IReadOnlyList<ReceiptRow>>(b => b.SearchAsync(new ReceiptQuery(), Ct)));
    }

    [Fact]
    public async Task Expense_can_be_added_to_an_existing_receipt_and_the_rest_is_validated()
    {
        var receiptId = Validate(await WithAsync<IReceiptService, Result>(b => b.CreateAsync(new ReceiptInput
        {
            SupplierName = "Gärtner Grün",
            InvoiceDate = new DateOnly(2026, 2, 20),
            Gross = 120m,
        }, [], null, Ct)));
        var id = await TransactionAsync(new DateOnly(2026, 3, 5), -120m, "Gaertner Gruen", null, "RE 2026-17");

        var two = await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id,
            [new AssignmentPartInput { Amount = 100m, CostType = "betrkv-10", PropertyId = house, ReceiptId = receiptId }, new AssignmentPartInput { CostType = "betrkv-10", PropertyId = house, ReceiptId = receiptId }], Ct));
        Assert.True(two.Succeeded, string.Join(", ", two.Errors));

        var receipt = (await WithAsync<IReceiptService, ReceiptDetails?>(b => b.GetAsync(receiptId, Ct)))!;
        Assert.Equal(120m, receipt.Allocated);
        Assert.Equal(new DateOnly(2026, 3, 5), receipt.Data.PaidOn);

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.UnassignAsync(id, Ct)));

        receipt = (await WithAsync<IReceiptService, ReceiptDetails?>(b => b.GetAsync(receiptId, Ct)))!;
        Assert.Equal((0m, null), (receipt.Allocated, receipt.Data.PaidOn));

        // Invalid combinations: income to cost and cost without property
        var missing = await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id, [new AssignmentPartInput { CostType = "betrkv-10" }], Ct));
        Assert.Contains("Objekt", missing.Errors.Single(), StringComparison.Ordinal);
        var both = await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id, [new AssignmentPartInput { CostType = "betrkv-10", PropertyId = house, LeaseId = lease }], Ct));
        Assert.Contains("nicht beides", both.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(TransactionStatus.Open, await StatusAsync(id));
    }

    [Fact]
    public async Task Rules_apply_before_scoring_and_can_ignore_transactions()
    {
        var wasser = Validate(await WithAsync<IMatchRules, Result>(r => r.SaveAsync(null, new MatchRuleInput
        {
            Name = "Stadtwerke Wasser",
            CounterpartyContains = "Stadtwerke",
            PurposeContains = "Wasser",
            Action = RuleAction.Expense,
            CostType = "betrkv-02",
            PropertyId = house,
        }, Ct)));
        Validate(await WithAsync<IMatchRules, Result>(r => r.SaveAsync(null, new MatchRuleInput
        {
            Name = "Privatentnahme",
            PurposeContains = "privat",
            Action = RuleAction.Ignore,
        }, Ct)));
        var bad = await WithAsync<IMatchRules, Result>(r => r.SaveAsync(null, new MatchRuleInput { Name = "Leer", Action = RuleAction.Ignore }, Ct));
        Assert.Contains("mindestens eine Bedingung", bad.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(["Privatentnahme", "Stadtwerke Wasser"], (await WithAsync<IMatchRules, IReadOnlyList<MatchRuleRow>>(r => r.ListAsync(Ct))).Select(r => r.Name).Order());

        var water = await TransactionAsync(new DateOnly(2026, 3, 5), -88m, "Stadtwerke Musterstadt", SupplierIban, "Abschlag Wasser");
        var privateTx = await TransactionAsync(new DateOnly(2026, 3, 6), -50m, "Amazon", null, "privat Bestellung");

        var inbox = await WithAsync<IPaymentAssignment, IReadOnlyList<InboxRow>>(a => a.InboxAsync(account, Ct));
        var waterRow = inbox.Single(r => r.Transaction.Id == water);
        Assert.Equal((SuggestionKind.Expense, 100), (waterRow.Suggestion!.Kind, waterRow.Suggestion.Score));
        Assert.Contains("Wasserversorgung", waterRow.Suggestion.Target, StringComparison.Ordinal);
        Assert.Contains("Lindenweg 3", waterRow.Suggestion.Target, StringComparison.Ordinal);
        Assert.Equal(SuggestionKind.Ignore, inbox.Single(r => r.Transaction.Id == privateTx).Suggestion!.Kind);

        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AcceptAsync(privateTx, 0, Ct)));
        Assert.Equal(TransactionStatus.Ignored, await StatusAsync(privateTx));
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AcceptAsync(water, 0, Ct)));
        var item = Assert.Single(await WithAsync<IPaymentAssignment, IReadOnlyList<AssignmentView>>(a => a.AssignmentsAsync(water, Ct)));
        Assert.Contains("Wasserversorgung", item.Target, StringComparison.Ordinal);

        // Rule is editable and deletable (audit via the entity change history)
        var input = (await WithAsync<IMatchRules, MatchRuleInput?>(r => r.GetAsync(wasser, Ct)))!;
        input.Priority = 5;
        Validate(await WithAsync<IMatchRules, Result>(r => r.SaveAsync(wasser, input, Ct)));
        Validate(await WithAsync<IMatchRules, Result>(r => r.DeleteAsync(wasser, Ct)));
        var history = await WithAsync<IChangeHistory, IReadOnlyList<AuditEntryView>>(h => h.GetAsync("MatchRule", wasser, Ct));
        Assert.Equal([AuditAction.Created, AuditAction.Modified, AuditAction.Deleted], history.Select(h => h.Action).Order());
    }

    [Fact]
    public async Task Recurring_payee_is_suggested_from_the_earlier_assignment()
    {
        var first = await TransactionAsync(new DateOnly(2026, 2, 5), -80m, "Stadtwerke Musterstadt", SupplierIban, "Abschlag Februar");
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(first, [new AssignmentPartInput { CostType = "betrkv-11", PropertyId = house }], Ct)));
        var second = await TransactionAsync(new DateOnly(2026, 3, 5), -80m, "SWM", SupplierIban, "Abschlag März");

        var suggestion = (await WithAsync<IPaymentAssignment, AssignmentProposal?>(a => a.ProposalAsync(second, Ct)))!.Suggestions[0];

        Assert.Equal(SuggestionKind.Expense, suggestion.Kind);
        Assert.Contains("Beleuchtung", suggestion.Target, StringComparison.Ordinal);
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AcceptAsync(second, 0, Ct)));
    }

    [Fact]
    public async Task Outgoing_transaction_to_a_lease_is_a_payout_and_incoming_to_a_cost_type_is_a_credit_note()
    {
        var payout = await TransactionAsync(new DateOnly(2026, 3, 5), -120m, "Anna Schmidt", TenantIban, "Guthaben Auszahlung");
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(payout, [new AssignmentPartInput { LeaseId = lease }], Ct)));
        var entry = Assert.Single((await WithAsync<ITenantAccounts, TenantAccount?>(k => k.GetAsync(lease, Ct)))!.Entries, e => e.EntryType == EntryType.Payment);
        Assert.Equal((120m, 0m), (entry.Debit, entry.Credit));
        Assert.StartsWith("Auszahlung", entry.Text, StringComparison.Ordinal);

        var credit = await TransactionAsync(new DateOnly(2026, 3, 6), 45m, "Stadtwerke Musterstadt", SupplierIban, "Gutschrift Wasser");
        Validate(await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(credit, [new AssignmentPartInput { CostType = "betrkv-02", PropertyId = house }], Ct)));
        var assignment = Assert.Single(await WithAsync<IPaymentAssignment, IReadOnlyList<AssignmentView>>(a => a.AssignmentsAsync(credit, Ct)));
        Assert.Equal(45m, assignment.Amount);
        var link = assignment.Link!;
        var receiptId = Guid.Parse(link[(link.LastIndexOf('/') + 1)..]);
        var receipt = (await WithAsync<IReceiptService, ReceiptDetails?>(b => b.GetAsync(receiptId, Ct)))!;
        Assert.Equal((-45m, -45m), (receipt.Data.Gross, receipt.Allocated));
    }

    [Fact]
    public async Task Ignored_transactions_must_be_reopened_before_assignment()
    {
        var id = await TransactionAsync(new DateOnly(2026, 3, 5), 770m, "Anna Schmidt", TenantIban, "Miete");
        Validate(await WithAsync<IBankService, Result>(b => b.IgnoreAsync(id, "privat", Ct)));

        var result = await WithAsync<IPaymentAssignment, Result>(a => a.AssignAsync(id, [new AssignmentPartInput { LeaseId = lease }], Ct));

        Assert.Contains("wieder geöffnet", result.Errors.Single(), StringComparison.Ordinal);
    }

    private async Task<Guid> TransactionAsync(DateOnly date, decimal amount, string? name, string? iban, string purpose)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var transaction = new Transaction(account, null, new ParsedTransaction(date, amount, purpose, CounterpartyName: name, CounterpartyIban: iban), Guid.NewGuid().ToString("N"));
        db.Add(transaction);
        await db.SaveChangesAsync(Ct);
        return transaction.Id;
    }

    private async Task<TransactionStatus> StatusAsync(Guid id)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<Transaction>().AsNoTracking().SingleAsync(u => u.Id == id, Ct)).Status;
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
