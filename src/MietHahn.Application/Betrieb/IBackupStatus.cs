namespace MietHahn.Application.Betrieb;

/// <summary>
/// Ergebnis der letzten Sicherung. Der Backup-Container schreibt nach jedem Lauf eine Statusdatei,
/// MietHahn liest sie für Dashboard, Health-Check und Warnmails.
/// </summary>
public interface IBackupStatus
{
    Task<BackupZustand> AbfragenAsync(CancellationToken cancellationToken = default);
}

public enum BackupLage
{
    /// <summary>Keine Statusdatei konfiguriert (z. B. lokale Entwicklung).</summary>
    NichtEingerichtet = 0,
    Ok = 1,
    Fehlgeschlagen = 2,

    /// <summary>Die letzte erfolgreiche Sicherung ist älter als erlaubt (Backup-Container läuft nicht?).</summary>
    Veraltet = 3,

    /// <summary>Statusdatei fehlt oder ist unlesbar (noch nie gesichert?).</summary>
    Unbekannt = 4,
}

public sealed record BackupZustand(
    BackupLage Lage,
    string Beschreibung,
    DateTimeOffset? Zeitpunkt = null,
    string? Meldung = null,
    string? Snapshot = null,
    int? DauerSekunden = null)
{
    public bool BrauchtAufmerksamkeit => Lage is BackupLage.Fehlgeschlagen or BackupLage.Veraltet or BackupLage.Unbekannt;
}
