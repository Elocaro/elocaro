using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Löscht alte Einträge des Job-Laufprotokolls (täglich). Technische Daten, daher physisch gelöscht.</summary>
[DisallowConcurrentExecution]
internal sealed class JobLogCleanupJob(MietHahnDbContext db, TimeProvider clock, IOptions<JobsOptions> options) : IJob
{
    public static readonly JobKey Key = new("job-log-cleanup", "system");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cancellationToken = context.CancellationToken;
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.LogRetentionDays);
        await db.Set<JobRun>().IgnoreQueryFilters().Where(l => l.StartedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
