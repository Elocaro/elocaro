using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Application.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Audit;

internal sealed class AccessLog(
    MietHahnDbContext db,
    ICurrentUser user,
    TimeProvider clock,
    IOptions<AuditOptions> options) : IAccessLog
{
    /// <summary>Within this time span the same access (user, record, purpose) is logged only once.</summary>
    public static readonly TimeSpan TimeWindow = TimeSpan.FromMinutes(30);

    public async Task LogAsync(string entityType, Guid entityId, string purpose, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (!options.Value.LogAccess)
        {
            return;
        }

        // Do not log repeated opening (e.g. after every save) each time: one entry per time window suffices as evidence.
        var now = clock.GetUtcNow();
        var since = now - TimeWindow;
        var identifier = user.Identifier;
        if (await db.Set<AuditEntry>().AnyAsync(
                a => a.EntityId == entityId && a.Action == AuditAction.Read && a.User == identifier && a.Purpose == purpose && a.Timestamp >= since,
                cancellationToken))
        {
            return;
        }

        db.Add(new AuditEntry(now, identifier, entityType, entityId, AuditAction.Read, "[]", purpose));
        await db.SaveChangesAsync(cancellationToken);
    }
}
