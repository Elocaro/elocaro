using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.OperatingCosts;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Finance;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.OperatingCosts;

internal sealed class CostAllocationService(MietHahnDbContext db) : ICostAllocation
{
    public async Task<CostAllocationOverview?> GetAsync(Guid propertyId, DateOnly day, CancellationToken cancellationToken = default)
    {
        var property = await db.Set<Property>().AsNoTracking().Include(o => o.Units).ThenInclude(e => e.Room).AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var rules = await db.Set<CostAllocationRule>().AsNoTracking().Include(r => r.Shares).Where(r => r.PropertyId == propertyId).ToListAsync(cancellationToken);
        var unitNames = property.Units.ToDictionary(e => e.Id, e => e.Name);
        CostRuleView View(CostAllocationRule r) => new(
            r.Id, r.Validity, r.Key, r.MeterType, r.PreDeductionPercent, r.PreDeductionAmount, r.PreDeductionNote,
            r.Shares.Select(a => new UnitPercent(a.UnitId, unitNames.GetValueOrDefault(a.UnitId, "?"), a.Percent)).OrderBy(a => a.UnitName, StringComparer.CurrentCulture).ToList(),
            r.ExcludeVacantRooms);

        var rows = CostTypes.All.Where(t => t.Allocable).Select(t =>
        {
            var series = rules.Where(r => r.CostType == t.Key).OrderByDescending(r => r.Validity.From).ToList();
            var current = series.FirstOrDefault(r => r.Validity.Contains(day));
            return new CostKeyRow(t.Key, t.Name, t.BetrKvNumber, CostGroups.Of(t.Key), current is null ? null : View(current), series.Select(View).ToList());
        }).ToList();

        var sharedFlats = property.Units.Where(e => e.IsSharedFlat).OrderBy(e => e.Order)
            .Select(e => new SharedFlatRow(e.Id, e.Name, e.SharedFlatBilling, e.Room.OrderBy(z => z.Order).Select(z => z.Name).ToList())).ToList();
        var units = property.Units.Where(e => e.Type is UnitType.Apartment or UnitType.Commercial).OrderBy(e => e.Order)
            .Select(e => new ReferenceOption(e.Id, e.Name)).ToList();
        return new CostAllocationOverview(property.Id, property.Name, day, rows, sharedFlats, units);
    }

