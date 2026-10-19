using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Deletes old entries of the job run log (daily). Technical data, hence physically deleted.</summary>
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
