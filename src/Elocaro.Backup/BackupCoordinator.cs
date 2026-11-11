using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Elocaro.Backup;

/// <summary>Runs one operation at a time in the background and remembers its result.</summary>
public sealed class BackupCoordinator(BackupEngine engine, BackupSettings settings, TimeProvider clock, ILogger<BackupCoordinator> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock gate = new();
    private readonly Dictionary<string, OperationResult> results = new(StringComparer.Ordinal);
    private OperationKind? runningKind;
    private string? runningTarget;
    private DateTimeOffset? startedAt;
    private OperationResult? last;

    /// <summary>The running operation (for tests and the scheduler, which waits for it).</summary>
    internal Task Running { get; private set; } = Task.CompletedTask;

    public OperationStatus Status()
    {
        lock (gate)
        {
            return new OperationStatus(runningKind is not null, runningKind, startedAt, last, runningTarget, new Dictionary<string, OperationResult>(results, StringComparer.Ordinal));
        }
    }

    /// <summary>Starts a backup; false if another operation is running.</summary>
    public bool TryStartBackup(BackupTarget target, CancellationToken stopping, string? targetId = null) =>
        TryStart(OperationKind.Backup, targetId, async token => await engine.BackupAsync(target, token), "Sicherung erfolgreich", stopping);

    /// <summary>Starts a restore; false if another operation is running.</summary>
    public bool TryStartRestore(BackupTarget target, string snapshot, bool force, CancellationToken stopping, string? targetId = null) =>
        TryStart(OperationKind.Restore, targetId, async token =>
        {
            await engine.RestoreAsync(target, snapshot, force, token);
            return null;
        }, "Wiederherstellung abgeschlossen", stopping);

    private bool TryStart(OperationKind kind, string? targetId, Func<CancellationToken, Task<string?>> work, string success, CancellationToken stopping)
    {
        lock (gate)
        {
            if (runningKind is not null)
            {
                return false;
            }

            runningKind = kind;
            runningTarget = targetId;
            startedAt = clock.GetUtcNow();
            Running = Task.Run(() => RunAsync(kind, targetId, work, success, startedAt.Value, stopping), CancellationToken.None);
            return true;
        }
    }

    private async Task RunAsync(OperationKind kind, string? targetId, Func<CancellationToken, Task<string?>> work, string success, DateTimeOffset started, CancellationToken stopping)
    {
        string? snapshot = null;
        var succeeded = false;
        string message;
        try
        {
            snapshot = await work(stopping);
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
