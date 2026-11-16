using System.Text.Json;
using Elocaro.Application.Audit;
using Elocaro.Domain.Common;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Elocaro.Infrastructure.Prospects;

/// <summary>
/// Daily: deletes prospects whose deletion day has come (rejected, or taken over as tenant) for good, not only marked as deleted. The change log
/// of such a prospect holds names and contact data too, so it goes with it; what stays is one entry per prospect that says it was deleted, without personal data.
/// The job is not tied to the module: switching the module off must not keep the data of strangers.
/// </summary>
internal sealed class ProspectRetention(ElocaroDbContext db, TimeProvider clock)
{
    public const string Reason = "Löschfrist abgelaufen";

    private static readonly string EntityType = nameof(Prospect);

    public async Task<int> CleanUpAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);
        var ids = await db.Set<Prospect>().IgnoreQueryFilters().Where(p => p.DeleteOn != null && p.DeleteOn <= today).Select(p => p.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        var disclosureIds = await db.Set<SelfDisclosure>().IgnoreQueryFilters().Where(s => ids.Contains(s.ProspectId)).Select(s => s.Id).ToListAsync(cancellationToken);
        await db.Set<AuditEntry>().Where(a => (a.EntityType == EntityType && ids.Contains(a.EntityId)) || (a.EntityType == nameof(SelfDisclosure) && disclosureIds.Contains(a.EntityId))).ExecuteDeleteAsync(cancellationToken);
        // The answers go with the prospect (cascade in the database).
        await db.Set<Prospect>().IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync(cancellationToken);
        foreach (var id in ids)
        {
            db.Add(new AuditEntry(now, "System (Löschfrist)", EntityType, id, AuditAction.Deleted, JsonSerializer.Serialize(Array.Empty<FieldChange>()), Reason));
        }

        await db.SaveChangesAsync(cancellationToken);
        return ids.Count;
    }
}

[DisallowConcurrentExecution]
internal sealed class ProspectRetentionJob(ProspectRetention retention) : IJob
{
    public static readonly JobKey Key = new("prospect-retention", "system");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await retention.CleanUpAsync(cancellationToken);
    }
}
