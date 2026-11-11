using Elocaro.Application.Common;
using Elocaro.Domain.Operations;

namespace Elocaro.Application.Operations;

/// <summary>A saved backup target without secrets: whether a password or credential exists is shown, never its value.</summary>
public sealed record BackupSettingsView(
    Guid Id,
    string Name,
    BackupProvider Provider,
    string Location,
    bool Enabled,
    TimeOnly Time,
    int IntervalDays,
    int KeepDaily,
    int KeepWeekly,
    int KeepMonthly,
    bool HasPassword,
    IReadOnlySet<string> StoredCredentials);

/// <summary>Entry of the settings page. Empty secrets (password, credentials) keep the stored value.</summary>
public sealed class BackupSettingsInput
{
    public string Name { get; set; } = string.Empty;

    public BackupProvider Provider { get; set; }

    public string Location { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public TimeOnly Time { get; set; } = new(2, 30);

    /// <summary>Days between two backups: 1 = daily, 7 = weekly.</summary>
    public int IntervalDays { get; set; } = 1;

    public int KeepDaily { get; set; } = 7;

    public int KeepWeekly { get; set; } = 4;

    public int KeepMonthly { get; set; } = 12;

    public string? Password { get; set; }

    public Dictionary<string, string> Credentials { get; } = new(StringComparer.Ordinal);
}

/// <summary>Maintenance of the backup targets that the app manages itself (several are possible).</summary>
public interface IBackupSettings
{
    /// <summary>All targets, in the order they were created. Empty as long as the backup runs through the environment of the backup container.</summary>
    Task<IReadOnlyList<BackupSettingsView>> ListAsync(CancellationToken cancellationToken = default);

    Task<BackupSettingsView?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a target (<paramref name="id"/> empty) or changes one, and takes over the schedule from the backup container.</summary>
    Task<Result> SaveAsync(Guid? id, BackupSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>Removes a target from the app. The backups already at the target stay there. Without any target left the container's own schedule applies again.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
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

/// <param name="RunningTargetId">ID of the target the running operation works on.</param>
/// <param name="Results">The last backup per target ID (as <c>Guid.ToString("N")</c>).</param>
public sealed record BackupProgress(bool Available, bool Running, string? Kind, BackupOperationRow? Last, string? RunningTargetId = null, IReadOnlyDictionary<string, BackupOperationRow>? Results = null);

/// <summary>Remote control of the backup container.</summary>
public interface IBackupControl
{
    /// <summary>Running operation and the results of the last ones (backup or restore).</summary>
    Task<BackupProgress> ProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a target without changing anything. <paramref name="id"/> names the saved target whose secrets fill the empty ones in
    /// <paramref name="input"/> (<c>null</c> for a new one).
    /// </summary>
    Task<BackupTargetCheck> TestAsync(Guid? id, BackupSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>Starts a backup of a saved target (runs in the background; one at a time).</summary>
    Task<Result> StartBackupAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Existing backups at a saved target, newest first.</summary>
    Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists the backups of a target given directly (new installation: nothing is saved yet).</summary>
    Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(BackupSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces database and files with a backup of a saved target (runs in the background). The app is disconnected from the
    /// database during that time and must be restarted afterwards.
    /// </summary>
    Task<Result> StartRestoreAsync(Guid id, string snapshot, CancellationToken cancellationToken = default);

    /// <summary>Restore from a target given directly (new installation).</summary>
    Task<Result> StartRestoreAsync(BackupSettingsInput input, string snapshot, CancellationToken cancellationToken = default);
}
