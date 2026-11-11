namespace Elocaro.Application.Operations;

/// <summary>
/// Result of the last backup. The backup container writes a status file after each run,
/// Elocaro reads it for the dashboard, health check and warning mails.
/// </summary>
public interface IBackupStatus
{
    /// <summary>The overall state: the worst of all enabled targets (or the one backup of the container environment).</summary>
    Task<BackupSnapshot> QueryAsync(CancellationToken cancellationToken = default);

    /// <summary>The state of every target managed in the app (empty without any).</summary>
    Task<IReadOnlyList<BackupTargetStatus>> QueryTargetsAsync(CancellationToken cancellationToken = default);
}

public sealed record BackupTargetStatus(Guid Id, string Name, bool Enabled, BackupSnapshot Status);

public enum BackupState
{
    /// <summary>No status file configured (e.g. local development).</summary>
    NotConfigured = 0,
    Ok = 1,
    Failed = 2,

    /// <summary>The last successful backup is older than allowed (backup container not running?).</summary>
    Outdated = 3,

    /// <summary>Status file missing or unreadable (never backed up yet?).</summary>
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
