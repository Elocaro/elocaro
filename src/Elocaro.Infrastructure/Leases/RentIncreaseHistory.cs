using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Leases;

/// <summary>Facts about earlier rent increases that the lockout rules need.</summary>
internal static class RentIncreaseHistory
{
    /// <summary>
    /// Days on which increases after modernisation (§ 559 BGB) took effect, per lease. They are part of the base rent but neither
    /// restart the 15 months and the lockout of § 558 Abs. 1 BGB nor the lockout of § 557b Abs. 2 BGB.
    /// </summary>
    public static async Task<ILookup<Guid, DateOnly>> ModernisationDatesAsync(ElocaroDbContext db, IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(leaseIds);
        var rows = await db.Set<RentIncrease>().AsNoTracking()
            .Where(r => leaseIds.Contains(r.LeaseId) && r.Kind == RentIncreaseKind.Modernisation && r.Status == RentIncreaseStatus.InForce)
            .Select(r => new { r.LeaseId, r.EffectiveFrom })
            .ToListAsync(cancellationToken);
        return rows.ToLookup(r => r.LeaseId, r => r.EffectiveFrom);
    }

    public static async Task<IReadOnlyCollection<DateOnly>> ModernisationDatesAsync(ElocaroDbContext db, Guid leaseId, CancellationToken cancellationToken) =>
        [.. (await ModernisationDatesAsync(db, [leaseId], cancellationToken))[leaseId]];
}
