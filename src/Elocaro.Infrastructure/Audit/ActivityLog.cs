using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Audit;

internal sealed class ActivityLog(ElocaroDbContext db) : IActivityLog
{
    public async Task<IReadOnlyList<ActivityRow>> RecentAsync(ChangeChannel? channel = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var operations = await db.Set<Operation>()
            .AsNoTracking()
            .Where(o => channel == null || o.Channel == channel)
            .OrderByDescending(o => o.Timestamp)
            .ThenByDescending(o => o.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
        var ids = operations.Select(o => o.Id).ToList();

        var touched = (await db.Set<AuditEntry>()
                .AsNoTracking()
                .Where(a => a.OperationId != null && ids.Contains(a.OperationId.Value) && a.Action != AuditAction.Read && a.Action != AuditAction.SecurityEvent)
                .GroupBy(a => new { a.OperationId, a.EntityType, a.Action })
                .Select(g => new { g.Key.OperationId, g.Key.EntityType, g.Key.Action, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.OperationId!.Value);
        var undone = (await db.Set<Operation>()
                .AsNoTracking()
                .Where(o => o.UndoOf != null && ids.Contains(o.UndoOf.Value))
                .Select(o => o.UndoOf!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return [.. operations.Select(o =>
        {
            var rows = touched[o.Id].OrderBy(t => t.EntityType, StringComparer.Ordinal).ToList();
            return new ActivityRow(
                o.Id,
                o.Timestamp,
                o.User,
                o.Channel,
                o.Label,
                [.. rows.Select(t => new ActivityTouch(t.EntityType, t.Action, t.Count))],
                o.UndoOf,
                undone.Contains(o.Id),
                o.UndoOf is null && rows.Count > 0 && rows.All(t => t.Action is AuditAction.Created or AuditAction.Modified or AuditAction.Deleted && UndoPolicy.IsRestorable(t.EntityType)));
        })];
    }
}
