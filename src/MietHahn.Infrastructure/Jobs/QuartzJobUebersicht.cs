using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Betrieb;
using MietHahn.Infrastructure.Persistenz;
using Quartz;
using Quartz.Impl.Matchers;

namespace MietHahn.Infrastructure.Jobs;

internal sealed class QuartzJobUebersicht(ISchedulerFactory schedulerFactory, MietHahnDbContext db) : IJobUebersicht
{
    public async Task<IReadOnlyList<JobStatus>> LadenAsync(CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var schluessel = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);

        var letzteLaeufe = await db.Set<JobLauf>()
            .GroupBy(l => l.JobName)
            .Select(g => g.OrderByDescending(l => l.GestartetAm).First())
            .ToDictionaryAsync(l => l.JobName, cancellationToken);

        var ergebnis = new List<JobStatus>();
        foreach (var key in schluessel.OrderBy(k => k.Name, StringComparer.Ordinal))
        {
            var detail = await scheduler.GetJobDetail(key, cancellationToken);
            var trigger = await scheduler.GetTriggersOfJob(key, cancellationToken);
            var naechste = trigger.Select(t => t.GetNextFireTimeUtc()).Where(t => t.HasValue).Min();
            letzteLaeufe.TryGetValue(key.Name, out var lauf);

            ergebnis.Add(new JobStatus(key.Name, detail?.Description, naechste, lauf?.GestartetAm, lauf?.Erfolgreich, lauf?.Fehler));
        }

        return ergebnis;
    }
}
