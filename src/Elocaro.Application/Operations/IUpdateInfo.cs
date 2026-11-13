namespace Elocaro.Application.Operations;

/// <summary>Which version runs and whether a newer release is available (display only: Elocaro never updates itself).</summary>
public interface IUpdateInfo
{
    Task<UpdateInfo> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks GitHub for the published releases right away instead of waiting for the next scheduled check. Does nothing for a
    /// switched-off check, a development build, or if the last attempt was only a moment ago (spares the GitHub rate limit).
    /// </summary>
    Task<UpdateInfo> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <param name="Version">Without the leading "v", e.g. "0.2.0".</param>
/// <param name="Url">Page of the release on GitHub.</param>
/// <param name="Body">Release notes as Markdown (the section of the changelog); empty if there is none.</param>
public sealed record ReleaseNotes(string Version, DateOnly? Date, string Url, string Body);

/// <param name="CurrentVersion">The running version; null for development builds.</param>
/// <param name="Enabled">The check is switched on (<c>UpdateCheck__Enabled</c>).</param>
/// <param name="CheckedAt">When the releases were last fetched successfully; null if never.</param>
/// <param name="Failed">The last attempt failed (the last known releases are still shown).</param>
/// <param name="Releases">Published releases, newest first.</param>
public sealed record UpdateInfo(string? CurrentVersion, bool Enabled, DateTimeOffset? CheckedAt, bool Failed, IReadOnlyList<ReleaseNotes> Releases)
{
    /// <summary>Releases that are newer than the running version (empty for development builds).</summary>
    public IReadOnlyList<ReleaseNotes> Newer =>
        CurrentVersion is null ? [] : [.. Releases.Where(r => ReleaseVersion.IsNewer(r.Version, CurrentVersion))];

    public ReleaseNotes? Latest => Newer.Count > 0 ? Newer[0] : null;

    public bool UpdateAvailable => Latest is not null;

    /// <summary>The release notes of the running version, if it is a published release.</summary>
    public ReleaseNotes? Installed =>
        CurrentVersion is null ? null : Releases.FirstOrDefault(r => ReleaseVersion.Compare(r.Version, CurrentVersion) == 0);

    /// <summary>The command that updates a standard installation to <paramref name="version"/>.</summary>
    public static string UpdateCommand(string version) => $"deploy/update.sh {version}";
}

/// <summary>Comparison of release versions of the form X.Y.Z (a leading "v" and a pre-release suffix are ignored).</summary>
public static class ReleaseVersion
{
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    /// <summary>Negative, zero or positive like <see cref="IComparable{T}"/>; unparsable versions count as older.</summary>
    public static int Compare(string? left, string? right) => (TryParse(left, out var a), TryParse(right, out var b)) switch
    {
        (true, true) => a.CompareTo(b),
        (true, false) => 1,
        (false, true) => -1,
        _ => 0,
    };

    public static bool IsNewer(string? candidate, string? current) => TryParse(candidate, out _) && TryParse(current, out _) && Compare(candidate, current) > 0;
}
