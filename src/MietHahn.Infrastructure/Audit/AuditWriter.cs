using System.Text.Json;
using MietHahn.Application.Audit;
using MietHahn.Application.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Audit;

/// <summary>Writes audit entries for data not captured by the change tracker (e.g. accounts).</summary>
internal sealed class AuditWriter(MietHahnDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(string entityType, Guid entityId, AuditAction action, IReadOnlyList<FieldChange> changes, CancellationToken cancellationToken = default)
    {
        db.Add(new AuditEntry(clock.GetUtcNow(), user.Identifier, entityType, entityId, action, JsonSerializer.Serialize(changes, Json)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
