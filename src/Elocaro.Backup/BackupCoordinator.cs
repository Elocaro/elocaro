using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Elocaro.Backup;

/// <summary>Runs one operation at a time in the background and remembers its result.</summary>
public sealed class BackupCoordinator(BackupEngine engine, BackupSettings settings, BackupTransfers transfers, TimeProvider clock, ILogger<BackupCoordinator> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock gate = new();
    private readonly Dictionary<string, OperationResult> results = new(StringComparer.Ordinal);
    private OperationKind? runningKind;
    private string? runningTarget;
    private DateTimeOffset? startedAt;
    private OperationResult? last;
    private OperationProgress? progress;

    /// <summary>The running operation (for tests and the scheduler, which waits for it).</summary>
    internal Task Running { get; private set; } = Task.CompletedTask;

    public OperationStatus Status()
    {
        lock (gate)
        {
            return new OperationStatus(runningKind is not null, runningKind, startedAt, last, runningTarget, new Dictionary<string, OperationResult>(results, StringComparer.Ordinal), progress, transfers.Export(), transfers.Upload());
        }
    }

    /// <summary>Starts a backup; false if another operation is running.</summary>
    public bool TryStartBackup(BackupTarget target, CancellationToken stopping, string? targetId = null) =>
        TryStart(OperationKind.Backup, targetId, async (token, report) => await engine.BackupAsync(target, token, report), "Sicherung erfolgreich", stopping);

    /// <summary>Starts a restore; false if another operation is running.</summary>
    public bool TryStartRestore(BackupTarget target, string snapshot, bool force, CancellationToken stopping, string? targetId = null) =>
        TryStart(OperationKind.Restore, targetId, async (token, report) =>
        {
            await engine.RestoreAsync(target, snapshot, force, token, report);
            return null;
        }, "Wiederherstellung abgeschlossen", stopping);

    /// <summary>Starts packing one backup as a file for download; false if another operation is running.</summary>
    public bool TryStartExport(BackupTarget target, string snapshot, CancellationToken stopping, string? targetId = null) =>
        TryStart(OperationKind.Export, targetId, async (token, report) =>
        {
            transfers.DiscardExport();
            await engine.ExportAsync(target, snapshot, transfers.ExportFile, transfers.ExportWorkDir, token, report);
            transfers.SetExport($"elocaro-sicherung-{snapshot[..8]}.tar", snapshot);
            return null;
        }, "Die Sicherung liegt zum Herunterladen bereit", stopping);

    /// <summary>Starts the restore of the uploaded backup file; false if another operation is running.</summary>
    public bool TryStartRestoreUpload(string password, bool force, CancellationToken stopping) =>
        TryStart(OperationKind.Restore, null, async (token, report) =>
        {
            await engine.RestoreAsync(new BackupTarget(transfers.ImportDir, password, null), "latest", force, token, report);
            // Restored: the file is not needed any more (after a failure it stays, so that another attempt is possible)
            transfers.DiscardUpload();
            return null;
        }, "Wiederherstellung abgeschlossen", stopping);

    /// <summary>Starts removing one backup; false if another operation is running.</summary>
    public bool TryStartDelete(BackupTarget target, string snapshot, CancellationToken stopping, string? targetId = null) =>
        TryStart(OperationKind.Delete, targetId, async (token, _) =>
        {
            await engine.DeleteSnapshotAsync(target, snapshot, token);
            return null;
        }, "Sicherung gelöscht", stopping);

    private bool TryStart(OperationKind kind, string? targetId, Func<CancellationToken, Action<OperationProgress>, Task<string?>> work, string success, CancellationToken stopping)
    {
        lock (gate)
        {
            if (runningKind is not null)
            {
                return false;
            }

            runningKind = kind;
            progress = null;
            runningTarget = targetId;
            startedAt = clock.GetUtcNow();
            Running = Task.Run(() => RunAsync(kind, targetId, work, success, startedAt.Value, stopping), CancellationToken.None);
            return true;
        }
    }

    private async Task RunAsync(OperationKind kind, string? targetId, Func<CancellationToken, Action<OperationProgress>, Task<string?>> work, string success, DateTimeOffset started, CancellationToken stopping)
    {
        string? snapshot = null;
        var succeeded = false;
        string message;
        try
        {
            snapshot = await work(stopping, SetProgress);
            succeeded = true;
            message = success;
        }
        catch (BackupException ex)
        {
            message = ex.Message;
        }
        catch (OperationCanceledException)
        {
            message = "Abgebrochen (der Backup-Dienst wurde beendet).";
        }
#pragma warning disable CA1031 // The background task must never end unhandled; the message goes to the result.
        catch (Exception ex)
        {
            BackupLog.Crashed(logger, ex, kind);
            message = ex.Message;
        }
#pragma warning restore CA1031

        var finished = clock.GetUtcNow();
        var result = new OperationResult(kind, succeeded, message, snapshot, finished, (int)(finished - started).TotalSeconds, targetId);
        if (succeeded)
        {
            BackupLog.Succeeded(logger, kind, message);
        }
        else
        {
            BackupLog.Failed(logger, kind, message);
        }

        // The status file feeds the monitoring of the app (age of the last backup); a restore must not reset it.
        if (kind == OperationKind.Backup)
        {
            await WriteStatusFileAsync(result);
        }

        lock (gate)
        {
            last = result;
            if (kind == OperationKind.Backup)
            {
                results[targetId ?? "default"] = result;
            }

            runningTarget = null;
            runningKind = null;
            startedAt = null;
            progress = null;
        }
    }

    private void SetProgress(OperationProgress value)
    {
        lock (gate)
        {
            progress = value;
        }
    }

    /// <summary>One status file per target the app names (<c>backup-&lt;id&gt;.json</c>), the plain one for the target of the environment.</summary>
    private string StatusFileOf(string? targetId) =>
        targetId is null ? settings.StatusFile : Path.Combine(Path.GetDirectoryName(settings.StatusFile) ?? ".", $"backup-{targetId}.json");

    private async Task WriteStatusFileAsync(OperationResult result)
    {
        try
        {
            var file = StatusFileOf(result.TargetId);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var content = JsonSerializer.Serialize(new
            {
                timestamp = result.FinishedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                succeeded = result.Succeeded,
                message = result.Message,
                snapshot = result.Snapshot ?? string.Empty,
                durationSeconds = result.DurationSeconds,
            }, Json);
            var temporary = file + ".tmp";
            await File.WriteAllTextAsync(temporary, content);
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupLog.StatusFileFailed(logger, ex, settings.StatusFile);
        }
    }
}
