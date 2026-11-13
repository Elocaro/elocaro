using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Operations;
using Elocaro.Application.Todos;
using Elocaro.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Elocaro.Infrastructure.Operations;

public sealed class UpdateCheckOptions
{
    public const string Section = "UpdateCheck";

    /// <summary>
    /// Ask GitHub twice a day which release is the newest (public API without sign-in; GitHub sees the IP address of the server).
    /// Switch off for instances without internet access or who do not want this call.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>List of published releases of the project (newest first).</summary>
    public Uri ReleasesUrl { get; set; } = new("https://api.github.com/repos/elocaro/elocaro/releases?per_page=15");

    /// <summary>The running version; set by the web application (null for development builds).</summary>
    public string? CurrentVersion { get; set; }
}

/// <summary>The last known releases, shared by the check (writes) and the display (reads).</summary>
internal sealed class UpdateState
{
    private volatile Snapshot snapshot = new([], null, false);

    public Snapshot Current => snapshot;

    public void Succeeded(IReadOnlyList<ReleaseNotes> releases, DateTimeOffset at) => snapshot = new(releases, at, false);

    public void Failed() => snapshot = snapshot with { Failed = true };

    public sealed record Snapshot(IReadOnlyList<ReleaseNotes> Releases, DateTimeOffset? CheckedAt, bool Failed);
}

internal sealed class UpdateInfoService(UpdateState state, IOptions<UpdateCheckOptions> options) : IUpdateInfo
{
    public Task<UpdateInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        var current = state.Current;
        var version = ReleaseVersion.TryParse(options.Value.CurrentVersion, out _) ? options.Value.CurrentVersion : null;
        return Task.FromResult(new UpdateInfo(version, options.Value.Enabled, current.CheckedAt, current.Failed, current.Releases));
    }
}

/// <summary>Fetches the published releases (version, date, release notes) from GitHub.</summary>
internal sealed partial class UpdateChecker(HttpClient http, UpdateState state, TimeProvider clock, IOptions<UpdateCheckOptions> options, ILogger<UpdateChecker> logger)
{
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.ReleasesUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            state.Succeeded(ReleaseList.Parse(json), clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogCheckFailed(logger, ex.Message);
            state.Failed();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Die Prüfung auf neue Versionen ist fehlgeschlagen: {Reason}")]
    private static partial void LogCheckFailed(ILogger logger, string reason);
}

/// <summary>Reads the answer of the GitHub releases API and the release notes (the Markdown of the changelog section).</summary>
internal static class ReleaseList
{
    /// <returns>Published releases (no drafts, no pre-releases) with a valid version, newest first.</returns>
    public static IReadOnlyList<ReleaseNotes> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseNotes>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (Flag(item, "draft") || Flag(item, "prerelease") || Text(item, "tag_name") is not { } tag || !ReleaseVersion.TryParse(tag, out var version))
            {
                continue;
            }

            DateOnly? date = DateTimeOffset.TryParse(Text(item, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
                ? DateOnly.FromDateTime(published.UtcDateTime)
                : null;
            releases.Add(new ReleaseNotes(version.ToString(3), date, Text(item, "html_url") ?? "", (Text(item, "body") ?? "").Trim()));
        }

        return [.. releases.OrderByDescending(r => Version.Parse(r.Version))];
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

[DisallowConcurrentExecution]
internal sealed class UpdateCheckJob(UpdateChecker checker) : IJob
{
    public static readonly JobKey Key = new("update-check", "system");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await checker.CheckAsync(cancellationToken);
    }
}

/// <summary>Points the accounts with the system permission to a newer release on the dashboard.</summary>
internal sealed class UpdateTodos(IUpdateInfo updates, IOptions<DemoOptions> demo) : ITodoSource
{
    public const string Link = "/verwaltung/system/neuigkeiten";

    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (demo.Value.Enabled || (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.System))))
        {
            return [];
        }

        var info = await updates.GetAsync(cancellationToken);
        return info.Latest is { } latest
            ? [new TodoItem("System", $"Neue Version {latest.Version} verfügbar", Link, Urgency.Info, $"Installiert ist {info.CurrentVersion}. Änderungen und Update-Befehl auf der Seite „Neuigkeiten“.")]
            : [];
    }
}
