using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Elocaro.Infrastructure.Jobs;

/// <summary>Deletes old entries of the job run log (daily). Technical data, hence physically deleted.</summary>
[DisallowConcurrentExecution]
internal sealed class JobLogCleanupJob(ElocaroDbContext db, TimeProvider clock, IOptions<JobsOptions> options) : IJob
{
    public static readonly JobKey Key = new("job-log-cleanup", "system");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.LogRetentionDays);
        await db.Set<JobRun>().IgnoreQueryFilters().Where(l => l.StartedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
