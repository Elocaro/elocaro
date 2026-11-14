using Elocaro.Application.Metering;
using Elocaro.Domain.Heating;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Heating;

internal sealed class HeatingReferences(ElocaroDbContext db) : IHeatingReferences
{
    public async Task<IReadOnlyDictionary<DateOnly, decimal>> DegreeDaysAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var station = await db.Set<HeatingSettings>().AsNoTracking().Where(h => h.PropertyId == propertyId).Select(h => h.DegreeDayStationId).FirstOrDefaultAsync(cancellationToken);
        return station is null
            ? new Dictionary<DateOnly, decimal>()
            : await db.Set<DegreeDayValue>().AsNoTracking().Where(v => v.StationId == station).ToDictionaryAsync(v => v.Month, v => v.DegreeDays, cancellationToken);
    }
}

/// <summary>The heating cost settings use meters (hot water heat, oil tank); such a meter cannot be removed.</summary>
internal sealed class HeatingMeterUsage(ElocaroDbContext db) : IMeterUsage
{
    public async Task<IReadOnlyList<string>> UsersAsync(Guid meterId, CancellationToken cancellationToken = default) =>
        await db.Set<HeatingSettings>().AsNoTracking().AnyAsync(h => h.HotWaterHeatMeterId == meterId || h.OilTankMeterId == meterId, cancellationToken)
            ? ["die Heizkosteneinstellungen"]
            : [];
}
