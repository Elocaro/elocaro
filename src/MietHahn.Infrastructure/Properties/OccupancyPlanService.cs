using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Finance;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Properties;

internal sealed class OccupancyPlanService(MietHahnDbContext db, TimeProvider clock) : IOccupancyPlan
{
    public async Task<OccupancyPlan?> GetAsync(Guid propertyId, Period range, CancellationToken cancellationToken = default)
    {
        var end = range.To ?? throw new ArgumentException("Der Zeitraum braucht ein Ende.", nameof(range));
        var property = await db.Set<Property>().AsNoTracking().Include(o => o.Units).ThenInclude(e => e.Room).AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var units = property.Units.Where(e => e.Type is UnitType.Apartment or UnitType.Commercial).OrderBy(e => e.Order).ThenBy(e => e.Name, StringComparer.CurrentCulture).ToList();
        var unitIds = units.Select(e => e.Id).ToList();
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.Components).Include(v => v.ExtraRooms)
            .Where(v => unitIds.Contains(v.UnitId) && v.Term.From <= end && (v.Term.To == null || v.Term.To >= range.From))
            .AsSplitQuery().ToListAsync(cancellationToken);
        var descriptions = await TenantAccounts.DescriptionsAsync(db, leases, cancellationToken);
        var today = GermanTime.Today(clock.GetUtcNow());

        OccupancyBar? Bar(Lease lease)
        {
            if (lease.Term.Intersect(range) is not { } visible)
            {
                return null;
            }

            var shown = new Period(visible.From, visible.To ?? end);
            return new OccupancyBar(
                lease.Id,
                shown,
                lease.Start,
                lease.End,
                descriptions[lease.Id].Tenant,
                lease.MonthlyRentOn(shown.From),
                lease.End is { } over && over < today,
                lease.End is null);
        }

        var rows = new List<OccupancyRow>();
        foreach (var unit in units)
        {
            if (unit.Room.Count == 0)
            {
                rows.Add(new OccupancyRow(unit.Id, unit.Name, null, false, [.. leases.Where(v => v.RentalUnitId == unit.Id).OrderBy(v => v.Start).Select(Bar).OfType<OccupancyBar>()]));
                continue;
            }

            foreach (var room in unit.Room.OrderBy(z => z.Order).ThenBy(z => z.Name, StringComparer.Ordinal))
            {
                rows.Add(new OccupancyRow(room.Id, room.Name, unit.Name, true, [.. leases.Where(v => v.RentalUnitId == room.Id || v.RoomIds.Contains(room.Id)).OrderBy(v => v.Start).Select(Bar).OfType<OccupancyBar>()]));
            }
        }

        return new OccupancyPlan(property.Id, property.Name, range, rows);
    }
}
