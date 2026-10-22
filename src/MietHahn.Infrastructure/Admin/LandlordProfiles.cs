using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

/// <summary>Which sender details a document carries: the profile chosen for the property, otherwise the standard profile (the oldest one).</summary>
internal static class LandlordProfiles
{
    public static async Task<LandlordProfile?> ForPropertyAsync(MietHahnDbContext db, Guid? propertyId, CancellationToken cancellationToken)
    {
        if (propertyId is { } id
            && await db.Set<Property>().AsNoTracking().Where(o => o.Id == id).Select(o => o.LandlordProfileId).SingleOrDefaultAsync(cancellationToken) is { } profileId
            && await db.Set<LandlordProfile>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == profileId, cancellationToken) is { } chosen)
        {
            return chosen;
        }

        return await db.Set<LandlordProfile>().AsNoTracking().OrderBy(v => v.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task<Guid?> PropertyOfLeaseAsync(MietHahnDbContext db, Guid leaseId, CancellationToken cancellationToken) =>
        await db.Set<Lease>().AsNoTracking().Where(v => v.Id == leaseId)
            .Join(db.Set<Unit>(), v => v.UnitId, e => e.Id, (v, e) => (Guid?)e.PropertyId).SingleOrDefaultAsync(cancellationToken);

    /// <summary>For documents that belong to a unit (leases, letters, statements).</summary>
    public static async Task<LandlordProfile?> ForUnitAsync(MietHahnDbContext db, Guid unitId, CancellationToken cancellationToken) =>
        await ForPropertyAsync(db, await db.Set<Unit>().AsNoTracking().Where(e => e.Id == unitId).Select(e => (Guid?)e.PropertyId).SingleOrDefaultAsync(cancellationToken), cancellationToken);
}
