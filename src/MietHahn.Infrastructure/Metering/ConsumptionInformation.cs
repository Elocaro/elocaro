using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Metering;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Notices;
using MietHahn.Infrastructure.Persistence;
using ZaehlerEntitaet = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Infrastructure.Metering;

internal sealed class ConsumptionInformation(MietHahnDbContext db, TimeProvider clock) : IConsumptionInformation
{
    private static readonly MeterType[] Types = [MeterType.Heat, MeterType.HotWater];

    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<MonthlyConsumption>> ForLeaseAsync(Guid leaseId, int months, CancellationToken cancellationToken = default)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.People).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        return lease is null ? [] : await MonthsAsync(lease, months, cancellationToken);
    }

    public async Task<IReadOnlyList<TenantConsumption>> ForTenantAsync(ClaimsPrincipal user, int months, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        if (viewer != NoticeViewer.Tenant)
        {
            return [];
        }

        var leases = (await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.People).ToListAsync(cancellationToken))
            .Where(v => v.Tenant.Any(p => persons.Contains(p.PersonId)) && (v.Term.To is null || v.Term.To >= Today.AddMonths(-months))).ToList();
        var names = await RentalUnit.GetAsync(db, cancellationToken);
        var result = new List<TenantConsumption>();
        foreach (var lease in leases)
        {
            var list = await MonthsAsync(lease, months, cancellationToken);
            if (list.Count > 0)
            {
                result.Add(new TenantConsumption(lease.Id, names.GetValueOrDefault(lease.RentalUnitId)?.Name ?? "Wohnung", list));
            }
        }

        return result;
    }

    public async Task<MonthlyConsumption?> ForLeaseMonthAsync(Guid leaseId, DateOnly month, CancellationToken cancellationToken = default)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.People).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        var context = lease is null ? null : await ContextAsync(lease, cancellationToken);
        return context is null ? null : Build(context, lease!, new DateOnly(month.Year, month.Month, 1));
    }

    private async Task<IReadOnlyList<MonthlyConsumption>> MonthsAsync(Lease lease, int months, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(lease, cancellationToken);
        if (context is null)
        {
            return [];
        }

        var current = new DateOnly(Today.Year, Today.Month, 1);
        var result = new List<MonthlyConsumption>();
        for (var i = 1; i <= months; i++)
        {
            var month = current.AddMonths(-i);
            var last = month.AddMonths(1).AddDays(-1);
            if (last < lease.Start || (lease.End is { } end && month > end))
            {
                continue;
            }

            var built = Build(context, lease, month);
            if (built.Lines.Count > 0)
            {
                result.Add(built);
            }
        }

        return result;
    }

    private async Task<Context?> ContextAsync(Lease lease, CancellationToken cancellationToken)
    {
        var unit = await db.Set<Unit>().AsNoTracking().Include(e => e.Areas).SingleOrDefaultAsync(e => e.Id == lease.UnitId, cancellationToken);
        if (unit is null)
        {
            return null;
        }

        var meters = await db.Set<ZaehlerEntitaet>().AsNoTracking().Include(z => z.Readings).Include(z => z.Terms)
            .Where(z => z.PropertyId == unit.PropertyId && Types.Contains(z.Type)).ToListAsync(cancellationToken);
        if (!meters.Any(z => z.UnitId == unit.Id && z.RoomId == null))
        {
            return null;
        }

        var units = await db.Set<Unit>().AsNoTracking().Include(e => e.Areas).Where(e => e.PropertyId == unit.PropertyId).ToDictionaryAsync(e => e.Id, cancellationToken);
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.People).Where(v => v.RoomId == null && units.Keys.Contains(v.UnitId)).ToListAsync(cancellationToken);
        return new Context(unit, meters, units, leases);
    }

    private static MonthlyConsumption Build(Context context, Lease lease, DateOnly month)
    {
        var lines = new List<ConsumptionLine>();
        foreach (var type in Types)
        {
            if (!context.Meters.Any(z => z.Type == type && z.UnitId == context.Unit.Id && z.RoomId == null))
            {
                continue;
            }

            var (current, hint) = Sum(context, context.Unit.Id, type, month);
            var (previous, _) = Sum(context, context.Unit.Id, type, month.AddMonths(-1));
            var (lastYear, _) = Sum(context, context.Unit.Id, type, month.AddYears(-1));
            var last = month.AddMonths(1).AddDays(-1);
            var basisText = type == MeterType.Heat ? "je m²" : "je Person";

            var shares = new List<decimal>();
            foreach (var (unitId, unit) in context.Units)
            {
                if (!context.Meters.Any(z => z.Type == type && z.UnitId == unitId && z.RoomId == null))
                {
                    continue;
                }

                var other = context.Leases.FirstOrDefault(v => v.UnitId == unitId && v.Start <= last && (v.End is null || v.End >= month));
                var basis = Basis(type, unit, other, last);
                var quantity = Sum(context, unitId, type, month).Quantity;
                if (other is not null && basis is > 0 && quantity is { } value)
                {
                    shares.Add(value / basis.Value);
                }
            }

            var ownBasis = Basis(type, context.Unit, lease, last);
            decimal? own = current is { } mine && ownBasis is > 0 ? Math.Round(mine / ownBasis.Value, 3, MidpointRounding.AwayFromZero) : null;
            var first = context.Meters.First(z => z.Type == type && z.UnitId == context.Unit.Id);
            lines.Add(new ConsumptionLine(type, type == MeterType.Heat ? "Heizung (Wärme)" : "Warmwasser", first.MeasurementUnit, current, previous, lastYear, own,
                ConsumptionComparison.Average(shares), shares.Count, basisText, hint));
        }

        return new MonthlyConsumption(month, lines);
    }

    /// <summary>Consumption of all meters of a type in a unit in a month; <c>null</c> if a reading is missing.</summary>
    private static (decimal? Quantity, string? Hint) Sum(Context context, Guid unitId, MeterType type, DateOnly month)
    {
        var period = new Period(month, month.AddMonths(1).AddDays(-1));
        var assigned = context.Meters.Where(z => z.Type == type && z.UnitId == unitId && z.RoomId == null).ToList();
        // A metering point is a chain of replaced meters; count each chain once, via its newest meter.
        var ends = assigned.Where(z => context.Meters.All(o => o.PredecessorId != z.Id)).ToList();
        decimal total = 0;
        foreach (var end in ends)
        {
            var consumption = MeterConsumption.Calculate(MeterService.MeteringPoint(end, context.Meters), context.Meters, period);
            if (consumption.Quantity is not { } quantity)
            {
                return (null, consumption.MissingReason);
            }

            total += quantity;
        }

        return (total, null);
    }

    private static decimal? Basis(MeterType type, Unit unit, Lease? lease, DateOnly day) =>
        type == MeterType.Heat ? unit.AreaOn(day)?.LivingArea : lease?.OccupantCountOn(day);

    private sealed record Context(Unit Unit, List<ZaehlerEntitaet> Meters, Dictionary<Guid, Unit> Units, List<Lease> Leases);
}
