using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Leases;

/// <summary>Bezeichnungen der vermietbaren Objekte (Einheit ohne Zimmer oder WG-Zimmer) für Anzeige und Prüfung.</summary>
internal sealed record RentalUnit(Guid Id, Guid UnitId, Guid? RoomId, Guid PropertyId, string Name, string Property, bool IsRentable)
{
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
