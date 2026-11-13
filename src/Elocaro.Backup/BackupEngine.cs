using System.Formats.Tar;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Elocaro.Backup;

/// <summary>Back up and restore: pg_dump + file storage into a restic repository (port of the former elocaro-backup.sh).</summary>
public sealed partial class BackupEngine(IProcessRunner runner, BackupSettings settings, ILogger<BackupEngine> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DatabaseName();

    [GeneratedRegex("^(latest|[0-9a-f]{8,64})$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnapshotId();

    // How much of the whole backup each step is worth (percent): the transfer of the files takes the longest.
    private static readonly (string Name, int Weight)[] BackupSteps =
    [
        ("Repository prüfen", 5), ("Datenbank sichern", 15), ("Dateien übertragen", 60), ("Alte Sicherungen aufräumen", 10), ("Sicherung prüfen", 10),
    ];

    private static readonly (string Name, int Weight)[] RestoreSteps =
    [
        ("Repository prüfen", 5), ("Stand herunterladen", 45), ("Datenbank ersetzen", 30), ("Dateien ersetzen", 20),
    ];

    /// <summary>Reports the start of a step (and how far inside it, 0 to 1) as progress of the whole operation.</summary>
    private static void Report(Action<OperationProgress>? report, (string Name, int Weight)[] steps, int index, double within = 0, string? detail = null)
    {
        if (report is null)
        {
            return;
        }

        var before = steps.Take(index).Sum(s => s.Weight);
        var total = steps.Sum(s => s.Weight);
        report(new OperationProgress(steps[index].Name, index + 1, steps.Length, (before + (steps[index].Weight * Math.Clamp(within, 0, 1))) / total, detail));
    }

    /// <summary>Back up and clean up; returns the snapshot ID.</summary>
    public async Task<string> BackupAsync(BackupTarget target, CancellationToken cancellationToken, Action<OperationProgress>? report = null)
    {
        var environment = target.ToEnvironment();
        var retention = target.Retention ?? settings.Retention;
        Report(report, BackupSteps, 0);
        await EnsureRepositoryAsync(environment, create: true, cancellationToken);

        if (Directory.Exists(settings.WorkDir))
        {
            Directory.Delete(settings.WorkDir, recursive: true);
        }

        Directory.CreateDirectory(settings.WorkDir);
        try
        {
            Report(report, BackupSteps, 1);
            BackupLog.DumpingDatabase(logger, settings.Database);
            // Uncompressed: restic deduplicates and compresses itself (unchanged tables hardly cost any space).
            await RunAsync("pg_dump", ["--format=custom", "--compress=0", $"--file={Path.Combine(settings.WorkDir, settings.Database + ".dump")}", settings.Database], null, cancellationToken);

            Report(report, BackupSteps, 2);
            BackupLog.WritingBackup(logger);
            // restic prints status lines (percent_done, files, bytes) as JSON; a few per second.
            var resticEnvironment = new Dictionary<string, string>(environment, StringComparer.Ordinal) { ["RESTIC_PROGRESS_FPS"] = "2" };
            var result = await RunAsync("restic", ["backup", "--host", settings.Hostname, "--tag", BackupSettings.Tag, "--json", settings.WorkDir, settings.DataPath], resticEnvironment, cancellationToken,
                line => ReportTransfer(report, line));
            var snapshot = ParseSnapshotId(result);

            Report(report, BackupSteps, 3);
            BackupLog.Pruning(logger);
            await RunAsync("restic", ["forget", "--host", settings.Hostname, "--tag", BackupSettings.Tag, "--prune",
                "--keep-daily", Number(retention.Daily), "--keep-weekly", Number(retention.Weekly), "--keep-monthly", Number(retention.Monthly)], environment, cancellationToken);

            Report(report, BackupSteps, 4);
            BackupLog.Checking(logger);
            await RunAsync("restic", ["check"], environment, cancellationToken);
            return snapshot;
        }
        finally
        {
            if (Directory.Exists(settings.WorkDir))
            {
                Directory.Delete(settings.WorkDir, recursive: true);
            }
        }
    }

    public async Task<IReadOnlyList<SnapshotInfo>> SnapshotsAsync(BackupTarget target, CancellationToken cancellationToken)
    {
        var environment = target.ToEnvironment();
        // One call (the list is read often, on remote targets every call costs time): restic says by its exit code what is wrong
        var result = await runner.RunAsync("restic", ["snapshots", "--json", "--host", settings.Hostname, "--tag", BackupSettings.Tag], environment, cancellationToken);
        switch (result.ExitCode)
        {
            case 0:
                break;
            case 10:
                throw new BackupException("Am Ziel gibt es kein Repository.");
            case 12:
                throw new BackupException("Das Repository-Passwort stimmt nicht.");
            default:
                throw new BackupException(Tail(result.Output));
        }

        var snapshots = JsonSerializer.Deserialize<List<ResticSnapshot>>(JsonPart(result.Output), Json) ?? [];
        return [.. snapshots.OrderByDescending(s => s.Time).Select(s => new SnapshotInfo(s.Id, s.ShortId ?? s.Id[..8], s.Time, s.Summary?.TotalBytesProcessed))];
    }

    private static readonly (string Name, int Weight)[] ExportSteps =
    [
        ("Repository prüfen", 5), ("Sicherung kopieren", 70), ("Datei erstellen", 25),
    ];

    /// <summary>
    /// Packs one backup as a file: a new restic repository (same password, same chunker parameters) that holds only that snapshot,
    /// as a .tar. It stays encrypted; restoring from it needs the password of the backup.
    /// </summary>
    public async Task ExportAsync(BackupTarget target, string snapshot, string tarFile, string workDir, CancellationToken cancellationToken, Action<OperationProgress>? report = null)
    {
        if (!SnapshotId().IsMatch(snapshot) || snapshot == "latest")
        {
            throw new BackupException($"'{snapshot}' ist keine gültige Sicherungs-ID.");
        }

        var source = target.ToEnvironment();
        Report(report, ExportSteps, 0);
        await EnsureRepositoryAsync(source, create: false, cancellationToken);

        if (Directory.Exists(workDir))
        {
            Directory.Delete(workDir, recursive: true);
        }

        var repository = Path.Combine(workDir, "repo");
        Directory.CreateDirectory(repository);
        try
        {
            var environment = new Dictionary<string, string>(source, StringComparer.Ordinal)
            {
                ["RESTIC_REPOSITORY"] = repository,
                ["RESTIC_PASSWORD"] = target.Password,
                ["RESTIC_FROM_REPOSITORY"] = source["RESTIC_REPOSITORY"],
                ["RESTIC_FROM_PASSWORD"] = source["RESTIC_PASSWORD"],
            };
            Report(report, ExportSteps, 1);
            await RunAsync("restic", ["init", "--copy-chunker-params"], environment, cancellationToken);
            await RunAsync("restic", ["copy", snapshot], environment, cancellationToken);

            Report(report, ExportSteps, 2);
            Directory.CreateDirectory(Path.GetDirectoryName(tarFile)!);
            var temporary = tarFile + ".tmp";
            await TarFile.CreateFromDirectoryAsync(repository, temporary, includeBaseDirectory: false, cancellationToken);
            File.Move(temporary, tarFile, overwrite: true);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    /// <summary>The backups in an uploaded backup file (unpacked into <paramref name="repository"/>), or why they cannot be read.</summary>
    public async Task<(RepositoryCheck Check, IReadOnlyList<SnapshotInfo> Snapshots)> InspectAsync(string repository, string password, CancellationToken cancellationToken)
    {
        var target = new BackupTarget(repository, password, null);
        var check = await TestAsync(target, cancellationToken);
        if (check.State != RepositoryState.Ready)
        {
            return (check.State == RepositoryState.New ? new RepositoryCheck(RepositoryState.Unreachable, "Die Datei enthält keine Sicherung.") : check, []);
        }

        var snapshots = await SnapshotsAsync(target, cancellationToken);
        return snapshots.Count == 0
            ? (new RepositoryCheck(RepositoryState.Unreachable, "Die Datei enthält keine Sicherung."), snapshots)
            : (check, snapshots);
    }

    /// <summary>Removes one backup from the repository and frees the space only it used (restic forget --prune).</summary>
    public async Task DeleteSnapshotAsync(BackupTarget target, string snapshot, CancellationToken cancellationToken)
    {
        // "latest" is a valid restore target but too vague for something that cannot be undone
        if (!SnapshotId().IsMatch(snapshot) || snapshot == "latest")
        {
            throw new BackupException($"'{snapshot}' ist keine gültige Sicherungs-ID.");
        }

        var environment = target.ToEnvironment();
        await EnsureRepositoryAsync(environment, create: false, cancellationToken);
        await RunAsync("restic", ["forget", "--prune", snapshot], environment, cancellationToken);
    }

    /// <summary>Checks the repository without changing anything.</summary>
    public async Task<RepositoryCheck> TestAsync(BackupTarget target, CancellationToken cancellationToken)
    {
        // restic exit codes: 10 = repository does not exist, 12 = wrong password
        var result = await runner.RunAsync("restic", ["cat", "config"], target.ToEnvironment(), cancellationToken);
        return result.ExitCode switch
        {
            0 => new RepositoryCheck(RepositoryState.Ready, "Das Repository ist erreichbar, das Passwort stimmt."),
            10 => new RepositoryCheck(RepositoryState.New, "Am Ziel gibt es noch kein Repository. Es wird bei der ersten Sicherung angelegt."),
            12 => new RepositoryCheck(RepositoryState.WrongPassword, "Das Repository-Passwort stimmt nicht."),
            _ => new RepositoryCheck(RepositoryState.Unreachable, Tail(result.Output)),
        };
    }

    public async Task VerifyAsync(BackupTarget target, string subset, CancellationToken cancellationToken)
    {
        var environment = target.ToEnvironment();
        await EnsureRepositoryAsync(environment, create: false, cancellationToken);
        await RunAsync("restic", ["check", $"--read-data-subset={subset}"], environment, cancellationToken);
    }

    /// <summary>
    /// Replaces database and file storage with the state of a snapshot. Without <paramref name="force"/> the database must not be in
    /// use. With it, open connections are cut (the app itself asks for it and restarts afterwards).
    /// </summary>
    public async Task RestoreAsync(BackupTarget target, string snapshot, bool force, CancellationToken cancellationToken, Action<OperationProgress>? report = null)
    {
        if (!SnapshotId().IsMatch(snapshot))
        {
            throw new BackupException($"'{snapshot}' ist keine gültige Sicherungs-ID.");
        }

        if (!DatabaseName().IsMatch(settings.Database))
        {
            throw new BackupException("Der Datenbankname ist ungültig.");
        }

        var environment = target.ToEnvironment();
        Report(report, RestoreSteps, 0);
        await EnsureRepositoryAsync(environment, create: false, cancellationToken);

        if (!force)
        {
            var connected = (await RunAsync("psql", ["-d", "postgres", "-tAc", $"SELECT count(*) FROM pg_stat_activity WHERE datname = '{settings.Database}' AND pid <> pg_backend_pid()"], null, cancellationToken)).Trim();
            if (int.TryParse(connected, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0)
            {
                throw new BackupException($"{count} Verbindung(en) zur Datenbank {settings.Database} offen. Erst die App stoppen: docker compose … stop app");
            }
        }

        if (Directory.Exists(settings.RestoreDir))
        {
            Directory.Delete(settings.RestoreDir, recursive: true);
        }

        try
        {
            Report(report, RestoreSteps, 1);
            BackupLog.FetchingSnapshot(logger, snapshot);
            await RunAsync("restic", ["restore", snapshot, "--host", settings.Hostname, "--tag", BackupSettings.Tag, "--target", settings.RestoreDir], environment, cancellationToken);
            var dump = settings.RestoreDir + Path.Combine(settings.WorkDir, settings.Database + ".dump");
            if (!File.Exists(dump))
            {
                throw new BackupException($"Die Sicherung enthält keinen Datenbank-Dump ({Path.Combine(settings.WorkDir, settings.Database + ".dump")}).");
            }

            Report(report, RestoreSteps, 2);
            BackupLog.RestoringDatabase(logger, settings.Database);
            await RunAsync("psql", ["-d", "postgres", "-v", "ON_ERROR_STOP=1", "-q", "-c", $"DROP DATABASE IF EXISTS \"{settings.Database}\" WITH (FORCE)", "-c", $"CREATE DATABASE \"{settings.Database}\""], null, cancellationToken);
            await RunAsync("pg_restore", ["--exit-on-error", "--no-owner", $"--dbname={settings.Database}", dump], null, cancellationToken);

            Report(report, RestoreSteps, 3);
            BackupLog.ReplacingFiles(logger, settings.DataPath);
            Directory.CreateDirectory(settings.DataPath);
            foreach (var entry in new DirectoryInfo(settings.DataPath).EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo directory)
                {
                    directory.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            }

            var restoredFiles = settings.RestoreDir + settings.DataPath;
            if (Directory.Exists(restoredFiles))
            {
                await RunAsync("cp", ["-a", restoredFiles + "/.", settings.DataPath + "/"], null, cancellationToken);
            }

            var owner = settings.AppUid.ToString(CultureInfo.InvariantCulture);
            await RunAsync("chown", ["-R", $"{owner}:{owner}", settings.DataPath], null, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(settings.RestoreDir))
            {
                Directory.Delete(settings.RestoreDir, recursive: true);
            }
        }
    }

    private async Task EnsureRepositoryAsync(IReadOnlyDictionary<string, string> environment, bool create, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("restic", ["cat", "config"], environment, cancellationToken);
        switch (result.ExitCode)
        {
            case 0:
                return;
            case 10 when create:
                BackupLog.CreatingRepository(logger);
                await RunAsync("restic", ["init"], environment, cancellationToken);
                return;
            case 10:
                throw new BackupException("Am Ziel gibt es kein Repository.");
            case 12:
                throw new BackupException("Das Repository-Passwort stimmt nicht.");
            default:
                throw new BackupException(Tail(result.Output));
        }
    }

    /// <summary>A status line of <c>restic backup --json</c> becomes progress inside the transfer step.</summary>
    private static void ReportTransfer(Action<OperationProgress>? report, string line)
    {
        if (report is null || !line.StartsWith('{'))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("message_type", out var type) && type.GetString() == "status" && root.TryGetProperty("percent_done", out var percent))
            {
                var detail = root.TryGetProperty("total_bytes", out var totalBytes) && root.TryGetProperty("bytes_done", out var doneBytes) && totalBytes.GetInt64() > 0
                    ? $"{Size(doneBytes.GetInt64())} von {Size(totalBytes.GetInt64())}"
                    : root.TryGetProperty("total_files", out var total) && root.TryGetProperty("files_done", out var done)
                        ? string.Create(CultureInfo.InvariantCulture, $"{done.GetInt64()} von {total.GetInt64()} Dateien")
                        : null;
                Report(report, BackupSteps, 2, percent.GetDouble(), detail);
            }
        }
        catch (JsonException)
        {
            // not a status line
        }
    }

    // The container runs without ICU (invariant globalization): no named cultures; the decimal comma is set by hand.
    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.0} GB").Replace('.', ','),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB"),
    };

    private async Task<string> RunAsync(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken, Action<string>? onOutput = null)
    {
        var result = await runner.RunAsync(file, arguments, environment, cancellationToken, onOutput);
        if (result.ExitCode != 0)
        {
            throw new BackupException(Tail(result.Output));
        }

        return result.Output;
    }

    private static string ParseSnapshotId(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("snapshot_id", out var id) && id.GetString() is { Length: > 0 } value)
                {
                    return value;
                }
            }
            catch (JsonException)
            {
                // not a JSON line (warning of restic)
            }
        }

        throw new BackupException("restic hat keine Sicherungs-ID gemeldet.");
    }

    /// <summary>The JSON array of <c>restic snapshots --json</c>; warnings before it are skipped.</summary>
    private static string JsonPart(string output)
    {
        var start = output.IndexOf('[', StringComparison.Ordinal);
        return start < 0 ? "[]" : output[start..];
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The last lines of an output as an error message.</summary>
    internal static string Tail(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = string.Join(' ', lines.TakeLast(3));
        return text.Length > 500 ? text[..500] : text;
    }

    private sealed record ResticSnapshot(string Id, string? ShortId, DateTimeOffset Time, ResticSummary? Summary = null);

    private sealed record ResticSummary([property: JsonPropertyName("total_bytes_processed")] long? TotalBytesProcessed);
}
