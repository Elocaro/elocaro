using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Betrieb;
using MietHahn.Infrastructure.Jobs;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;
using Quartz;

namespace MietHahn.Integration.Tests.Jobs;

public class QuartzJobsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scheduler_startet_mit_persistentem_Store_und_plant_die_Systemjobs()
    {
        await using var umgebung = await MitJobsAsync();
        await using var laufend = await StartenAsync(umgebung);

        var uebersicht = await UebersichtAsync(umgebung);

        Assert.Contains(uebersicht, j => j.Name == "mail-outbox" && j.NaechsteAusfuehrung is not null);
        var bereinigung = Assert.Single(uebersicht, j => j.Name == "job-protokoll-bereinigung");
        Assert.Equal("Löscht alte Einträge des Job-Laufprotokolls", bereinigung.Beschreibung);
        Assert.NotNull(bereinigung.NaechsteAusfuehrung);
    }

    [Fact]
    public async Task Joblauf_wird_protokolliert()
    {
        await using var umgebung = await MitJobsAsync();
        await using var laufend = await StartenAsync(umgebung);
        var scheduler = await laufend.SchedulerAsync();

        await scheduler.TriggerJob(new JobKey("job-protokoll-bereinigung", "system"), Ct);
        var lauf = await WartenAufLaufAsync(umgebung, "job-protokoll-bereinigung");

        Assert.True(lauf.Erfolgreich);
        Assert.NotNull(lauf.BeendetAm);
        var status = Assert.Single(await UebersichtAsync(umgebung), j => j.Name == "job-protokoll-bereinigung");
        Assert.True(status.LetzterLaufErfolgreich);
    }

    [Fact]
    public async Task Fehlschlag_eines_Jobs_wird_mit_Meldung_protokolliert()
    {
        await using var umgebung = await MitJobsAsync();
        await using var laufend = await StartenAsync(umgebung);
        var scheduler = await laufend.SchedulerAsync();

        var schluessel = new JobKey("absichtlich-fehlerhaft", "test");
        await scheduler.ScheduleJob(
            JobBuilder.Create<FehlerhafterJob>().WithIdentity(schluessel).Build(),
            TriggerBuilder.Create().StartNow().Build(),
            Ct);
        var lauf = await WartenAufLaufAsync(umgebung, schluessel.Name);

        Assert.False(lauf.Erfolgreich);
        Assert.Equal("Absichtlicher Testfehler", lauf.Fehler);
    }

    [Fact]
    public async Task Geplante_Jobs_ueberstehen_einen_Neustart()
    {
        await using var umgebung = await MitJobsAsync();
        var schluessel = new JobKey("einmalig-spaeter", "test");

        await using (var erster = await StartenAsync(umgebung))
        {
            var scheduler = await erster.SchedulerAsync();
            await scheduler.ScheduleJob(
                JobBuilder.Create<FehlerhafterJob>().WithIdentity(schluessel).Build(),
                TriggerBuilder.Create().StartAt(DateTimeOffset.UtcNow.AddDays(1)).Build(),
                Ct);
        }

        // Neuer Prozess (neuer ServiceProvider) gegen dieselbe Datenbank.
        await using var neu = await InfrastrukturUmgebung.ErstellenAsync(postgres, new Dictionary<string, string?>
        {
            ["Jobs:Aktiv"] = "true",
            ["ConnectionStrings:MietHahn"] = umgebung.Verbindung,
        });
        await using var zweiter = await StartenAsync(neu);
        var neuerScheduler = await zweiter.SchedulerAsync();

        Assert.True(await neuerScheduler.CheckExists(schluessel, Ct));
    }

    [Fact]
    public async Task Bereinigung_loescht_nur_alte_Protokolleintraege()
    {
        await using var umgebung = await MitJobsAsync();
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            db.AddRange(
                new JobLauf("alt", InfrastrukturUmgebung.Startzeit.AddDays(-31)),
                new JobLauf("neu", InfrastrukturUmgebung.Startzeit.AddDays(-29)));
            await db.SaveChangesAsync(Ct);
        }

        await using var laufend = await StartenAsync(umgebung);
        await (await laufend.SchedulerAsync()).TriggerJob(new JobKey("job-protokoll-bereinigung", "system"), Ct);
        await WartenAufLaufAsync(umgebung, "job-protokoll-bereinigung");

        await using var pruefung = umgebung.Services.CreateAsyncScope();
        var namen = await pruefung.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<JobLauf>()
            .Select(l => l.JobName).ToListAsync(Ct);
        Assert.DoesNotContain("alt", namen);
        Assert.Contains("neu", namen);
    }

    private Task<InfrastrukturUmgebung> MitJobsAsync() =>
        InfrastrukturUmgebung.ErstellenAsync(postgres, new Dictionary<string, string?> { ["Jobs:Aktiv"] = "true" });

    /// <summary>Startet den Scheduler wie der QuartzHostedService (der in Tests keinen Host hat).</summary>
    private static async Task<LaufenderScheduler> StartenAsync(InfrastrukturUmgebung umgebung)
    {
        var scheduler = await umgebung.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(Ct);
        await scheduler.Start(Ct);
        return new LaufenderScheduler(scheduler);
    }

    private static async Task<IReadOnlyList<JobStatus>> UebersichtAsync(InfrastrukturUmgebung umgebung)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IJobUebersicht>().LadenAsync(Ct);
    }

    private static async Task<JobLauf> WartenAufLaufAsync(InfrastrukturUmgebung umgebung, string jobName)
    {
        var bis = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < bis)
        {
            await using var scope = umgebung.Services.CreateAsyncScope();
            var lauf = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<JobLauf>().AsNoTracking()
                .Where(l => l.JobName == jobName && l.BeendetAm != null)
                .FirstOrDefaultAsync(Ct);
            if (lauf is not null)
            {
                return lauf;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException($"Job {jobName} wurde nicht innerhalb von 30 Sekunden protokolliert.");
    }

    private sealed class LaufenderScheduler(IScheduler scheduler) : IAsyncDisposable
    {
        public Task<IScheduler> SchedulerAsync() => Task.FromResult(scheduler);

        public async ValueTask DisposeAsync() => await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
    }
}

/// <summary>Testjob, der immer fehlschlägt.</summary>
public sealed class FehlerhafterJob : IJob
{
    public Task Execute(IJobExecutionContext context) => throw new InvalidOperationException("Absichtlicher Testfehler");
}
