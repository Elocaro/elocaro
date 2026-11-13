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
    IReadOnlySet<string> StoredCredentials,
    DayOfWeek? Weekday = null);

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

    /// <summary>Day of the week of a weekly backup (ignored for a daily one).</summary>
    public DayOfWeek Weekday { get; set; } = DayOfWeek.Monday;

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

/// <param name="Size">Size of the backed-up data in bytes, if known (backups made by restic 0.17 and later).</param>
public sealed record BackupSnapshotRow(string Id, string ShortId, DateTimeOffset Time, long? Size = null);

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
/// <summary>Where a running backup or restore stands: step <c>StepNumber</c> of <c>StepCount</c>, and how far the whole operation is (0 to 1) if known.</summary>
public sealed record BackupRunProgress(string Step, int StepNumber, int StepCount, double? Percent, string? Detail);

/// <summary>A backup file that waits in the backup service: prepared for download, or uploaded and ready to be restored.</summary>
public sealed record BackupFileInfo(string Name, long Size, DateTimeOffset Created, string? Snapshot);

/// <summary>A backup file on its way to the browser. Dispose after sending.</summary>
public sealed class BackupDownload(Stream content, string fileName, long? length, IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public string FileName { get; } = fileName;

    public long? Length { get; } = length;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        owner?.Dispose();
    }
}

/// <summary>What an uploaded backup file holds, or why it cannot be read (wrong password).</summary>
public sealed record BackupUploadCheck(bool Succeeded, string Message, IReadOnlyList<BackupSnapshotRow> Snapshots);

public sealed record BackupProgress(bool Available, bool Running, string? Kind, BackupOperationRow? Last, string? RunningTargetId = null, BackupRunProgress? Detail = null, IReadOnlyDictionary<string, BackupOperationRow>? Results = null, BackupFileInfo? Export = null, BackupFileInfo? Upload = null);

/// <summary>The backup target that the container is configured with through its environment (<c>RESTIC_REPOSITORY</c> in <c>deploy/.env</c>).</summary>
/// <param name="Repository">Where the backups lie (without a password in the address).</param>
/// <param name="Time">Time of the daily backup (HH:mm).</param>
/// <param name="Paused">The app manages targets itself, so the schedule of the environment is paused.</param>
public sealed record BackupEnvironment(string Repository, string Time, int KeepDaily, int KeepWeekly, int KeepMonthly, bool Paused);

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

    /// <summary>Packs one backup of a saved target as a file for download (runs in the background; see <see cref="BackupProgress.Export"/>).</summary>
    Task<Result> StartExportAsync(Guid id, string snapshot, CancellationToken cancellationToken = default);

    /// <summary>Packs one backup at the environment target as a file for download.</summary>
    Task<Result> StartEnvironmentExportAsync(string snapshot, CancellationToken cancellationToken = default);

    /// <summary>The prepared download, or <c>null</c> if there is none. The call is entered in the audit log.</summary>
    Task<BackupDownload?> OpenExportAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the prepared download.</summary>
    Task DiscardExportAsync(CancellationToken cancellationToken = default);

    /// <summary>Hands a backup file (the download of a backup) to the backup service. Replaces an earlier upload.</summary>
    Task<Result> UploadAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Opens the uploaded file with the password of the backup and says what it holds.</summary>
    Task<BackupUploadCheck> InspectUploadAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>Restores the uploaded file (runs in the background; the app restarts afterwards).</summary>
    Task<Result> StartRestoreUploadAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>Drops the uploaded file.</summary>
    Task DiscardUploadAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes one backup of a saved target for good (runs in the background; the space it alone used is freed).</summary>
    Task<Result> StartDeleteSnapshotAsync(Guid id, string snapshot, CancellationToken cancellationToken = default);

    /// <summary>Removes one backup at the environment target for good.</summary>
    Task<Result> StartEnvironmentDeleteSnapshotAsync(string snapshot, CancellationToken cancellationToken = default);

    /// <summary>The target of the container environment, or <c>null</c> if there is none (or the container is unreachable).</summary>
    Task<BackupEnvironment?> EnvironmentAsync(CancellationToken cancellationToken = default);

    /// <summary>Existing backups at the environment target, newest first.</summary>
    Task<IReadOnlyList<BackupSnapshotRow>> EnvironmentSnapshotsAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a backup to the environment target now.</summary>
    Task<Result> StartEnvironmentBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Restores from a backup at the environment target (runs in the background, the app restarts afterwards).</summary>
    Task<Result> StartEnvironmentRestoreAsync(string snapshot, CancellationToken cancellationToken = default);
}
