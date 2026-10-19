using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Operations;
using MietHahn.Infrastructure.Jobs;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;
using Quartz;

namespace MietHahn.Integration.Tests.Jobs;

public class QuartzJobsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scheduler_starts_with_persistent_store_and_schedules_system_jobs()
    {
        await using var environment = await WithJobsAsync();
        await using var running = await StartAsync(environment);

        var overview = await OverviewAsync(environment);

        Assert.Contains(overview, j => j.Name == "mail-outbox" && j.NextRun is not null);
        var cleanup = Assert.Single(overview, j => j.Name == "job-protokoll-bereinigung");
        Assert.Equal("Löscht alte Einträge des Job-Laufprotokolls", cleanup.Description);
        Assert.NotNull(cleanup.NextRun);
    }

    [Fact]
    public async Task Job_run_is_logged()
    {
        await using var environment = await WithJobsAsync();
        await using var running = await StartAsync(environment);
        var scheduler = await running.SchedulerAsync();

        await scheduler.TriggerJob(new JobKey("job-protokoll-bereinigung", "system"), Ct);
        var run = await WaitForRunAsync(environment, "job-protokoll-bereinigung");

        Assert.True(run.Succeeded);
        Assert.NotNull(run.EndedAt);
        var status = Assert.Single(await OverviewAsync(environment), j => j.Name == "job-protokoll-bereinigung");
        Assert.True(status.LastRunSucceeded);
    }

    [Fact]
    public async Task Job_failure_is_logged_with_message()
    {
        await using var environment = await WithJobsAsync();
        await using var running = await StartAsync(environment);
        var scheduler = await running.SchedulerAsync();

        var key = new JobKey("absichtlich-fehlerhaft", "test");
        await scheduler.ScheduleJob(
            JobBuilder.Create<FailingJob>().WithIdentity(key).Build(),
            TriggerBuilder.Create().StartNow().Build(),
            Ct);
        var run = await WaitForRunAsync(environment, key.Name);

        Assert.False(run.Succeeded);
        Assert.Equal("Absichtlicher Testfehler", run.Errors);
    }

    [Fact]
    public async Task Scheduled_jobs_survive_a_restart()
    {
        await using var environment = await WithJobsAsync();
        var key = new JobKey("einmalig-spaeter", "test");

        await using (var first = await StartAsync(environment))
        {
            var scheduler = await first.SchedulerAsync();
            await scheduler.ScheduleJob(
                JobBuilder.Create<FailingJob>().WithIdentity(key).Build(),
                TriggerBuilder.Create().StartAt(DateTimeOffset.UtcNow.AddDays(1)).Build(),
                Ct);
        }

        // Neuer Prozess (neuer ServiceProvider) gegen dieselbe Datenbank.
        await using var newItem = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["Jobs:Active"] = "true",
            ["ConnectionStrings:MietHahn"] = environment.Connection,
        });
        await using var second = await StartAsync(newItem);
        var newScheduler = await second.SchedulerAsync();

        Assert.True(await newScheduler.CheckExists(key, Ct));
    }

    [Fact]
    public async Task Cleanup_deletes_only_old_log_entries()
    {
        await using var environment = await WithJobsAsync();
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            db.AddRange(
                new JobRun("alt", InfrastructureEnvironment.StartTime.AddDays(-31)),
                new JobRun("neu", InfrastructureEnvironment.StartTime.AddDays(-29)));
            await db.SaveChangesAsync(Ct);
        }

        await using var running = await StartAsync(environment);
        await (await running.SchedulerAsync()).TriggerJob(new JobKey("job-protokoll-bereinigung", "system"), Ct);
        await WaitForRunAsync(environment, "job-protokoll-bereinigung");

        await using var check = environment.Services.CreateAsyncScope();
        var names = await check.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<JobRun>()
            .Select(l => l.JobName).ToListAsync(Ct);
        Assert.DoesNotContain("alt", names);
        Assert.Contains("neu", names);
    }

    private Task<InfrastructureEnvironment> WithJobsAsync() =>
        InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Jobs:Active"] = "true" });

    /// <summary>Startet den Scheduler wie der QuartzHostedService (der in Tests keinen Host hat).</summary>
    private static async Task<RunningScheduler> StartAsync(InfrastructureEnvironment environment)
    {
        var scheduler = await environment.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(Ct);
        await scheduler.Start(Ct);
        return new RunningScheduler(scheduler);
    }

    private static async Task<IReadOnlyList<JobStatus>> OverviewAsync(InfrastructureEnvironment environment)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IJobOverview>().GetAsync(Ct);
    }

    private static async Task<JobRun> WaitForRunAsync(InfrastructureEnvironment environment, string jobName)
    {
        var to = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < to)
        {
            await using var scope = environment.Services.CreateAsyncScope();
            var run = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<JobRun>().AsNoTracking()
                .Where(l => l.JobName == jobName && l.EndedAt != null)
                .FirstOrDefaultAsync(Ct);
            if (run is not null)
            {
                return run;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException($"Job {jobName} wurde nicht innerhalb von 30 Sekunden protokolliert.");
    }

    private sealed class RunningScheduler(IScheduler scheduler) : IAsyncDisposable
    {
        public Task<IScheduler> SchedulerAsync() => Task.FromResult(scheduler);

        public async ValueTask DisposeAsync() => await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
    }
}

/// <summary>Testjob, der immer fehlschlägt.</summary>
public sealed class FailingJob : IJob
{
    public Task Execute(IJobExecutionContext context) => throw new InvalidOperationException("Absichtlicher Testfehler");
}
