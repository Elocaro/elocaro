using System.Text.RegularExpressions;

namespace Elocaro.Backup;

/// <summary>How many snapshots restic keeps per period (restic forget --keep-*).</summary>
public sealed record RetentionPolicy(int Daily = 7, int Weekly = 4, int Monthly = 12);

/// <summary>
/// Where and how to back up: restic repository, its password and the backend credentials. The sidecar stores nothing of it;
/// the app sends the target with every call.
/// </summary>
public sealed partial record BackupTarget(
    string Repository,
    string Password,
    Dictionary<string, string>? Environment = null,
    RetentionPolicy? Retention = null)
{
    /// <summary>Only backend credential variables may be passed on to the child processes (no PATH, LD_PRELOAD, PG*, …).</summary>
    [GeneratedRegex("^(AWS|AZURE|RCLONE|B2|GOOGLE|OS|ST)_[A-Z0-9_]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AllowedVariable();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Repository) || Repository.Contains('\n', StringComparison.Ordinal) || Repository.StartsWith('-'))
        {
            errors.Add("Repository fehlt oder ist ungültig.");
        }

        if (string.IsNullOrEmpty(Password))
        {
            errors.Add("Das Repository-Passwort fehlt.");
        }

        foreach (var (key, value) in Environment ?? [])
        {
            if (!AllowedVariable().IsMatch(key) || value.Contains('\0', StringComparison.Ordinal))
            {
                errors.Add($"Variable '{key}' ist nicht zulässig.");
            }
        }

        var retention = Retention ?? new RetentionPolicy();
        if (retention.Daily is < 0 or > 3650 || retention.Weekly is < 0 or > 520 || retention.Monthly is < 0 or > 240
            || retention.Daily + retention.Weekly + retention.Monthly == 0)
        {
            errors.Add("Die Aufbewahrung ist ungültig (mindestens eine Periode, sinnvolle Obergrenzen).");
        }

        return errors;
    }

    internal Dictionary<string, string> ToEnvironment()
    {
        var variables = new Dictionary<string, string>(Environment ?? [], StringComparer.Ordinal)
        {
            ["RESTIC_REPOSITORY"] = Repository,
            ["RESTIC_PASSWORD"] = Password,
        };
        return variables;
    }
}

public sealed record SnapshotInfo(string Id, string ShortId, DateTimeOffset Time);

public enum RepositoryState
{
    /// <summary>Repository exists and the password is correct.</summary>
    Ready,

    /// <summary>No repository there yet; the first backup creates it.</summary>
    New,

    WrongPassword,
    Unreachable,
}

public sealed record RepositoryCheck(RepositoryState State, string Message);

public enum OperationKind
{
    Backup,
    Restore,
}

public sealed record OperationResult(OperationKind Kind, bool Succeeded, string Message, string? Snapshot, DateTimeOffset FinishedAt, int DurationSeconds);

public sealed record OperationStatus(bool Running, OperationKind? Kind, DateTimeOffset? StartedAt, OperationResult? Last);

/// <summary>An external command failed; the message is the end of its output.</summary>
public sealed class BackupException(string message) : Exception(message);
