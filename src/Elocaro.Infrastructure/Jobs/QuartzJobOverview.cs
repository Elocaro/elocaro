using Elocaro.Application.Operations;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl.Matchers;

namespace Elocaro.Infrastructure.Jobs;

internal sealed class QuartzJobOverview(ISchedulerFactory schedulerFactory, ElocaroDbContext db) : IJobOverview
{
    public async Task<IReadOnlyList<JobStatus>> GetAsync(CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);

        var lastRuns = await db.Set<JobRun>()
            .GroupBy(l => l.JobName)
            .Select(g => g.OrderByDescending(l => l.StartedAt).First())
            .ToDictionaryAsync(l => l.JobName, cancellationToken);

        var result = new List<JobStatus>();
        foreach (var key in jobKeys.OrderBy(k => k.Name, StringComparer.Ordinal))
        {
            var detail = await scheduler.GetJobDetail(key, cancellationToken);
            var trigger = await scheduler.GetTriggersOfJob(key, cancellationToken);
            var next = trigger.Select(t => t.GetNextFireTimeUtc()).Where(t => t.HasValue).Min();
            lastRuns.TryGetValue(key.Name, out var run);

            result.Add(new JobStatus(key.Name, detail?.Description, next, run?.StartedAt, run?.Succeeded, run?.Errors));
        }

        return result;
    }
}
