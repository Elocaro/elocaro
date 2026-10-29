using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Properties;

/// <summary>Which sender details a document carries: the profile chosen for the property, otherwise the standard profile (the oldest one).</summary>
internal static class SenderProfiles
{
    /// <summary>The profile of the property on the given day (default: today): the latest entry up to that day, else the profile chosen on the property, else the standard profile.</summary>
    public static async Task<LandlordProfile?> ForPropertyAsync(ElocaroDbContext db, Guid? propertyId, CancellationToken cancellationToken, DateOnly? on = null)
    {
        if (propertyId is { } assigned)
        {
            var day = on ?? GermanTime.Today(TimeProvider.System.GetUtcNow());
            var scheduled = await db.Set<PropertyLandlord>().AsNoTracking()
                .Where(a => a.PropertyId == assigned && a.ValidFrom <= day)
                .OrderByDescending(a => a.ValidFrom).Select(a => (Guid?)a.LandlordProfileId).FirstOrDefaultAsync(cancellationToken);
            if (scheduled is { } scheduledId && await db.Set<LandlordProfile>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == scheduledId, cancellationToken) is { } fromEntry)
            {
                return fromEntry;
            }
        }

        if (propertyId is { } id
            && await db.Set<Property>().AsNoTracking().Where(o => o.Id == id).Select(o => o.LandlordProfileId).SingleOrDefaultAsync(cancellationToken) is { } profileId
            && await db.Set<LandlordProfile>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == profileId, cancellationToken) is { } chosen)
        {
            return chosen;
        }

        return await db.Set<LandlordProfile>().AsNoTracking().OrderBy(v => v.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>For documents that belong to a unit (leases, letters, statements).</summary>
    public static async Task<LandlordProfile?> ForUnitAsync(ElocaroDbContext db, Guid unitId, CancellationToken cancellationToken) =>
        await ForPropertyAsync(db, await db.Set<Unit>().AsNoTracking().Where(e => e.Id == unitId).Select(e => (Guid?)e.PropertyId).SingleOrDefaultAsync(cancellationToken), cancellationToken);
}
