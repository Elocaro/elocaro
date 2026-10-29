using Elocaro.Application.Admin;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Properties;

/// <summary>Whether rooms, parking spaces and commercial units exist.</summary>
internal sealed class PropertyOptionUsage(ElocaroDbContext db) : IOptionUsageSource
{
    public async Task<IReadOnlyDictionary<AppOption, int>> CountAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<AppOption, int>();
        var rooms = await db.Set<Room>().CountAsync(cancellationToken);
        if (rooms > 0)
        {
            result[AppOption.RoomRental] = rooms;
        }

        foreach (var row in await db.Set<Unit>().GroupBy(u => u.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
        {
            if (row.Key == UnitType.ParkingSpace)
            {
                result[AppOption.UnitParking] = row.Count;
            }
            else if (row.Key == UnitType.Commercial)
            {
                result[AppOption.UnitCommercial] = row.Count;
            }
        }

        return result;
    }
}
