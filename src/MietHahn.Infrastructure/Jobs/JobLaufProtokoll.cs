using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MietHahn.Infrastructure.Persistenz;
using Quartz;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Schreibt Start, Ende und Fehler jeder Jobausführung in <see cref="JobLauf"/>.</summary>
internal sealed partial class JobLaufProtokoll(IServiceScopeFactory scopes, TimeProvider uhr, ILogger<JobLaufProtokoll> logger) : IJobListener
{
    // Zuordnung Ausführung (FireInstanceId) → Protokolleintrag; der Listener ist ein Singleton.
    private readonly ConcurrentDictionary<string, Guid> laufIds = new(StringComparer.Ordinal);

    public string Name => nameof(JobLaufProtokoll);

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var lauf = new JobLauf(context.JobDetail.Key.Name, uhr.GetUtcNow());
        db.Add(lauf);
        await db.SaveChangesAsync(cancellationToken);
        laufIds[context.FireInstanceId] = lauf.Id;
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!laufIds.TryRemove(context.FireInstanceId, out var laufId))
        {
            return;
        }

        if (jobException is not null)
        {
            JobFehlgeschlagen(logger, jobException, context.JobDetail.Key.Name);
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var lauf = await db.Set<JobLauf>().FindAsync([laufId], cancellationToken);
        if (lauf is null)
        {
            return;
        }

        lauf.Beenden(uhr.GetUtcNow(), jobException);
        await db.SaveChangesAsync(cancellationToken);
    }


    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} ist fehlgeschlagen.")]
    private static partial void JobFehlgeschlagen(ILogger logger, Exception fehler, string job);
}
