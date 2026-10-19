namespace MietHahn.Application.Operations;

/// <summary>
/// Ergebnis der letzten Sicherung. Der Backup-Container schreibt nach jedem Lauf eine Statusdatei,
/// MietHahn liest sie für Dashboard, Health-Check und Warnmails.
/// </summary>
public interface IBackupStatus
{
    Task<BackupSnapshot> QueryAsync(CancellationToken cancellationToken = default);
}

public enum BackupState
{
    /// <summary>Keine Statusdatei konfiguriert (z. B. lokale Entwicklung).</summary>
    NotConfigured = 0,
    Ok = 1,
    Failed = 2,

    /// <summary>Die letzte erfolgreiche Sicherung ist älter als erlaubt (Backup-Container läuft nicht?).</summary>
    Outdated = 3,

    /// <summary>Statusdatei fehlt oder ist unlesbar (noch nie gesichert?).</summary>
    Unknown = 4,
}

public sealed record BackupSnapshot(
    BackupState State,
    string Description,
    DateTimeOffset? Timestamp = null,
    string? Message = null,
    string? Snapshot = null,
    int? DurationSeconds = null)
{
    public bool NeedsAttention => State is BackupState.Failed or BackupState.Outdated or BackupState.Unknown;
}
