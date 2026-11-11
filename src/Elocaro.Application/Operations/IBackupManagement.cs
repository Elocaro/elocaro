using Elocaro.Application.Common;
using Elocaro.Domain.Operations;

namespace Elocaro.Application.Operations;

/// <summary>Saved settings without secrets: whether a password or credential exists is shown, never its value.</summary>
public sealed record BackupSettingsView(
    BackupProvider Provider,
    string Location,
    bool Enabled,
    TimeOnly Time,
    int KeepDaily,
    int KeepWeekly,
    int KeepMonthly,
    bool HasPassword,
    IReadOnlySet<string> StoredCredentials);

/// <summary>Entry of the settings page. Empty secrets (password, credentials) keep the stored value.</summary>
public sealed class BackupSettingsInput
{
    public BackupProvider Provider { get; set; }

    public string Location { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public TimeOnly Time { get; set; } = new(2, 30);

    public int KeepDaily { get; set; } = 7;

    public int KeepWeekly { get; set; } = 4;

    public int KeepMonthly { get; set; } = 12;

    public string? Password { get; set; }

    public Dictionary<string, string> Credentials { get; } = new(StringComparer.Ordinal);
}

/// <summary>Maintenance of the backup settings that the app manages itself.</summary>
public interface IBackupSettings
{
    /// <summary><c>null</c> as long as the backup runs through the environment of the backup container.</summary>
    Task<BackupSettingsView?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the settings and takes over the schedule from the backup container.</summary>
    Task<Result> SaveAsync(BackupSettingsInput input, CancellationToken cancellationToken = default);
}

public sealed record BackupSnapshotRow(string Id, string ShortId, DateTimeOffset Time);

public enum BackupTargetState
{
    Ready,
    New,
    WrongPassword,
    Unreachable,

    /// <summary>Entries are missing or invalid (the message names them); nothing was tried.</summary>
    Invalid,

    /// <summary>The backup container itself does not answer.</summary>
    ServiceUnavailable,
}

public sealed record BackupTargetCheck(BackupTargetState State, string Message)
{
    public bool Succeeded => State is BackupTargetState.Ready or BackupTargetState.New;
}

public sealed record BackupOperationRow(string Kind, bool Succeeded, string Message, DateTimeOffset FinishedAt);

public sealed record BackupProgress(bool Available, bool Running, string? Kind, BackupOperationRow? Last);

/// <summary>Remote control of the backup container.</summary>
public interface IBackupControl
{
    /// <summary>Running operation and result of the last one (backup or restore).</summary>
    Task<BackupProgress> ProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks a target without changing anything; empty secrets in <paramref name="input"/> are taken from the saved settings.</summary>
    Task<BackupTargetCheck> TestAsync(BackupSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>Starts a backup with the saved settings (runs in the background).</summary>
    Task<Result> StartBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Existing backups at the saved target, newest first.</summary>
    Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the backups of a target given directly (new installation: nothing is saved yet).</summary>
    Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(BackupSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces database and files with a backup (runs in the background). Without <paramref name="input"/> the saved target is used.
    /// The app is disconnected from the database during that time and must be restarted afterwards.
    /// </summary>
    Task<Result> StartRestoreAsync(string snapshot, BackupSettingsInput? input = null, CancellationToken cancellationToken = default);
}
