using System.Globalization;
using System.Text.Json;
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

    /// <summary>Back up and clean up; returns the snapshot ID.</summary>
    public async Task<string> BackupAsync(BackupTarget target, CancellationToken cancellationToken)
    {
        var environment = target.ToEnvironment();
        var retention = target.Retention ?? settings.Retention;
        await EnsureRepositoryAsync(environment, create: true, cancellationToken);

        if (Directory.Exists(settings.WorkDir))
        {
            Directory.Delete(settings.WorkDir, recursive: true);
        }

        Directory.CreateDirectory(settings.WorkDir);
        try
        {
            BackupLog.DumpingDatabase(logger, settings.Database);
            // Uncompressed: restic deduplicates and compresses itself (unchanged tables hardly cost any space).
            await RunAsync("pg_dump", ["--format=custom", "--compress=0", $"--file={Path.Combine(settings.WorkDir, settings.Database + ".dump")}", settings.Database], null, cancellationToken);

            BackupLog.WritingBackup(logger);
            var result = await RunAsync("restic", ["backup", "--host", settings.Hostname, "--tag", BackupSettings.Tag, "--json", "--quiet", settings.WorkDir, settings.DataPath], environment, cancellationToken);
            var snapshot = ParseSnapshotId(result);

            BackupLog.Pruning(logger);
            await RunAsync("restic", ["forget", "--host", settings.Hostname, "--tag", BackupSettings.Tag, "--prune",
                "--keep-daily", Number(retention.Daily), "--keep-weekly", Number(retention.Weekly), "--keep-monthly", Number(retention.Monthly)], environment, cancellationToken);

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
        await EnsureRepositoryAsync(environment, create: false, cancellationToken);
        var result = await RunAsync("restic", ["snapshots", "--json", "--host", settings.Hostname, "--tag", BackupSettings.Tag], environment, cancellationToken);
        var snapshots = JsonSerializer.Deserialize<List<ResticSnapshot>>(JsonPart(result), Json) ?? [];
        return [.. snapshots.OrderByDescending(s => s.Time).Select(s => new SnapshotInfo(s.Id, s.ShortId ?? s.Id[..8], s.Time))];
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
    public async Task RestoreAsync(BackupTarget target, string snapshot, bool force, CancellationToken cancellationToken)
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
            BackupLog.FetchingSnapshot(logger, snapshot);
            await RunAsync("restic", ["restore", snapshot, "--host", settings.Hostname, "--tag", BackupSettings.Tag, "--target", settings.RestoreDir], environment, cancellationToken);
            var dump = settings.RestoreDir + Path.Combine(settings.WorkDir, settings.Database + ".dump");
            if (!File.Exists(dump))
            {
                throw new BackupException($"Die Sicherung enthält keinen Datenbank-Dump ({Path.Combine(settings.WorkDir, settings.Database + ".dump")}).");
            }

            BackupLog.RestoringDatabase(logger, settings.Database);
            await RunAsync("psql", ["-d", "postgres", "-v", "ON_ERROR_STOP=1", "-q", "-c", $"DROP DATABASE IF EXISTS \"{settings.Database}\" WITH (FORCE)", "-c", $"CREATE DATABASE \"{settings.Database}\""], null, cancellationToken);
            await RunAsync("pg_restore", ["--exit-on-error", "--no-owner", $"--dbname={settings.Database}", dump], null, cancellationToken);

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

    private async Task<string> RunAsync(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(file, arguments, environment, cancellationToken);
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

    private sealed record ResticSnapshot(string Id, string? ShortId, DateTimeOffset Time);
}
