using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Audit;

/// <summary>Löscht Audit-Einträge nach Ablauf der Aufbewahrungsfristen (Änderungen und Zugriffe getrennt).</summary>
[DisallowConcurrentExecution]
internal sealed class AuditCleanupJob(AuditCleanup cleanup) : IJob
{
    public static readonly JobKey Key = new("audit-bereinigung", "system");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await cleanup.CleanUpAsync(context.CancellationToken);
    }
}

internal sealed class AuditCleanup(MietHahnDbContext db, TimeProvider clock, IOptions<AuditOptions> options)
{
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var changesCutoff = now.AddYears(-options.Value.RetentionYears);
        var accessesCutoff = now.AddMonths(-options.Value.AccessLogRetentionMonths);

        var changes = await db.Set<AuditEntry>()
            .Where(a => a.Action != AuditAction.Read && a.Timestamp < changesCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var accesses = await db.Set<AuditEntry>()
            .Where(a => a.Action == AuditAction.Read && a.Timestamp < accessesCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        return changes + accesses;
    }
}
