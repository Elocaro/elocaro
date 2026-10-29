using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Audit;

internal sealed class ChangeHistory(ElocaroDbContext db) : IChangeHistory
{
    public async Task<IReadOnlyList<AuditEntryView>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var entries = await db.Set<AuditEntry>()
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

        return entries
            .Select(a => new AuditEntryView(a.Timestamp, a.User, a.Action, AuditCapture.Read(a.ChangesJson), a.Purpose))
            .ToList();
    }
}
