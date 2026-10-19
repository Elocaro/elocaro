using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Finance;
using MietHahn.Application.People;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Finance;

internal sealed class TenantAccounts(MietHahnDbContext db, TimeProvider clock) : ITenantAccounts
{
    public async Task<TenantAccount?> GetAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        var (rentalUnit, tenant) = (await DescriptionsAsync(db, [lease], cancellationToken))[lease.Id];
        var charges = await db.Set<Charge>().AsNoTracking().Where(s => s.LeaseId == leaseId).ToListAsync(cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking().Where(z => z.LeaseId == leaseId).ToListAsync(cancellationToken);
        var result = Allocate(charges, payments, settings.Allocation);
        var today = GermanTime.Today(clock.GetUtcNow());

        var lines = charges.Select(s => (Date: s.Due, Order: RankOrder(s), s.Id, Row: (Func<decimal, AccountEntry>)(balance => new AccountEntry(
                s.Id, EntryType.Charge, s.Due, s.Name, s.Kind, Math.Max(s.Amount, 0), Math.Max(-s.Amount, 0), balance,
                s.Amount > 0 ? result.Open[s.Id] : null, s.Automatic, null)), Amount: s.Amount))
            .Concat(payments.Select(z => (Date: z.Date, Order: 1000, z.Id, Row: (Func<decimal, AccountEntry>)(balance => new AccountEntry(
                z.Id, EntryType.Payment, z.Date, PaymentText(z), null, Math.Max(-z.Amount, 0), Math.Max(z.Amount, 0), balance,
                null, false, z.Origin)), Amount: -z.Amount)))
            .OrderBy(z => z.Date).ThenBy(z => z.Order).ThenBy(z => z.Id);

        var bookings = new List<AccountEntry>();
        var running = 0m;
        foreach (var line in lines)
        {
            running += line.Amount;
            bookings.Add(line.Row(running));
        }

        var due = charges.Where(s => s.Amount > 0 && s.Due <= today).Sum(s => result.Open[s.Id]);
        return new TenantAccount(lease.Id, rentalUnit, tenant, settings.AccountingStart, bookings, running, due,
            AnnualValues(charges, result), settings.Allocation);
    }

    public async Task<int> GenerateChargesAsync(Guid? leaseId = null, CancellationToken cancellationToken = default)
    {
        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        if (settings.AccountingStart is not { } from)
        {
            return 0;
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var toMonth = new DateOnly(today.Year, today.Month, 1);
        var query = db.Set<Lease>().AsNoTracking().Include(v => v.Components).AsQueryable();
        if (leaseId is { } id)
        {
            query = query.Where(v => v.Id == id);
        }

        var leases = await query.ToListAsync(cancellationToken);
        var leaseIds = leases.Select(v => v.Id).ToList();
        var existingByLease = (await db.Set<Charge>().Where(s => s.Kind == ChargeType.Rent && leaseIds.Contains(s.LeaseId)).ToListAsync(cancellationToken))
            .ToLookup(s => s.LeaseId);

        var changes = 0;
        foreach (var lease in leases)
        {
            var existing = existingByLease[lease.Id].ToDictionary(s => (s.ServiceMonth!.Value, s.Component!.Value));
            var target = new HashSet<(DateOnly, RentComponentType)>();
            foreach (var month in ChargeCalculation.Months(lease, from, toMonth))
            {
                // Due on the n-th working day; if the tenant moves in during the month, no earlier than the move-in day.
                var due = WorkingDays.NthWorkingDay(month.Year, month.Month, lease.DueOnWorkingDay);
                if (lease.Start > due)
                {
                    due = lease.Start;
                }

                foreach (var items in ChargeCalculation.Month(lease, month.Year, month.Month, from))
                {
                    target.Add((month, items.Component));
                    if (existing.TryGetValue((month, items.Component), out var charge))
                    {
                        changes += charge.Adjust(due, items.Amount, items.Name) ? 1 : 0;
                    }
                    else
                    {
                        db.Add(Charge.MonthlyRent(lease.Id, month, items.Component, due, items.Amount, items.Name));
                        changes++;
                    }
                }
            }

            // Lease shortened, component dropped or accounting start moved.
            foreach (var surplus in existing.Where(b => !target.Contains(b.Key)))
            {
                db.Remove(surplus.Value);
                changes++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return changes;
    }

    public Task<Result> CreateChargeAsync(Guid leaseId, ChargeInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await ValidateLeaseAsync(leaseId, cancellationToken);
            if (input.Kind == ChargeType.Rent)
            {
                throw new RuleViolationException("Monatsmieten entstehen automatisch aus dem Vertrag.");
            }

            var amount = input.Amount ?? 0m;
            var charge = new Charge(
                leaseId,
                input.Kind,
                input.Due ?? throw new RuleViolationException("Bitte das Fälligkeitsdatum angeben."),
                input.Kind == ChargeType.Refund ? -amount : amount,
                string.IsNullOrWhiteSpace(input.Name) ? DefaultText(input.Kind) : input.Name.Trim())
            {
                Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            };
            db.Add(charge);
            await db.SaveChangesAsync(cancellationToken);
            return charge.Id;
        });

    public Task<Result> DeleteChargeAsync(Guid chargeId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var charge = await db.Set<Charge>().SingleOrDefaultAsync(s => s.Id == chargeId, cancellationToken) ?? throw new NotFoundException();
            if (charge.Automatic)
            {
                throw new RuleViolationException("Monatsmieten ergeben sich aus dem Vertrag und lassen sich nicht löschen; bei Bedarf eine Gutschrift (Erstattung) anlegen.");
            }

            // Payment allocations to this receivable are dropped (FK ON DELETE SET NULL does not apply to soft deletion).
            await db.Set<Payment>().Where(z => z.ChargeId == chargeId).ExecuteUpdateAsync(u => u.SetProperty(z => z.ChargeId, (Guid?)null), cancellationToken);
            db.Remove(charge);
            await db.SaveChangesAsync(cancellationToken);
            return charge.Id;
        });

    public Task<Result> RecordPaymentAsync(Guid leaseId, PaymentInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await ValidateLeaseAsync(leaseId, cancellationToken);
            if (input.ChargeId is { } specific && !await db.Set<Charge>().AnyAsync(s => s.Id == specific && s.LeaseId == leaseId, cancellationToken))
            {
                throw new RuleViolationException("Die gewählte Forderung gehört nicht zu diesem Mietverhältnis.");
            }

            var amount = input.Amount ?? 0m;
            var payment = new Payment(leaseId, input.Date ?? throw new RuleViolationException("Bitte das Zahlungsdatum angeben."), input.Payout ? -amount : amount)
            {
                Purpose = string.IsNullOrWhiteSpace(input.Purpose) ? null : input.Purpose.Trim(),
                ChargeId = input.Payout ? null : input.ChargeId,
            };
            db.Add(payment);
            await db.SaveChangesAsync(cancellationToken);
            return payment.Id;
        });

