using Elocaro.Application.Common;
using Elocaro.Application.Operations;

namespace Elocaro.Infrastructure.Operations;

internal sealed class BackupControl(BackupSidecar sidecar, IBackupTargets targets) : IBackupControl
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
        return new BackupProgress(true, status.Running, status.Kind, status.Last is { } last ? Row(last) : null, status.TargetId, results);
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

    private static BackupOperationRow Row(BackupSidecar.WireResult result) => new(result.Kind, result.Succeeded, result.Message, result.FinishedAt);
}
