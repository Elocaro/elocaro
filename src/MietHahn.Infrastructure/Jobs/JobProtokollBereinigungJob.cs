using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Infrastructure.Persistenz;
using Quartz;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Löscht alte Einträge des Job-Laufprotokolls (täglich). Technische Daten, daher physisch gelöscht.</summary>
[DisallowConcurrentExecution]
internal sealed class JobProtokollBereinigungJob(MietHahnDbContext db, TimeProvider uhr, IOptions<JobsOptionen> optionen) : IJob
{
    public static readonly JobKey Schluessel = new("job-protokoll-bereinigung", "system");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cancellationToken = context.CancellationToken;
        var grenze = uhr.GetUtcNow().AddDays(-optionen.Value.ProtokollAufbewahrungTage);
        await db.Set<JobLauf>().IgnoreQueryFilters().Where(l => l.GestartetAm < grenze).ExecuteDeleteAsync(cancellationToken);
    }
}
