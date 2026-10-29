using System.Collections.Concurrent;
using Elocaro.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Elocaro.Infrastructure.Jobs;

/// <summary>Writes the start, end and errors of every job execution to <see cref="JobRun"/>.</summary>
internal sealed partial class JobRunLog(IServiceScopeFactory scopes, TimeProvider clock, ILogger<JobRunLog> logger) : IJobListener
{
    // Mapping of execution (FireInstanceId) → log entry; the listener is a singleton.
    private readonly ConcurrentDictionary<string, Guid> runIds = new(StringComparer.Ordinal);

    public string Name => nameof(JobRunLog);

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var run = new JobRun(context.JobDetail.Key.Name, clock.GetUtcNow());
        db.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        runIds[context.FireInstanceId] = run.Id;
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!runIds.TryRemove(context.FireInstanceId, out var runId))
        {
            return;
        }

        if (jobException is not null)
        {
            JobFailed(logger, jobException, context.JobDetail.Key.Name);
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var run = await db.Set<JobRun>().FindAsync([runId], cancellationToken);
        if (run is null)
        {
            return;
        }

        run.End(clock.GetUtcNow(), jobException);
        await db.SaveChangesAsync(cancellationToken);
    }


    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} ist fehlgeschlagen.")]
    private static partial void JobFailed(ILogger logger, Exception errors, string job);
}
