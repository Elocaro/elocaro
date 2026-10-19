using System.Text.Json;
using MietHahn.Application.Audit;
using MietHahn.Application.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Audit;

/// <summary>Schreibt Audit-Einträge für Daten, die nicht über den Change-Tracker erfasst werden (z. B. Konten).</summary>
internal sealed class AuditWriter(MietHahnDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(string entityType, Guid entityId, AuditAction action, IReadOnlyList<FieldChange> changes, CancellationToken cancellationToken = default)
    {
        db.Add(new AuditEntry(clock.GetUtcNow(), user.Identifier, entityType, entityId, action, JsonSerializer.Serialize(changes, Json)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