    public Task<Result> DeletePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var payment = await db.Set<Payment>().SingleOrDefaultAsync(z => z.Id == paymentId, cancellationToken) ?? throw new NotFoundException();
            if (payment.Origin != PaymentOrigin.Manual)
            {
                throw new RuleViolationException("Zahlungen aus dem Bankimport werden über die Zuordnung des Umsatzes geändert.");
            }

            db.Remove(payment);
            await db.SaveChangesAsync(cancellationToken);
            return payment.Id;
        });

    public Task<IReadOnlyList<OpenItem>> OpenItemsAsync(CancellationToken cancellationToken = default) => OpenItemsAsync(null, cancellationToken);

    public Task<IReadOnlyList<OpenItem>> OpenItemsForLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default) => OpenItemsAsync(leaseId, cancellationToken);

    private async Task<IReadOnlyList<OpenItem>> OpenItemsAsync(Guid? onlyLease, CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        var chargeQuery = db.Set<Charge>().AsNoTracking();
        var paymentQuery = db.Set<Payment>().AsNoTracking();
        if (onlyLease is { } only)
        {
            chargeQuery = chargeQuery.Where(s => s.LeaseId == only);
            paymentQuery = paymentQuery.Where(z => z.LeaseId == only);
        }

        var charges = (await chargeQuery.ToListAsync(cancellationToken)).ToLookup(s => s.LeaseId);
        var payments = (await paymentQuery.ToListAsync(cancellationToken)).ToLookup(z => z.LeaseId);
        var leaseIds = charges.Select(g => g.Key).ToList();
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Where(v => leaseIds.Contains(v.Id)).ToListAsync(cancellationToken);
        var descriptions = await DescriptionsAsync(db, leases, cancellationToken);

        var items = new List<OpenItem>();
        foreach (var lease in leases)
        {
            var result = Allocate(charges[lease.Id].ToList(), payments[lease.Id].ToList(), settings.Allocation);
            var (rentalUnit, tenant) = descriptions[lease.Id];
            items.AddRange(charges[lease.Id]
                .Where(s => s.Amount > 0 && s.Due <= today && result.Open[s.Id] > 0)
                .Select(s => new OpenItem(s.Id, lease.Id, rentalUnit, tenant, s.Kind, s.Name, s.Due, s.Amount, result.Open[s.Id], today.DayNumber - s.Due.DayNumber)));
        }

        return items.OrderBy(p => p.Due).ThenBy(p => p.RentalUnit, StringComparer.CurrentCulture).ThenBy(p => RankOrder(charges[p.LeaseId].Single(s => s.Id == p.ChargeId))).ToList();
    }

    /// <summary>Receivables: positive charges and payouts; credits: payments and credit notes.</summary>
    internal static AllocationResult Allocate(IReadOnlyCollection<Charge> charges, IReadOnlyCollection<Payment> payments, AllocationOrder order)
    {
        var claims = charges.Where(s => s.Amount > 0).Select(s => new AllocationItem(s.Id, s.Due, s.Amount, s.Rank, s.Automatic))
            .Concat(payments.Where(z => z.Amount < 0).Select(z => new AllocationItem(z.Id, z.Date, -z.Amount)));
        var credit = payments.Where(z => z.Amount > 0).Select(z => new AllocationItem(z.Id, z.Date, z.Amount, Designation: z.ChargeId))
            .Concat(charges.Where(s => s.Amount < 0).Select(s => new AllocationItem(s.Id, s.Due, -s.Amount)));
        return Allocation.Distribute(claims, credit, order);
    }

    private static List<AnnualValue> AnnualValues(List<Charge> charges, AllocationResult result)
    {
        var rents = charges.Where(s => s.Automatic).ToDictionary(s => s.Id);
        var target = rents.Values.Select(s => (s.ServiceMonth!.Value.Year, s.Component!.Value, Debit: s.Amount, Settled: result.Settled(s.Id), Inflow: 0m));
        var inflow = result.Shares.Where(a => rents.ContainsKey(a.ClaimId))
            .Select(a => (a.Date.Year, rents[a.ClaimId].Component!.Value, Debit: 0m, Settled: 0m, Inflow: a.Amount));
        return target.Concat(inflow)
            .GroupBy(w => (w.Year, w.Value))
            .Select(g => new AnnualValue(g.Key.Year, g.Key.Value, g.Sum(w => w.Debit), g.Sum(w => w.Settled), g.Sum(w => w.Inflow)))
            .OrderByDescending(w => w.Year).ThenBy(w => w.Component)
            .ToList();
    }

    internal static async Task<Dictionary<Guid, (string RentalUnit, string Tenant)>> DescriptionsAsync(MietHahnDbContext db, List<Lease> leases, CancellationToken cancellationToken)
    {
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var personIds = leases.SelectMany(v => v.Parties.Select(p => p.PersonId)).Distinct().ToList();
        var names = (await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.DisplayName);
        return leases.ToDictionary(v => v.Id, v =>
        {
            var m = rentalUnits.GetValueOrDefault(v.RentalUnitId);
            return (m is null ? "?" : $"{m.Property}, {m.Name}", LeaseService.TenantText(v, names));
        });
    }

    private async Task ValidateLeaseAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Lease>().AnyAsync(v => v.Id == leaseId, cancellationToken))
        {
            throw new NotFoundException();
        }
    }

    /// <summary>On the same day: components of the monthly rent in a fixed order, then other receivables.</summary>
    private static int RankOrder(Charge s) => s.Component is { } b ? (int)b : 100;

    private static string PaymentText(Payment z) => (z.Amount < 0 ? "Auszahlung" : "Zahlung") + (z.Purpose is { } purpose ? $": {purpose}" : "");

    internal static string DefaultText(ChargeType kind) => kind switch
    {
        ChargeType.AdditionalPayment => "Nachzahlung",
        ChargeType.Refund => "Erstattung (Guthaben)",
        ChargeType.DunningFee => "Mahngebühr",
        ChargeType.ReturnedDebitFee => "Rücklastschriftgebühr",
        ChargeType.DefaultInterest => "Verzugszinsen",
        ChargeType.Damages => "Schadensersatz",
        ChargeType.Deposit => "Kaution",
        ChargeType.BalanceCarriedForward => "Saldovortrag",
        ChargeType.Rent => "Miete",
        _ => "Sonstige Forderung",
    };
}

