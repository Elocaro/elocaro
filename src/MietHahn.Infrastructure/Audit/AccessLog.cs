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
    /// <summary>Innerhalb dieses Zeitraums wird derselbe Zugriff (Benutzer, Datensatz, Zweck) nur einmal protokolliert.</summary>
    public static readonly TimeSpan TimeWindow = TimeSpan.FromMinutes(30);

    public async Task LogAsync(string entityType, Guid entityId, string purpose, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (!options.Value.LogAccess)
        {
            return;
        }

        // Wiederholtes Öffnen (z. B. nach jedem Speichern) nicht jedes Mal protokollieren: ein Eintrag je Zeitfenster genügt als Nachweis.
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