    public Task<Result> SetRuleAsync(Guid propertyId, CostRuleInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var unitIds = await db.Set<Unit>().Where(e => e.PropertyId == propertyId).Select(e => e.Id).ToListAsync(cancellationToken);
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var shares = ParseShares(input, unitIds);
            var series = await db.Set<CostAllocationRule>().Include(r => r.Shares)
                .Where(r => r.PropertyId == propertyId && r.CostType == input.CostType).ToListAsync(cancellationToken);
            var before = series.ToList();
            var rule = CostAllocationRule.SetFrom(
                series,
                propertyId,
                input.CostType,
                input.From ?? throw new RuleViolationException("Bitte den Stichtag angeben."),
                input.Key,
                input.MeterType,
                input.PreDeductionPercent,
                input.PreDeductionAmount,
                input.PreDeductionNote,
                shares,
                input.ExcludeVacantRooms);
            foreach (var removed in before.Except(series))
            {
                db.Remove(removed);
            }

            db.Add(rule);
            await db.SaveChangesAsync(cancellationToken);
            return rule.Id;
        });

    public Task<Result> EndRuleAsync(Guid propertyId, string costType, DateOnly lastDay, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var series = await db.Set<CostAllocationRule>().Where(r => r.PropertyId == propertyId && r.CostType == costType).ToListAsync(cancellationToken);
            var current = series.FirstOrDefault(r => r.Validity.Contains(lastDay)) ?? throw new RuleViolationException("Zu diesem Tag gibt es keinen Umlageschlüssel.");
            if (lastDay < current.Validity.From)
            {
                throw new RuleViolationException("Das Ende liegt vor dem Beginn des Umlageschlüssels.");
            }

            CostAllocationRule.EndOn(series, lastDay);
            await db.SaveChangesAsync(cancellationToken);
            return current.Id;
        });

    public Task<Result> SetSharedFlatBillingAsync(Guid unitId, SharedFlatBilling mode, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var unit = await db.Set<Unit>().Include(e => e.Room).SingleOrDefaultAsync(e => e.Id == unitId, cancellationToken) ?? throw new NotFoundException();
            if (!unit.IsSharedFlat)
            {
                throw new RuleViolationException("Nur Wohngemeinschaften mit Zimmern haben einen Abrechnungsmodus.");
            }

            unit.SharedFlatBilling = mode;
            await db.SaveChangesAsync(cancellationToken);
            return unit.Id;
        });

    public async Task<LeaseCostAgreement?> GetLeaseAgreementAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var (rentalUnit, tenant) = (await TenantAccounts.DescriptionsAsync(db, [lease], cancellationToken))[lease.Id];
        var propertyId = await db.Set<Unit>().Where(e => e.Id == lease.UnitId).Select(e => e.PropertyId).SingleAsync(cancellationToken);
        var rows = CostTypes.All.Where(t => t.Allocable).Select(t => new AgreementRow(t.Key, t.Name, t.BetrKvNumber, lease.IsAgreed(t.Key))).ToList();
        return new LeaseCostAgreement(lease.Id, $"{tenant}, {rentalUnit}", propertyId, rows);
    }

    public Task<Result> SetLeaseAgreementAsync(Guid leaseId, IReadOnlyCollection<string> excludedCostTypes, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(excludedCostTypes);
            var lease = await db.Set<Lease>().SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken) ?? throw new NotFoundException();
            lease.SetExcludedCostTypes(excludedCostTypes);
            await db.SaveChangesAsync(cancellationToken);
            return lease.Id;
        });

    private static List<FixedShare> ParseShares(CostRuleInput input, List<Guid> unitIds)
    {
        var result = new List<FixedShare>();
        if (input.Key != AllocationKey.FixedPercent)
        {
            return result;
        }

        foreach (var share in input.Shares.Where(a => !string.IsNullOrWhiteSpace(a.Percent)))
        {
            if (!Guid.TryParse(share.UnitId, out var unit) || !unitIds.Contains(unit))
            {
                throw new RuleViolationException("Die Einheit gehört nicht zum Objekt.");
            }

            if (!decimal.TryParse(share.Percent!.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var percent))
            {
                throw new RuleViolationException($"„{share.Percent}“ ist kein gültiger Prozentsatz.");
            }

            result.Add(new FixedShare(unit, percent));
        }

        return result;
    }
}

internal sealed class CostSubtypeService(MietHahnDbContext db) : ICostSubtypes
{
    public async Task<IReadOnlyList<CostSubtypeView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = await db.Set<CostSubtype>().AsNoTracking().ToListAsync(cancellationToken);
        return list
            .OrderBy(a => CostTypes.All.ToList().FindIndex(t => t.Key == a.CostType)).ThenBy(a => a.Name, StringComparer.CurrentCulture)
            .Select(a => new CostSubtypeView(a.Id, a.CostType, CostTypes.Find(a.CostType)?.Name ?? a.CostType, a.Name)).ToList();
    }

    public Task<Result> AddAsync(string costType, string name, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var subtype = new CostSubtype(costType, name);
            var existing = await db.Set<CostSubtype>().Where(a => a.CostType == costType).Select(a => a.Name).ToListAsync(cancellationToken);
            if (existing.Contains(subtype.Name, StringComparer.OrdinalIgnoreCase))
            {
                throw new RuleViolationException($"Die Unterart „{subtype.Name}“ gibt es schon.");
            }

            db.Add(subtype);
            await db.SaveChangesAsync(cancellationToken);
            return subtype.Id;
        });

    public Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var subtype = await db.Set<CostSubtype>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw new NotFoundException();
            await db.Set<CostItem>().Where(p => p.SubtypeId == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.SubtypeId, (Guid?)null), cancellationToken);
            db.Remove(subtype);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });
}
