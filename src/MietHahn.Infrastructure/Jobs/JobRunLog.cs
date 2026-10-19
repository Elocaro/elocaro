using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Schreibt Start, Ende und Fehler jeder Jobausführung in <see cref="JobRun"/>.</summary>
internal sealed partial class JobRunLog(IServiceScopeFactory scopes, TimeProvider clock, ILogger<JobRunLog> logger) : IJobListener
{
    // Zuordnung Ausführung (FireInstanceId) → Protokolleintrag; der Listener ist ein Singleton.
    private readonly ConcurrentDictionary<string, Guid> runIds = new(StringComparer.Ordinal);

    public string Name => nameof(JobRunLog);

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
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
