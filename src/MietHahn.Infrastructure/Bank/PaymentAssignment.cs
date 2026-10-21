using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Bank;
using MietHahn.Application.Common;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Bank;
using MietHahn.Domain.Bank.Matching;
using MietHahn.Domain.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Domain.Tax;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Finance;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Bank;

internal sealed class PaymentAssignment(MietHahnDbContext db) : IPaymentAssignment
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<IReadOnlyList<InboxRow>> InboxAsync(Guid? bankAccountId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Set<Transaction>().AsNoTracking().Where(u => u.Status == TransactionStatus.Open);
        if (bankAccountId is { } account)
        {
            query = query.Where(u => u.BankAccountId == account);
        }

        var transactions = await query.OrderByDescending(u => u.BookingDate).ThenByDescending(u => u.Id).ToListAsync(cancellationToken);
        if (transactions.Count == 0)
        {
            return [];
        }

        var context = await LoadAsync(cancellationToken);
        return transactions.Select(u =>
        {
            var suggestions = PaymentMatcher.Suggest(Input(u, context), context.Match);
            return new InboxRow(Row(u), suggestions.Count == 0 ? null : View(suggestions[0], u, context), Math.Max(0, suggestions.Count - 1));
        }).ToList();
    }

    public async Task<AssignmentProposal?> ProposalAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await db.Set<Transaction>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == transactionId, cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        var account = await db.Set<BankAccount>().AsNoTracking().SingleAsync(k => k.Id == transaction.BankAccountId, cancellationToken);
        IReadOnlyList<SuggestionView> views = [];
        if (transaction.Status == TransactionStatus.Open)
        {
            var (suggestions, context) = await SuggestionsAsync(transaction, cancellationToken);
            views = suggestions.Select(s => View(s, transaction, context)).ToList();
        }

        return new AssignmentProposal(Row(transaction), account.Name, views);
    }

    public Task<Result> AssignAsync(Guid transactionId, IReadOnlyList<AssignmentPartInput> parts, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(parts);
            var transaction = await db.Set<Transaction>().SingleOrDefaultAsync(u => u.Id == transactionId, cancellationToken) ?? throw new NotFoundException();
            var inputs = parts.Where(p => !p.IsEmpty).ToList();
            var amounts = TransactionSplit.Resolve(transaction.Amount, inputs.Select(p => p.Amount).ToList());
            transaction.MarkAsAssigned();

            var costs = new List<(int Index, AssignmentPartInput Part, decimal Amount)>();
            for (var i = 0; i < inputs.Count; i++)
            {
                var part = inputs[i];
                try
                {
                    if (part.IsLoan)
                    {
                        if (part.LeaseId is not null || part.ChargeId is not null || part.IsCost)
                        {
                            throw new RuleViolationException("Eine Darlehensrate lässt sich nicht zugleich einem Mieter oder einer Kostenart zuordnen.");
                        }

                        await AddLoanInstalmentAsync(transaction, part, amounts[i], cancellationToken);
                    }
                    else if (part.IsCost)
                    {
                        if (part.LeaseId is not null || part.ChargeId is not null)
                        {
                            throw new RuleViolationException("Bitte entweder einen Mieter oder eine Kostenart angeben, nicht beides.");
                        }

                        costs.Add((i, part, -amounts[i]));
                    }
                    else
                    {
                        await AddPaymentAsync(transaction, part, amounts[i], cancellationToken);
                    }
                }
                catch (RuleViolationException ex) when (inputs.Count > 1)
                {
                    throw new RuleViolationException($"Zeile {i + 1}: {ex.Message}");
                }
            }

            await AddCostsAsync(transaction, costs, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return transaction.Id;
        });

    public async Task<Result> AcceptAsync(Guid transactionId, int rank = 0, CancellationToken cancellationToken = default)
    {
        var transaction = await db.Set<Transaction>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == transactionId, cancellationToken);
        if (transaction is null)
        {
            return Command.NotFound;
        }

        var (suggestions, _) = await SuggestionsAsync(transaction, cancellationToken);
        if (rank < 0 || rank >= suggestions.Count)
        {
            return Result.Failed("Für diesen Umsatz gibt es keinen Vorschlag.");
        }

        var suggestion = suggestions[rank];
        switch (suggestion.Kind)
        {
            case SuggestionKind.Ignore:
                return await Command.ExecuteAsync(async () =>
                {
                    var tracked = await db.Set<Transaction>().SingleAsync(u => u.Id == transactionId, cancellationToken);
                    tracked.Ignore(suggestion.RuleName is null ? null : $"Regel „{suggestion.RuleName}“");
                    await db.SaveChangesAsync(cancellationToken);
                    return tracked.Id;
                });
            case SuggestionKind.Expense:
                return await AssignAsync(transactionId,
                [
                    new AssignmentPartInput
                    {
                        CostType = suggestion.CostType,
                        PropertyId = suggestion.PropertyId,
                        UnitId = suggestion.UnitId,
                        Allocable = suggestion.Allocable,
                    },
                ], cancellationToken);
            default:
                return await AssignAsync(transactionId, suggestion.Parts
                    .Select(p => new AssignmentPartInput { LeaseId = suggestion.LeaseId, ChargeId = p.ChargeId, Amount = suggestion.Parts.Count == 1 ? null : p.Amount })
                    .ToList(), cancellationToken);
        }
    }

    public Task<Result> UnassignAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var transaction = await db.Set<Transaction>().SingleOrDefaultAsync(u => u.Id == transactionId, cancellationToken) ?? throw new NotFoundException();
            transaction.ReleaseAssignment();

            var assignments = await db.Set<TransactionAssignment>().Where(a => a.TransactionId == transactionId).ToListAsync(cancellationToken);
            var paymentIds = assignments.Where(a => a.PaymentId is not null).Select(a => a.PaymentId!.Value).ToList();
            foreach (var payment in await db.Set<Payment>().Where(z => paymentIds.Contains(z.Id)).ToListAsync(cancellationToken))
            {
                db.Remove(payment);
            }

            var receiptIds = assignments.Where(a => a.ReceiptId is not null).Select(a => a.ReceiptId!.Value).Distinct().ToList();
            var receipts = await db.Set<Receipt>().Include(b => b.Items).Include(b => b.Files).AsSplitQuery()
                .Where(b => receiptIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
            foreach (var assignment in assignments.Where(a => a.CostItemId is not null))
            {
                if (receipts.TryGetValue(assignment.ReceiptId!.Value, out var receipt))
                {
                    receipt.RemoveCostItem(assignment.CostItemId!.Value);
                    if (assignment.PaidOnSet && receipt.PaidOn == transaction.BookingDate)
                    {
                        receipt.PaidOn = null;
                    }
                }
            }

            // A receipt created only for this transaction disappears with it, unless files were added in the meantime.
            foreach (var receipt in assignments.Where(a => a.ReceiptCreated).Select(a => receipts.GetValueOrDefault(a.ReceiptId!.Value)).OfType<Receipt>().Distinct())
            {
                if (receipt.Items.Count == 0 && receipt.Files.Count == 0)
                {
                    db.Remove(receipt);
                }
            }

            foreach (var assignment in assignments)
            {
                db.Remove(assignment);
            }

            await db.SaveChangesAsync(cancellationToken);
            return transaction.Id;
        });

    public async Task<IReadOnlyList<AssignmentView>> AssignmentsAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var assignments = await db.Set<TransactionAssignment>().AsNoTracking().Where(a => a.TransactionId == transactionId).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        if (assignments.Count == 0)
        {
            return [];
        }

        var paymentIds = assignments.Where(a => a.PaymentId is not null).Select(a => a.PaymentId!.Value).ToList();
        var payments = await db.Set<Payment>().AsNoTracking().Where(z => paymentIds.Contains(z.Id)).ToDictionaryAsync(z => z.Id, cancellationToken);
        var leaseIds = payments.Values.Select(z => z.LeaseId).Distinct().ToList();
        var leases = await LeaseTextsAsync(leaseIds, cancellationToken);
        var chargeIds = payments.Values.Where(z => z.ChargeId is not null).Select(z => z.ChargeId!.Value).ToList();
        var charges = await db.Set<Charge>().AsNoTracking().Where(s => chargeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var receiptIds = assignments.Where(a => a.ReceiptId is not null).Select(a => a.ReceiptId!.Value).Distinct().ToList();
        var receipts = await db.Set<Receipt>().AsNoTracking().Include(b => b.Items).Include(b => b.Files).AsSplitQuery()
            .Where(b => receiptIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var loanIds = assignments.Where(a => a.LoanId is not null).Select(a => a.LoanId!.Value).Distinct().ToList();
        var loans = await db.Set<Loan>().AsNoTracking().Where(d => loanIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);

        var result = new List<AssignmentView>();
        foreach (var a in assignments)
        {
            if (a.PaymentId is { } paymentId && payments.TryGetValue(paymentId, out var payment))
            {
                result.Add(new AssignmentView(a.Id, a.Amount, payment.Amount < 0 ? "Auszahlung" : "Zahlung", leases.GetValueOrDefault(payment.LeaseId, "?"),
                    payment.ChargeId is { } chargeId && charges.TryGetValue(chargeId, out var charge) ? $"auf Forderung „{charge}“" : "Verrechnung nach Reihenfolge",
                    $"/verwaltung/vertraege/{payment.LeaseId}/konto", false));
            }
            else if (a.ReceiptId is { } receiptId && receipts.TryGetValue(receiptId, out var receipt) && receipt.Items.FirstOrDefault(p => p.Id == a.CostItemId) is { } item)
            {
                result.Add(new AssignmentView(a.Id, a.Amount, "Kostenposition",
                    $"{CostTypes.Find(item.CostType)?.Name ?? item.CostType}, {properties.GetValueOrDefault(item.PropertyId, "?")}",
                    $"{(item.Allocable ? "umlagefähig" : "nicht umlagefähig")}, Zeitraum {item.BillingPeriod}",
                    $"/verwaltung/belege/{receipt.Id}", receipt.Files.Count == 0));
            }
            else if (a.LoanId is { } loanId && loans.TryGetValue(loanId, out var loan))
            {
                var interest = a.LoanInterest ?? 0m;
                result.Add(new AssignmentView(a.Id, a.Amount, "Darlehensrate", $"{loan.Bank}{(string.IsNullOrWhiteSpace(loan.LoanNumber) ? "" : $" ({loan.LoanNumber})")}, {properties.GetValueOrDefault(loan.PropertyId, "?")}",
                    $"Zinsen {Money(interest)}, Tilgung {Money(-a.Amount - interest)}", $"/verwaltung/finanzen/darlehen/{loan.Id}", false));
            }
            else
            {
                result.Add(new AssignmentView(a.Id, a.Amount, "Zuordnung", "gelöscht", null, null, false));
            }
        }

        return result;
    }

    public async Task<AssignmentOptions> OptionsAsync(CancellationToken cancellationToken = default)
    {
        var leaseData = await LoadLeasesAsync(cancellationToken);
        var leases = leaseData.Descriptions.OrderBy(d => d.Value, StringComparer.CurrentCulture).Select(d => new LeaseOption(d.Key, d.Value)).ToList();
        var charges = leaseData.OpenCharges.SelectMany(l => l.Value.Select(c => new ChargeOption(c.ChargeId, l.Key,
            $"{leaseData.Descriptions[l.Key]} · {c.Name} · fällig {c.Due.ToString("dd.MM.yyyy", German)} · offen {Money(c.Open)}", c.Open)))
            .OrderBy(c => c.Text, StringComparer.CurrentCulture).ToList();
        var properties = await db.Set<Property>().AsNoTracking().OrderBy(o => o.Name).Select(o => new ReferenceOption(o.Id, o.Name)).ToListAsync(cancellationToken);
        var units = await db.Set<Unit>().AsNoTracking().Join(db.Set<Property>(), e => e.PropertyId, o => o.Id, (e, o) => new { e.Id, e.Name, Property = o.Name })
            .OrderBy(e => e.Property).ThenBy(e => e.Name).ToListAsync(cancellationToken);
        var receipts = (await db.Set<Receipt>().AsNoTracking().Include(b => b.Items).OrderByDescending(b => b.InvoiceDate).Take(300).ToListAsync(cancellationToken))
            .Where(b => b.Gross > 0 && b.Gross - b.Allocated > 0)
            .Select(b => new ReceiptOption(b.Id, $"{b.SupplierName ?? "Beleg"} · {Money(b.Gross)} vom {b.InvoiceDate.ToString("dd.MM.yyyy", German)} · offen {Money(b.Gross - b.Allocated)}", b.Gross - b.Allocated))
            .ToList();
        var loanOptions = await (from d in db.Set<Loan>().AsNoTracking()
                                 join o in db.Set<Property>().AsNoTracking() on d.PropertyId equals o.Id
                                 orderby o.Name, d.Bank
                                 select new { d.Id, d.Bank, d.LoanNumber, Property = o.Name }).ToListAsync(cancellationToken);
        return new AssignmentOptions(leases, charges, properties, units.Select(e => new ReferenceOption(e.Id, $"{e.Name} ({e.Property})")).ToList(), receipts)
        {
            Loans = loanOptions.Select(d => new LoanOption(d.Id, $"{d.Bank}{(string.IsNullOrWhiteSpace(d.LoanNumber) ? "" : $" ({d.LoanNumber})")} · {d.Property}")).ToList(),
        };
    }

    private async Task AddLoanInstalmentAsync(Transaction transaction, AssignmentPartInput part, decimal amount, CancellationToken cancellationToken)
    {
        if (amount >= 0)
        {
            throw new RuleViolationException("Nur Ausgänge lassen sich als Darlehensrate buchen.");
        }

        var loan = await db.Set<Loan>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == part.LoanId, cancellationToken)
            ?? throw new RuleViolationException("Das Darlehen wurde nicht gefunden.");
        var extras = await db.Set<LoanExtraPayment>().AsNoTracking().Where(s => s.LoanId == loan.Id).ToListAsync(cancellationToken);
        var interest = part.LoanInterest
            ?? LoanCalculator.InterestAround(LoanCalculator.Calculate(loan.ToCalculation(), extras.Select(s => s.ToCalculation()).ToList()), transaction.BookingDate)
            ?? throw new RuleViolationException("Für dieses Datum gibt es im Tilgungsplan keine Rate. Bitte den Zinsanteil der Rate selbst angeben.");
        if (interest < 0 || interest > -amount)
        {
            throw new RuleViolationException($"Der Zinsanteil muss zwischen 0 und {Money(-amount)} liegen.");
        }

        db.Add(new TransactionAssignment(transaction.Id, amount) { LoanId = loan.Id, LoanInterest = interest });
    }

    private async Task AddPaymentAsync(Transaction transaction, AssignmentPartInput part, decimal amount, CancellationToken cancellationToken)
    {
        var leaseId = part.LeaseId;
        if (part.ChargeId is { } chargeId)
        {
            if (amount < 0)
            {
                throw new RuleViolationException("Eine Auszahlung kann nicht auf eine Forderung gebucht werden.");
            }

            var charge = await db.Set<Charge>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == chargeId, cancellationToken)
                ?? throw new RuleViolationException("Die gewählte Forderung wurde nicht gefunden.");
            if (charge.Amount <= 0)
            {
                throw new RuleViolationException("Gutschriften lassen sich nicht als Zahlungsbestimmung wählen.");
            }

            if (leaseId is { } given && given != charge.LeaseId)
            {
                throw new RuleViolationException("Die Forderung gehört zu einem anderen Mietverhältnis.");
            }

            leaseId = charge.LeaseId;
        }

        var lease = leaseId ?? throw new RuleViolationException("Bitte ein Mietverhältnis oder eine Kostenart wählen.");
        if (!await db.Set<Lease>().AnyAsync(v => v.Id == lease, cancellationToken))
        {
            throw new RuleViolationException("Das Mietverhältnis wurde nicht gefunden.");
        }

        var purpose = string.IsNullOrWhiteSpace(part.Note) ? transaction.Purpose : $"{part.Note.Trim()} ({transaction.Purpose})";
        var payment = new Payment(lease, transaction.ValueDate ?? transaction.BookingDate, amount, PaymentOrigin.Bank)
        {
            Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Length > 500 ? purpose[..500] : purpose,
            ChargeId = part.ChargeId,
        };
        db.Add(payment);
        db.Add(new TransactionAssignment(transaction.Id, amount) { PaymentId = payment.Id });
    }

    private async Task AddCostsAsync(Transaction transaction, List<(int Index, AssignmentPartInput Part, decimal Amount)> costs, CancellationToken cancellationToken)
    {
        if (costs.Count == 0)
        {
            return;
        }

        var existing = new Dictionary<Guid, Receipt>();
        Receipt? created = null;
        foreach (var (index, part, amount) in costs)
        {
            try
            {
                Receipt receipt;
                var isNew = false;
                if (part.ReceiptId is { } receiptId)
                {
                    if (!existing.TryGetValue(receiptId, out var found))
                    {
                        found = await db.Set<Receipt>().Include(b => b.Items).Include(b => b.Files).AsSplitQuery().SingleOrDefaultAsync(b => b.Id == receiptId, cancellationToken)
                            ?? throw new RuleViolationException("Der gewählte Beleg wurde nicht gefunden.");
                        existing[receiptId] = found;
                    }

                    receipt = found;
                }
                else
                {
                    // One new receipt for all parts without an existing receipt; it carries the whole amount of these parts.
                    if (created is null)
                    {
                        var gross = costs.Where(c => c.Part.ReceiptId is null).Sum(c => c.Amount);
                        created = NewReceipt(transaction, gross);
                        db.Add(created);
                    }

                    receipt = created;
                    isNew = true;
                }

                var item = await AddCostItemAsync(receipt, part, amount, transaction, cancellationToken);
                var paidOnSet = !isNew && receipt.PaidOn is null;
                if (paidOnSet)
                {
                    receipt.PaidOn = transaction.BookingDate;
                }

                db.Add(new TransactionAssignment(transaction.Id, -amount) { ReceiptId = receipt.Id, CostItemId = item.Id, ReceiptCreated = isNew, PaidOnSet = paidOnSet });
            }
            catch (RuleViolationException ex) when (costs.Count > 1 || index > 0)
            {
                throw new RuleViolationException($"Zeile {index + 1}: {ex.Message}");
            }
        }
    }

    private async Task<CostItem> AddCostItemAsync(Receipt receipt, AssignmentPartInput part, decimal amount, Transaction transaction, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(part.CostType))
        {
            throw new RuleViolationException("Bitte die Kostenart wählen.");
        }

        var propertyId = part.PropertyId ?? throw new RuleViolationException("Bitte das Objekt wählen.");
        if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
        {
            throw new RuleViolationException("Das Objekt wurde nicht gefunden.");
        }

        if (part.UnitId is { } unit && !await db.Set<Unit>().AnyAsync(e => e.Id == unit && e.PropertyId == propertyId, cancellationToken))
        {
            throw new RuleViolationException("Die Einheit gehört nicht zum gewählten Objekt.");
        }

        // Default: service period of the receipt, otherwise the calendar year of the booking.
        var from = part.From ?? receipt.ServiceFrom ?? new DateOnly(transaction.BookingDate.Year, 1, 1);
        var to = part.To ?? (part.From is null ? receipt.ServiceTo : null) ?? new DateOnly(from.Year, 12, 31);
        if (to < from)
        {
            throw new RuleViolationException("Der Abrechnungszeitraum endet vor seinem Beginn.");
        }

        var item = receipt.AddCostItem(part.CostType, amount, propertyId, new Period(from, to), part.Allocable);
        item.UnitId = part.UnitId;
        item.Note = string.IsNullOrWhiteSpace(part.Note) ? null : part.Note.Trim();
        return item;
    }

    private static Receipt NewReceipt(Transaction transaction, decimal gross)
    {
        var supplier = string.IsNullOrWhiteSpace(transaction.CounterpartyName) ? "Unbekannt" : transaction.CounterpartyName.Trim();
        var note = $"Aus Bankumsatz vom {transaction.BookingDate.ToString("dd.MM.yyyy", German)}: {transaction.Purpose}";
        var receipt = new Receipt(transaction.BookingDate, gross)
        {
            SupplierName = supplier.Length > 200 ? supplier[..200] : supplier,
            PaidOn = transaction.BookingDate,
            Note = note.Length > 2000 ? note[..2000] : note,
        };
        receipt.Validate();
        return receipt;
    }

    private async Task<(IReadOnlyList<MatchSuggestion>, AssignmentContext)> SuggestionsAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(cancellationToken);
        return (PaymentMatcher.Suggest(Input(transaction, context), context.Match), context);
    }

    private static MatchInput Input(Transaction u, AssignmentContext context) =>
        new(u.BookingDate, u.Amount, u.CounterpartyName, u.CounterpartyIban, u.Purpose, context.DefaultProperties.GetValueOrDefault(u.BankAccountId));

    private static SuggestionView View(MatchSuggestion s, Transaction u, AssignmentContext context)
    {
        switch (s.Kind)
        {
            case SuggestionKind.Ignore:
                return new SuggestionView(s.Kind, s.Score, s.Confidence, s.Reasons, "Ignorieren (privat)", []);
            case SuggestionKind.Expense:
                var costType = CostTypes.Find(s.CostType ?? "")?.Name ?? s.CostType ?? "?";
                var property = s.PropertyId is { } p ? context.Properties.GetValueOrDefault(p, "?") : "Objekt noch wählen";
                return new SuggestionView(s.Kind, s.Score, s.Confidence, s.Reasons, $"{costType} · {property}", [new SuggestionPartView(costType, Math.Abs(u.Amount))]);
            default:
                return new SuggestionView(s.Kind, s.Score, s.Confidence, s.Reasons, context.Leases.GetValueOrDefault(s.LeaseId ?? Guid.Empty, "?"),
                    s.Parts.Select(p => new SuggestionPartView(p.Text, p.Amount)).ToList());
        }
    }

    private async Task<AssignmentContext> LoadAsync(CancellationToken cancellationToken)
    {
        var data = await LoadLeasesAsync(cancellationToken);
        var rules = await db.Set<MatchRule>().AsNoTracking().OrderBy(r => r.Priority).ThenBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var history = (await (from a in db.Set<TransactionAssignment>().AsNoTracking()
                              join t in db.Set<Transaction>() on a.TransactionId equals t.Id
                              join c in db.Set<CostItem>() on a.CostItemId equals c.Id
                              orderby a.CreatedAt descending
                              select new { t.CounterpartyIban, t.CounterpartyName, c.CostType, c.PropertyId, c.UnitId, c.Allocable })
                .Take(500).ToListAsync(cancellationToken))
            .Select(h => new ExpenseHistory(h.CounterpartyIban, h.CounterpartyName, h.CostType, h.PropertyId, h.UnitId, h.Allocable)).ToList();
        var defaults = (await db.Set<BankAccount>().AsNoTracking().ToListAsync(cancellationToken))
            .Where(k => k.PropertyIds.Count == 1).ToDictionary(k => k.Id, k => k.PropertyIds[0]);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        return new AssignmentContext(new MatchContext(rules, data.Candidates, history), data.Descriptions, defaults, properties);
    }

    private async Task<LeaseData> LoadLeasesAsync(CancellationToken cancellationToken)
    {
        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.Components).AsSplitQuery().ToListAsync(cancellationToken);
        var personIds = leases.SelectMany(v => v.Parties.Select(p => p.PersonId)).Distinct().ToList();
        var persons = await db.Set<Person>().AsNoTracking().Include(p => p.BankDetails).Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var charges = (await db.Set<Charge>().AsNoTracking().ToListAsync(cancellationToken)).ToLookup(s => s.LeaseId);
        var payments = (await db.Set<Payment>().AsNoTracking().ToListAsync(cancellationToken)).ToLookup(z => z.LeaseId);

        var descriptions = new Dictionary<Guid, string>();
        var openCharges = new Dictionary<Guid, List<OpenCharge>>();
        var candidates = new List<LeaseCandidate>();
        foreach (var lease in leases)
        {
            var tenants = lease.Tenant.Select(t => persons.GetValueOrDefault(t.PersonId)).OfType<Person>().ToList();
            var rentalUnit = rentalUnits.GetValueOrDefault(lease.RentalUnitId);
            var text = $"{rentalUnit?.Property ?? "?"}, {rentalUnit?.Name ?? "?"}: {string.Join(", ", tenants.Select(t => t.DisplayName))}";
            descriptions[lease.Id] = text;

            var result = TenantAccounts.Allocate(charges[lease.Id].ToList(), payments[lease.Id].ToList(), settings.Allocation);
            var open = charges[lease.Id].Where(s => s.Amount > 0 && result.Open[s.Id] > 0)
                .Select(s => new OpenCharge(s.Id, s.Kind, s.Due, s.ServiceMonth, result.Open[s.Id], s.Name))
                .OrderBy(c => c.Due).ThenBy(c => c.ChargeId).ToList();
            openCharges[lease.Id] = open;

            candidates.Add(new LeaseCandidate(
                lease.Id, text, rentalUnit?.Name ?? "", lease.Start, lease.End,
                tenants.Select(t => new TenantInfo(t.FirstName, t.LastName, t.Company, t.BankDetails.Select(b => b.Iban).ToList())).ToList(),
                lease.MonthlyRentOn, open));
        }

        return new LeaseData(candidates, descriptions, openCharges);
    }

    private async Task<Dictionary<Guid, string>> LeaseTextsAsync(List<Guid> leaseIds, CancellationToken cancellationToken)
    {
        if (leaseIds.Count == 0)
        {
            return [];
        }

        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Where(v => leaseIds.Contains(v.Id)).ToListAsync(cancellationToken);
        var personIds = leases.SelectMany(v => v.Parties.Select(p => p.PersonId)).Distinct().ToList();
        var names = await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        return leases.ToDictionary(v => v.Id, v => $"{rentalUnits.GetValueOrDefault(v.RentalUnitId)?.Property ?? "?"}, {rentalUnits.GetValueOrDefault(v.RentalUnitId)?.Name ?? "?"}: {LeaseService.TenantText(v, names)}");
    }

    private static TransactionRow Row(Transaction u) =>
        new(u.Id, u.BankAccountId, u.BookingDate, u.ValueDate, u.Amount, u.CounterpartyName, u.CounterpartyIban, u.Purpose, u.BookingText,
            u.EndToEndId, u.MandateReference, u.CreditorId, u.Status, u.Note);

    private static string Money(decimal amount) => amount.ToString("N2", German) + " €";

    private sealed record LeaseData(List<LeaseCandidate> Candidates, Dictionary<Guid, string> Descriptions, Dictionary<Guid, List<OpenCharge>> OpenCharges);

    private sealed record AssignmentContext(MatchContext Match, Dictionary<Guid, string> Leases, Dictionary<Guid, Guid> DefaultProperties, Dictionary<Guid, string> Properties);
}
