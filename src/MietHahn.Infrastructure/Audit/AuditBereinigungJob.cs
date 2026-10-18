using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Infrastructure.Persistenz;
using Quartz;

namespace MietHahn.Infrastructure.Audit;

/// <summary>Löscht Audit-Einträge nach Ablauf der Aufbewahrungsfristen (Änderungen und Zugriffe getrennt).</summary>
[DisallowConcurrentExecution]
internal sealed class AuditBereinigungJob(AuditBereinigung bereinigung) : IJob
{
    public static readonly JobKey Schluessel = new("audit-bereinigung", "system");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await bereinigung.BereinigenAsync(context.CancellationToken);
    }
}

internal sealed class AuditBereinigung(MietHahnDbContext db, TimeProvider uhr, IOptions<AuditOptionen> optionen)
{
    public async Task<int> BereinigenAsync(CancellationToken cancellationToken = default)
    {
        var jetzt = uhr.GetUtcNow();
        var aenderungenGrenze = jetzt.AddYears(-optionen.Value.AufbewahrungJahre);
        var zugriffeGrenze = jetzt.AddMonths(-optionen.Value.ZugriffeAufbewahrungMonate);

        var aenderungen = await db.Set<AuditEintrag>()
            .Where(a => a.Aktion != AuditAktion.Gelesen && a.Zeitpunkt < aenderungenGrenze)
            .ExecuteDeleteAsync(cancellationToken);
        var zugriffe = await db.Set<AuditEintrag>()
            .Where(a => a.Aktion == AuditAktion.Gelesen && a.Zeitpunkt < zugriffeGrenze)
            .ExecuteDeleteAsync(cancellationToken);
        return aenderungen + zugriffe;
    }
}
