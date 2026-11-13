using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.Operations;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;

namespace Elocaro.Infrastructure.Operations;

internal sealed class BackupControl(BackupSidecar sidecar, IBackupTargets targets, ElocaroDbContext db, ICurrentUser user, TimeProvider clock) : IBackupControl
{
    /// <summary>The name under which the container knows a target (and keeps its result and status file).</summary>
    internal static string Key(Guid id) => id.ToString("N");

    public async Task<BackupProgress> ProgressAsync(CancellationToken cancellationToken = default)
    {
        var status = await sidecar.StatusAsync(cancellationToken);
        if (status is null)
        {
            return new BackupProgress(false, false, null, null);
        }

        var results = (status.Results ?? []).ToDictionary(r => r.Key, r => Row(r.Value), StringComparer.Ordinal);
        return new BackupProgress(true, status.Running, status.Kind, status.Last is { } last ? Row(last) : null, status.TargetId,
            status.Progress is { } p ? new BackupRunProgress(p.Step, p.StepNumber, p.StepCount, p.Percent, p.Detail) : null, results,
            status.Export is { } export ? File(export) : null, status.Upload is { } upload ? File(upload) : null);
    }

    public async Task<BackupTargetCheck> TestAsync(Guid? id, BackupSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var resolved = await targets.ResolveAsync(id, input, cancellationToken);
        return resolved.Target is { } target
            ? await sidecar.TestAsync(target, cancellationToken)
            : new BackupTargetCheck(BackupTargetState.Invalid, string.Join(' ', resolved.Errors));
    }

    public async Task<Result> StartBackupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var resolved = await targets.ResolveAsync(id, null, cancellationToken);
        return resolved.Target is { } target ? await sidecar.StartBackupAsync(target, Key(id), cancellationToken) : Result.Failed([.. resolved.Errors]);
    }

    public async Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await LoadSnapshotsAsync(id, null, cancellationToken);

    public async Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(BackupSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return await LoadSnapshotsAsync(null, input, cancellationToken);
    }

    public async Task<Result> StartRestoreAsync(Guid id, string snapshot, CancellationToken cancellationToken = default)
    {
        var resolved = await targets.ResolveAsync(id, null, cancellationToken);
        return resolved.Target is { } target ? await sidecar.StartRestoreAsync(target, snapshot, Key(id), cancellationToken) : Result.Failed([.. resolved.Errors]);
    }

    public async Task<Result> StartRestoreAsync(BackupSettingsInput input, string snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var resolved = await targets.ResolveAsync(null, input, cancellationToken);
        return resolved.Target is { } target ? await sidecar.StartRestoreAsync(target, snapshot, null, cancellationToken) : Result.Failed([.. resolved.Errors]);
    }

    public async Task<Result> StartExportAsync(Guid id, string snapshot, CancellationToken cancellationToken = default)
    {
        var resolved = await targets.ResolveAsync(id, null, cancellationToken);
        return resolved.Target is { } target ? await sidecar.StartExportAsync(target, snapshot, Key(id), cancellationToken) : Result.Failed([.. resolved.Errors]);
    }

    public Task<Result> StartEnvironmentExportAsync(string snapshot, CancellationToken cancellationToken = default) =>
        sidecar.StartEnvironmentExportAsync(snapshot, cancellationToken);

    public async Task<BackupDownload?> OpenExportAsync(CancellationToken cancellationToken = default)
    {
        var download = await sidecar.OpenExportAsync(cancellationToken);
        if (download is not null)
        {
            // Independent of Audit:LogAccess: a backup file holds all personal data (encrypted, but it leaves the server).
            db.Add(new AuditEntry(clock.GetUtcNow(), user.Identifier, "Sicherungsdatei", Guid.Empty, AuditAction.Read, "[]", "Sicherung als Datei heruntergeladen"));
            await db.SaveChangesAsync(cancellationToken);
        }

        return download;
    }

    public Task DiscardExportAsync(CancellationToken cancellationToken = default) => sidecar.DiscardExportAsync(cancellationToken);

    public Task<Result> UploadAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        sidecar.UploadAsync(content, fileName, cancellationToken);

    public Task<BackupUploadCheck> InspectUploadAsync(string password, CancellationToken cancellationToken = default) =>
        sidecar.InspectUploadAsync(password, cancellationToken);

    public Task<Result> StartRestoreUploadAsync(string password, CancellationToken cancellationToken = default) =>
        sidecar.StartRestoreUploadAsync(password, cancellationToken);

    public Task DiscardUploadAsync(CancellationToken cancellationToken = default) => sidecar.DiscardUploadAsync(cancellationToken);

    public async Task<Result> StartDeleteSnapshotAsync(Guid id, string snapshot, CancellationToken cancellationToken = default)
    {
        var resolved = await targets.ResolveAsync(id, null, cancellationToken);
        return resolved.Target is { } target ? await sidecar.StartDeleteAsync(target, snapshot, Key(id), cancellationToken) : Result.Failed([.. resolved.Errors]);
    }

    public Task<Result> StartEnvironmentDeleteSnapshotAsync(string snapshot, CancellationToken cancellationToken = default) =>
        sidecar.StartEnvironmentDeleteAsync(snapshot, cancellationToken);

    public Task<BackupEnvironment?> EnvironmentAsync(CancellationToken cancellationToken = default) => sidecar.EnvironmentAsync(cancellationToken);

    public async Task<IReadOnlyList<BackupSnapshotRow>> EnvironmentSnapshotsAsync(CancellationToken cancellationToken = default) =>
        (await sidecar.EnvironmentSnapshotsAsync(cancellationToken)).Rows;

    public Task<Result> StartEnvironmentBackupAsync(CancellationToken cancellationToken = default) => sidecar.StartEnvironmentBackupAsync(cancellationToken);

    public Task<Result> StartEnvironmentRestoreAsync(string snapshot, CancellationToken cancellationToken = default) =>
        sidecar.StartEnvironmentRestoreAsync(snapshot, cancellationToken);

    private async Task<IReadOnlyList<BackupSnapshotRow>> LoadSnapshotsAsync(Guid? id, BackupSettingsInput? input, CancellationToken cancellationToken)
    {
        var resolved = await targets.ResolveAsync(id, input, cancellationToken);
        if (resolved.Target is not { } target)
        {
            return [];
        }

        var (rows, _) = await sidecar.SnapshotsAsync(target, cancellationToken);
        return rows;
    }

    private static BackupFileInfo File(BackupSidecar.WireFile file) => new(file.Name, file.Size, file.Created, file.Snapshot);

    private static BackupOperationRow Row(BackupSidecar.WireResult result) => new(result.Kind, result.Succeeded, result.Message, result.FinishedAt);
}
