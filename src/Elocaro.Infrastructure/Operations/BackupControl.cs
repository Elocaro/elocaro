using Elocaro.Application.Common;
using Elocaro.Application.Operations;

namespace Elocaro.Infrastructure.Operations;

internal sealed class BackupControl(BackupSidecar sidecar, IBackupTargets targets) : IBackupControl
{
    public async Task<BackupProgress> ProgressAsync(CancellationToken cancellationToken = default)
    {
        var status = await sidecar.StatusAsync(cancellationToken);
        if (status is null)
        {
            return new BackupProgress(false, false, null, null);
        }

        var last = status.Last is { } result ? new BackupOperationRow(result.Kind, result.Succeeded, result.Message, result.FinishedAt) : null;
        return new BackupProgress(true, status.Running, status.Kind, last);
    }

    public async Task<BackupTargetCheck> TestAsync(BackupSettingsInput input, CancellationToken cancellationToken = default)
    {
        var resolved = await targets.ResolveAsync(input, cancellationToken);
        return resolved.Target is { } target
            ? await sidecar.TestAsync(target, cancellationToken)
            : new BackupTargetCheck(BackupTargetState.Invalid, string.Join(' ', resolved.Errors));
    }

    public async Task<Result> StartBackupAsync(CancellationToken cancellationToken = default)
    {
        var resolved = await targets.ResolveAsync(null, cancellationToken);
        return resolved.Target is { } target ? await sidecar.StartBackupAsync(target, cancellationToken) : Result.Failed([.. resolved.Errors]);
    }

    public async Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(CancellationToken cancellationToken = default) =>
        await LoadSnapshotsAsync(null, cancellationToken);

    public async Task<IReadOnlyList<BackupSnapshotRow>> SnapshotsAsync(BackupSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return await LoadSnapshotsAsync(input, cancellationToken);
    }

    public async Task<Result> StartRestoreAsync(string snapshot, BackupSettingsInput? input = null, CancellationToken cancellationToken = default)
    {
        var resolved = await targets.ResolveAsync(input, cancellationToken);
        return resolved.Target is { } target ? await sidecar.StartRestoreAsync(target, snapshot, cancellationToken) : Result.Failed([.. resolved.Errors]);
    }

    private async Task<IReadOnlyList<BackupSnapshotRow>> LoadSnapshotsAsync(BackupSettingsInput? input, CancellationToken cancellationToken)
    {
        var resolved = await targets.ResolveAsync(input, cancellationToken);
        if (resolved.Target is not { } target)
        {
            return [];
        }

        var (rows, _) = await sidecar.SnapshotsAsync(target, cancellationToken);
        return rows;
    }
}