internal sealed class FinanceSettingsService(MietHahnDbContext db) : IFinanceSettings
{
    public async Task<FinanceSettingsInput> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateAsync(db, cancellationToken);
        return new FinanceSettingsInput
        {
            AccountingStart = settings.AccountingStart,
            Allocation = settings.Allocation,
            DunningGraceDays = settings.DunningGraceDays,
            DaysUntilFirstNotice = settings.DaysUntilFirstNotice,
            DaysUntilSecondNotice = settings.DaysUntilSecondNotice,
            DunningDeadlineDays = settings.DunningDeadlineDays,
            DunningMinimumAmount = settings.DunningMinimumAmount,
            ReminderFee = settings.ReminderFee,
            FirstNoticeFee = settings.FirstNoticeFee,
            SecondNoticeFee = settings.SecondNoticeFee,
            ChargeDefaultInterest = settings.ChargeDefaultInterest,
            BaseRate = settings.BaseRate,
            BaseRateAsOf = settings.BaseRateAsOf,
            InterestMarginPoints = settings.InterestMarginPoints,
        };
    }

    public async Task SaveAsync(FinanceSettingsInput data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var settings = await GetOrCreateAsync(db, cancellationToken);
        if (db.Entry(settings).State == EntityState.Detached)
        {
            db.Add(settings);
        }

        settings.SetAccountingStart(data.AccountingStart);
        settings.Allocation = data.Allocation;
        settings.DunningGraceDays = data.DunningGraceDays;
        settings.DaysUntilFirstNotice = data.DaysUntilFirstNotice;
        settings.DaysUntilSecondNotice = data.DaysUntilSecondNotice;
        settings.DunningDeadlineDays = data.DunningDeadlineDays;
        settings.DunningMinimumAmount = data.DunningMinimumAmount ?? 0m;
        settings.ReminderFee = data.ReminderFee ?? 0m;
        settings.FirstNoticeFee = data.FirstNoticeFee ?? 0m;
        settings.SecondNoticeFee = data.SecondNoticeFee ?? 0m;
        settings.ChargeDefaultInterest = data.ChargeDefaultInterest;
        settings.BaseRate = data.BaseRate;
        settings.BaseRateAsOf = data.BaseRate is null ? null : data.BaseRateAsOf;
        settings.InterestMarginPoints = data.InterestMarginPoints ?? DunningRules.Default.InterestMarginPoints;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The single record, or an unsaved one with default values.</summary>
    internal static async Task<FinanceSettings> GetOrCreateAsync(MietHahnDbContext db, CancellationToken cancellationToken) =>
        await db.Set<FinanceSettings>().OrderBy(f => f.CreatedAt).FirstOrDefaultAsync(cancellationToken) ?? new FinanceSettings();
}

