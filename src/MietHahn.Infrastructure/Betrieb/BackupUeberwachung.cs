using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Betrieb;
using MietHahn.Application.Kommunikation;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;
using Quartz;

namespace MietHahn.Infrastructure.Betrieb;

/// <summary>Zeigt ein Problem mit der Sicherung im Dashboard (für Konten mit Systemrecht).</summary>
internal sealed class BackupAufgaben(IBackupStatus status) : IAufgabenQuelle
{
    public const string Link = "/verwaltung/system/datensicherung";

    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.System)))
        {
            return [];
        }

        var zustand = await status.AbfragenAsync(cancellationToken);
        return zustand.BrauchtAufmerksamkeit
            ? [new Aufgabe("System", "Datensicherung prüfen", Link, Dringlichkeit.Dringend, zustand.Beschreibung)]
            : [];
    }
}

/// <summary>Meldet „Degraded“, wenn die letzte Sicherung fehlgeschlagen oder zu alt ist (die App bleibt erreichbar).</summary>
internal sealed class BackupHealthCheck(IBackupStatus status) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var zustand = await status.AbfragenAsync(cancellationToken);
        return zustand.BrauchtAufmerksamkeit ? HealthCheckResult.Degraded(zustand.Beschreibung) : HealthCheckResult.Healthy(zustand.Beschreibung);
    }
}

/// <summary>Schickt allen aktiven Admins eine Warnung, solange die Sicherung nicht in Ordnung ist (einmal täglich).</summary>
internal sealed class BackupWarnung(
    IBackupStatus status,
    MietHahnDbContext db,
    IMailVersand mailVersand,
    IOptions<AnwendungOptionen> anwendung)
{
    /// <returns>Anzahl der benachrichtigten Admins.</returns>
    public async Task<int> PruefenAsync(CancellationToken cancellationToken = default)
    {
        var zustand = await status.AbfragenAsync(cancellationToken);
        if (!zustand.BrauchtAufmerksamkeit)
        {
            return 0;
        }

        var empfaenger = await AdminEmpfaenger.LadenAsync(db, cancellationToken);
        if (empfaenger.Count == 0)
        {
            return 0;
        }

        await mailVersand.EinreihenAsync(
            new MailAuftrag(
                empfaenger,
                "backup-warnung",
                new
                {
                    zustand.Beschreibung,
                    zustand.Meldung,
                    Link = anwendung.Value.Link(BackupAufgaben.Link).ToString(),
                },
                Bezug: "backup"),
            cancellationToken);
        return empfaenger.Count;
    }
}

[DisallowConcurrentExecution]
internal sealed class BackupWarnungJob(BackupWarnung warnung) : IJob
{
    public static readonly JobKey Schluessel = new("backup-warnung", "system");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await warnung.PruefenAsync(context.CancellationToken);
    }
}
