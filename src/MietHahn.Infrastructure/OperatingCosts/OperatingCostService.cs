using Microsoft.EntityFrameworkCore;
using MietHahn.Application.OperatingCosts;
using MietHahn.Domain.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Finance;
using MietHahn.Infrastructure.Persistence;
using Zaehler = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Infrastructure.OperatingCosts;

/// <summary>
/// Assembles the input of the calculation core from the database (units with areas and rooms, leases with persons and
/// actually paid prepayments from the tenant account, cost items with receipts, keys, meters) and runs it. Saves nothing.
/// </summary>
internal sealed class OperatingCostService(MietHahnDbContext db) : IOperatingCosts
{
    private static readonly RentComponentType[] PrepaymentComponents =
    [
        RentComponentType.OperatingCostPrepayment,
        RentComponentType.HeatingCostPrepayment,
        RentComponentType.OperatingCostFlatRate,
    ];

    public Task<OperatingCostPreview?> PreviewAsync(Guid propertyId, int year, CancellationToken cancellationToken = default) =>
        PreviewAsync(propertyId, Period.CalendarYear(year), cancellationToken);

    public async Task<OperatingCostPreview?> PreviewAsync(Guid propertyId, Period billingPeriod, CancellationToken cancellationToken = default)
    {
        var end = billingPeriod.To ?? throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        var start = billingPeriod.From;
        var property = await db.Set<Property>().AsNoTracking()
            .Include(o => o.Units).ThenInclude(e => e.Areas)
            .Include(o => o.Units).ThenInclude(e => e.Room).ThenInclude(z => z.Areas)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var units = property.Units.Where(e => e.Type is UnitType.Apartment or UnitType.Commercial).OrderBy(e => e.Order).ToList();
        var unitIds = units.Select(e => e.Id).ToList();
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.People)
            .Where(v => unitIds.Contains(v.UnitId) && v.Term.From <= end && (v.Term.To == null || v.Term.To >= start))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        var descriptions = await TenantAccounts.DescriptionsAsync(db, leases, cancellationToken);
        var leaseIds = leases.Select(v => v.Id).ToList();
        var charges = (await db.Set<Charge>().AsNoTracking().Where(s => leaseIds.Contains(s.LeaseId)).ToListAsync(cancellationToken)).ToLookup(s => s.LeaseId);
        var payments = (await db.Set<Payment>().AsNoTracking().Where(z => leaseIds.Contains(z.LeaseId)).ToListAsync(cancellationToken)).ToLookup(z => z.LeaseId);

        var billingLeases = new List<BillingLease>();
        foreach (var lease in leases.OrderBy(v => v.Term.From).ThenBy(v => v.Id))
        {
            var allocation = TenantAccounts.Allocate([.. charges[lease.Id]], [.. payments[lease.Id]], settings.Allocation);
            var prepayments = charges[lease.Id]
                .Where(s => s.Kind == ChargeType.Rent && s.Component is { } component && PrepaymentComponents.Contains(component)
                    && s.ServiceMonth is { } month && Period.Month(month.Year, month.Month).Overlaps(billingPeriod))
                .OrderBy(s => s.ServiceMonth).ThenBy(s => s.Component)
                .Select(s => new PrepaymentMonth(s.ServiceMonth!.Value, s.Component!.Value, s.Amount, allocation.Settled(s.Id)))
                .ToList();
            billingLeases.Add(new BillingLease(
                lease.Id,
                descriptions[lease.Id].Tenant,
                lease.UnitId,
                lease.RoomId,
                lease.Term,
                [.. lease.People.Select(p => new PersonsState(p.Validity, p.Count))],
                lease.ReceivesOperatingCostStatement,
                lease.ExcludedCostTypes.ToHashSet(StringComparer.Ordinal),
                prepayments));
        }

        var rules = await db.Set<CostAllocationRule>().AsNoTracking().Include(r => r.Shares)
            .Where(r => r.PropertyId == propertyId).ToListAsync(cancellationToken);
        var meters = await db.Set<Zaehler>().AsNoTracking().Include(z => z.Readings).Where(z => z.PropertyId == propertyId).ToListAsync(cancellationToken);

        var input = new BillingInput(
            billingPeriod,
            [.. units.Select(ToBillingUnit)],
            billingLeases,
            await CostsAsync(propertyId, billingPeriod, cancellationToken),
            [.. rules.Select(r => r.ToBillingRule())],
            new MeterConsumptionSource(meters));

        var hints = new List<string>();
        if (billingLeases.Any(v => v.ReceivesStatement) && (settings.AccountingStart is not { } accountingStart || accountingStart > start))
        {
            hints.Add(settings.AccountingStart is { } from
                ? $"Die Buchhaltung beginnt erst im {from:MM/yyyy}: Vorauszahlungen davor sind im Mieterkonto nicht erfasst und fehlen in der Vorschau."
                : "Es ist kein Buchhaltungsbeginn festgelegt: Das Mieterkonto enthält keine Vorauszahlungen, die Vorschau rechnet ohne.");
        }

        return new OperatingCostPreview(property.Id, property.Name, OperatingCostCalculator.Calculate(input), hints);
    }

    private static BillingUnit ToBillingUnit(Unit unit) =>
        new(
            unit.Id,
            unit.Name,
            [.. unit.Areas.Select(a => new AreaState(a.Validity, a.LivingArea, a.HeatedArea))],
            [.. unit.Room.OrderBy(z => z.Order).Select(z => new BillingRoom(z.Id, z.Name, [.. z.Areas.Select(a => new RoomAreaState(a.Validity, a.AllocationArea))]))],
            unit.SharedFlatBilling);

    private async Task<List<BillingCost>> CostsAsync(Guid propertyId, Period billingPeriod, CancellationToken cancellationToken)
    {
        var end = billingPeriod.To!.Value;
        var items = await db.Set<CostItem>().AsNoTracking()
            .Where(p => p.PropertyId == propertyId && p.BillingPeriod.From <= end && p.BillingPeriod.To >= billingPeriod.From)
            .ToListAsync(cancellationToken);
        var receiptIds = items.Select(p => p.ReceiptId).Distinct().ToList();
        var receipts = await db.Set<Receipt>().AsNoTracking().Where(b => receiptIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        var supplierIds = receipts.Values.Select(b => b.SupplierId).OfType<Guid>().Distinct().ToList();
        var suppliers = await db.Set<Person>().AsNoTracking().Where(p => supplierIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        var subtypes = await db.Set<CostSubtype>().AsNoTracking().IgnoreQueryFilters().ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        return [.. items.OrderBy(p => p.BillingPeriod.From).ThenBy(p => p.Id).Select(p =>
        {
            var receipt = receipts[p.ReceiptId];
            var supplier = receipt.SupplierId is { } id ? suppliers.GetValueOrDefault(id) : receipt.SupplierName;
            var description = string.Join(", ", new[] { supplier ?? receipt.SupplierName, receipt.InvoiceNumber is { } number ? $"Rechnung {number}" : $"Rechnung vom {receipt.InvoiceDate:dd.MM.yyyy}" }.Where(t => !string.IsNullOrWhiteSpace(t)));
            return new BillingCost(
                p.Id,
                p.CostType,
                description,
                p.Amount,
                p.BillingPeriod,
                p.Allocable,
                p.UnitId,
                p.Section35a,
                p.LaborCosts,
                p.SubtypeId is { } subtype ? subtypes.GetValueOrDefault(subtype) : null);
        })];
    }
}
