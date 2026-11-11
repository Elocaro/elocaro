using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elocaro.Application.Common;
using Elocaro.Application.Operations;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Operations;

/// <summary>Client of the API of the backup container (<c>src/Elocaro.Backup</c>). Every call carries the target; the container stores nothing.</summary>
internal sealed class BackupSidecar(HttpClient http, IOptions<BackupOptions> options)
{
    internal const string Unavailable = "Der Backup-Dienst ist nicht erreichbar. Läuft der Container „backup“ und stimmt BACKUP_API_TOKEN?";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.Url) && !string.IsNullOrWhiteSpace(options.Value.ApiToken);

    public async Task<WireStatus?> StatusAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return null;
        }

        try
        {
            return await http.GetFromJsonAsync<WireStatus>(new Uri("status", UriKind.Relative), Json, cancellationToken);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return null;
        }
    }

    public async Task<BackupTargetCheck> TestAsync(BackupRepositoryTarget target, CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return new BackupTargetCheck(BackupTargetState.ServiceUnavailable, Unavailable);
        }

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("test", UriKind.Relative), new { target }, Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new BackupTargetCheck(BackupTargetState.ServiceUnavailable, await Problem(response, cancellationToken));
            }

            var check = await response.Content.ReadFromJsonAsync<WireCheck>(Json, cancellationToken);
            return check is null
                ? new BackupTargetCheck(BackupTargetState.ServiceUnavailable, Unavailable)
                : new BackupTargetCheck(Enum.Parse<BackupTargetState>(check.State, ignoreCase: true), check.Message);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return new BackupTargetCheck(BackupTargetState.ServiceUnavailable, Unavailable);
        }
    }

    public async Task<(IReadOnlyList<BackupSnapshotRow> Rows, string? Error)> SnapshotsAsync(BackupRepositoryTarget target, CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return ([], Unavailable);
        }

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("snapshots", UriKind.Relative), new { target }, Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ([], await Problem(response, cancellationToken));
            }

            var rows = await response.Content.ReadFromJsonAsync<List<WireSnapshot>>(Json, cancellationToken) ?? [];
            return ([.. rows.Select(r => new BackupSnapshotRow(r.Id, r.ShortId, r.Time))], null);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return ([], Unavailable);
        }
    }

    /// <summary>Starts a backup or restore in the container (answer 202); it runs there in the background.</summary>
    public Task<Result> StartBackupAsync(BackupRepositoryTarget target, CancellationToken cancellationToken) =>
        StartAsync("backup", new { target }, cancellationToken);

    public Task<Result> StartRestoreAsync(BackupRepositoryTarget target, string snapshot, CancellationToken cancellationToken) =>
        StartAsync("restore", new { target, snapshot, force = true }, cancellationToken);

    /// <summary>Takes over the schedule from the container (its daily backup from the environment pauses). Best effort.</summary>
    public async Task<bool> SetManagedAsync(bool managed, CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return false;
        }

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("managed", UriKind.Relative), new { managed }, Json, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return false;
        }
    }

    private async Task<Result> StartAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return Result.Failed(Unavailable);
        }

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Json, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Accepted => Result.Ok(),
                HttpStatusCode.Conflict => Result.Failed("Es läuft bereits eine Sicherung oder Wiederherstellung."),
                HttpStatusCode.BadRequest => Result.Failed("Das Ziel ist ungültig: " + await Problem(response, cancellationToken)),
                _ => Result.Failed(Unavailable),
            };
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return Result.Failed(Unavailable);
        }
    }

    private static bool IsConnectionProblem(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static async Task<string> Problem(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.ServiceUnavailable)
        {
            return Unavailable;
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join(' ', errors.EnumerateArray().Select(e => e.GetString()));
            }
        }
        catch (JsonException)
        {
            // not a JSON answer
        }

        return Unavailable;
    }

    internal sealed record WireStatus(bool Running, string? Kind, DateTimeOffset? StartedAt, WireResult? Last);

    internal sealed record WireResult(string Kind, bool Succeeded, string Message, string? Snapshot, DateTimeOffset FinishedAt, int DurationSeconds);

    private sealed record WireCheck(string State, string Message);

    private sealed record WireSnapshot(string Id, string ShortId, DateTimeOffset Time);
}
