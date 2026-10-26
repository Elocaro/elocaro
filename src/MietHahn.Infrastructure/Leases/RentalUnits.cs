using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Leases;

/// <summary>Labels of the rentable objects (unit without rooms or shared-flat room) for display and validation.</summary>
internal sealed record RentalUnit(Guid Id, Guid UnitId, Guid? RoomId, Guid PropertyId, string Name, string Property, bool IsRentable)
{
    /// <summary>Name of the rented object of a lease: "WE 03, Zimmer 2 und Zimmer 3" when the lease covers several rooms.</summary>
    public static string LabelOf(Guid rentalUnitId, IEnumerable<Guid> extraRoomIds, IReadOnlyDictionary<Guid, RentalUnit> all)
    {
        var first = all.GetValueOrDefault(rentalUnitId)?.Name ?? "–";
        var more = extraRoomIds.Select(id => all.GetValueOrDefault(id)?.Name).OfType<string>().Select(n => n[(n.LastIndexOf(", ", StringComparison.Ordinal) + 2)..]).ToList();
        return more.Count == 0 ? first : $"{first} und {string.Join(" und ", more)}";
    }

    public static async Task<Dictionary<Guid, RentalUnit>> GetAsync(MietHahnDbContext db, CancellationToken cancellationToken)
    {
        var properties = await db.Set<Property>().AsNoTracking()
            .Include(o => o.Units).ThenInclude(e => e.Room)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, RentalUnit>();
        foreach (var property in properties.OrderBy(o => o.Name, StringComparer.CurrentCulture))
        {
            foreach (var unit in property.Units.OrderBy(e => e.Order))
            {
                result[unit.Id] = new(unit.Id, unit.Id, null, property.Id, unit.Name, property.Name, unit.IsSelfRentable);
                foreach (var room in unit.Room.OrderBy(z => z.Order))
                {
                    result[room.Id] = new(room.Id, unit.Id, room.Id, property.Id, $"{unit.Name}, {room.Name}", property.Name, true);
                }
            }
        }

        return result;
    }
}