/// <summary>Creates the monthly rents of the current month daily (idempotent) and catches up on missing ones.</summary>
[DisallowConcurrentExecution]
internal sealed class ChargeJob(ITenantAccounts accounts) : IJob
{
    public static readonly JobKey Key = new("charges", "finance");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await accounts.GenerateChargesAsync(null, context.CancellationToken);
    }
}

/// <summary>Dashboard: accounting start missing; overdue arrears.</summary>
internal sealed class FinanceTodos(MietHahnDbContext db, ITenantAccounts accounts) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return [];
        }

        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        if (settings.AccountingStart is null)
        {
            return await db.Set<Lease>().AnyAsync(cancellationToken)
                ? [new TodoItem("Finanzen", "Buchhaltungsbeginn festlegen", "/verwaltung/finanzen/einstellungen", Urgency.Soon,
                    "Ab diesem Monat entstehen die Monatsmieten automatisch; ältere Rückstände als Saldovortrag erfassen.")]
                : [];
        }

        var open = await accounts.OpenItemsAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        var leases = open.Select(p => p.LeaseId).Distinct().Count();
        return [new TodoItem("Finanzen", $"Rückstände: {open.Sum(p => p.Open).ToString("#,##0.00 €", CultureInfo.GetCultureInfo("de-DE"))} bei {leases} Mietverhältnis{(leases == 1 ? "" : "sen")}",
            "/verwaltung/finanzen/offene-posten", open.Any(p => p.DaysOverdue > 30) ? Urgency.Urgent : Urgency.Soon,
            "Zahlungen erfassen bzw. Bankumsätze zuordnen, sonst mahnen.", open.Min(p => p.Due))];
    }
}

/// <summary>Tenant accounts of a person's leases for the GDPR access request.</summary>
internal sealed class TenantAccountPersonalData(MietHahnDbContext db) : IPersonalDataSource
{
    public string Section => "tenantAccounts";

    public async Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var leaseIds = await db.Set<LeaseParty>().Where(p => p.PersonId == personId && p.Role == PartyRole.Tenant).Select(p => p.LeaseId).ToListAsync(cancellationToken);
        if (leaseIds.Count == 0)
        {
            return null;
        }

        var charges = await db.Set<Charge>().AsNoTracking().Where(s => leaseIds.Contains(s.LeaseId)).OrderBy(s => s.Due)
            .Select(s => new { s.LeaseId, s.Due, s.Name, s.Amount }).ToListAsync(cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking().Where(z => leaseIds.Contains(z.LeaseId)).OrderBy(z => z.Date)
            .Select(z => new { z.LeaseId, z.Date, z.Amount, z.Purpose }).ToListAsync(cancellationToken);
        return charges.Count + payments.Count == 0 ? null : new { charges, payments };
    }
}
