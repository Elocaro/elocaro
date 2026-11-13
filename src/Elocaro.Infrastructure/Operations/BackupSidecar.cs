using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elocaro.Application.Common;
using Elocaro.Application.Operations;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Operations;

/// <summary>Client of the API of the backup container (<c>src/Elocaro.Backup</c>). Every call carries the target; the container stores nothing.</summary>
internal sealed class BackupSidecar(HttpClient http, IOptions<BackupOptions> options, IHttpClientFactory? clients = null)
{
    /// <summary>Name of the client without a time limit, for backup files in and out.</summary>
    internal const string TransferClient = "BackupSidecarTransfer";

    internal const string Unavailable = "Der Backup-Dienst ist nicht erreichbar. Läuft der Container „backup“ und stimmt BACKUP_API_TOKEN?";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Transfer => clients?.CreateClient(TransferClient) ?? http;

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
            return ([.. rows.Select(r => new BackupSnapshotRow(r.Id, r.ShortId, r.Time, r.Size))], null);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return ([], Unavailable);
        }
    }

    public async Task<BackupEnvironment?> EnvironmentAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return null;
        }

        try
        {
            return await http.GetFromJsonAsync<BackupEnvironment>(new Uri("environment", UriKind.Relative), Json, cancellationToken);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return null;
        }
    }

    public async Task<(IReadOnlyList<BackupSnapshotRow> Rows, string? Error)> EnvironmentSnapshotsAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return ([], Unavailable);
        }

        try
        {
            using var response = await http.PostAsync(new Uri("environment/snapshots", UriKind.Relative), null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ([], await Problem(response, cancellationToken));
            }

            var rows = await response.Content.ReadFromJsonAsync<List<WireSnapshot>>(Json, cancellationToken) ?? [];
            return ([.. rows.Select(r => new BackupSnapshotRow(r.Id, r.ShortId, r.Time, r.Size))], null);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return ([], Unavailable);
        }
    }

    public Task<Result> StartExportAsync(BackupRepositoryTarget target, string snapshot, string? targetId, CancellationToken cancellationToken) =>
        StartAsync("export", new { target, snapshot, targetId }, cancellationToken);

    public Task<Result> StartEnvironmentExportAsync(string snapshot, CancellationToken cancellationToken) =>
        StartAsync("environment/export", new { snapshot }, cancellationToken);

    /// <summary>The prepared download as a stream (the response stays open until the stream is disposed), or <c>null</c>.</summary>
    public async Task<BackupDownload?> OpenExportAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return null;
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await Transfer.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("export/file", UriKind.Relative)), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return null;
            }

            var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "elocaro-sicherung.tar";
            return new BackupDownload(await response.Content.ReadAsStreamAsync(cancellationToken), name, response.Content.Headers.ContentLength, response);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            response?.Dispose();
            return null;
        }
    }

    public async Task DiscardExportAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return;
        }

        try
        {
            using var response = await http.DeleteAsync(new Uri("export", UriKind.Relative), cancellationToken);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            // the file disappears by itself after a while
        }
    }

    public async Task<Result> UploadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return Result.Failed(Unavailable);
        }

        try
        {
            using var body = new StreamContent(content);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-tar");
            using var response = await Transfer.PutAsync(new Uri("upload?name=" + Uri.EscapeDataString(fileName), UriKind.Relative), body, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => Result.Ok(),
                HttpStatusCode.Conflict => Result.Failed("Es läuft bereits eine Sicherung oder Wiederherstellung."),
                HttpStatusCode.BadRequest => Result.Failed(await Problem(response, cancellationToken)),
                _ => Result.Failed(Unavailable),
            };
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return Result.Failed(Unavailable);
        }
    }

    public async Task<BackupUploadCheck> InspectUploadAsync(string password, CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return new BackupUploadCheck(false, Unavailable, []);
        }

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("upload/inspect", UriKind.Relative), new { password }, Json, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new BackupUploadCheck(false, "Es liegt keine hochgeladene Datei vor.", []);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new BackupUploadCheck(false, await Problem(response, cancellationToken), []);
            }

            var inspect = await response.Content.ReadFromJsonAsync<WireInspect>(Json, cancellationToken);
            return inspect is null
                ? new BackupUploadCheck(false, Unavailable, [])
                : new BackupUploadCheck(string.Equals(inspect.State, "Ready", StringComparison.OrdinalIgnoreCase), inspect.Message,
                    [.. inspect.Snapshots.Select(r => new BackupSnapshotRow(r.Id, r.ShortId, r.Time, r.Size))]);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            return new BackupUploadCheck(false, Unavailable, []);
        }
    }

    public Task<Result> StartRestoreUploadAsync(string password, CancellationToken cancellationToken) =>
        StartAsync("upload/restore", new { password, force = true }, cancellationToken);

    public async Task DiscardUploadAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return;
        }

        try
        {
            using var response = await http.DeleteAsync(new Uri("upload", UriKind.Relative), cancellationToken);
        }
        catch (Exception ex) when (IsConnectionProblem(ex, cancellationToken))
        {
            // nothing to do
        }
    }

    public Task<Result> StartDeleteAsync(BackupRepositoryTarget target, string snapshot, string? targetId, CancellationToken cancellationToken) =>
        StartAsync("delete", new { target, snapshot, targetId }, cancellationToken);

    public Task<Result> StartEnvironmentDeleteAsync(string snapshot, CancellationToken cancellationToken) =>
        StartAsync("environment/delete", new { snapshot }, cancellationToken);

    public Task<Result> StartEnvironmentBackupAsync(CancellationToken cancellationToken) =>
        StartAsync("environment/backup", new { }, cancellationToken);

    public Task<Result> StartEnvironmentRestoreAsync(string snapshot, CancellationToken cancellationToken) =>
        StartAsync("environment/restore", new { snapshot, force = true }, cancellationToken);

    /// <summary>Starts a backup or restore in the container (answer 202); it runs there in the background.</summary>
    /// <param name="targetId">Names the target in the container, which keeps one result and status file per ID.</param>
    public Task<Result> StartBackupAsync(BackupRepositoryTarget target, string? targetId, CancellationToken cancellationToken) =>
        StartAsync("backup", new { target, targetId }, cancellationToken);

    public Task<Result> StartRestoreAsync(BackupRepositoryTarget target, string snapshot, string? targetId, CancellationToken cancellationToken) =>
        StartAsync("restore", new { target, snapshot, force = true, targetId }, cancellationToken);

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

    internal sealed record WireStatus(bool Running, string? Kind, DateTimeOffset? StartedAt, WireResult? Last, string? TargetId = null, Dictionary<string, WireResult>? Results = null, WireProgress? Progress = null, WireFile? Export = null, WireFile? Upload = null);

    internal sealed record WireFile(string Name, long Size, DateTimeOffset Created, string? Snapshot);

    private sealed record WireInspect(string State, string Message, List<WireSnapshot> Snapshots);

    internal sealed record WireProgress(string Step, int StepNumber, int StepCount, double? Percent, string? Detail);

    internal sealed record WireResult(string Kind, bool Succeeded, string Message, string? Snapshot, DateTimeOffset FinishedAt, int DurationSeconds);

    private sealed record WireCheck(string State, string Message);

    private sealed record WireSnapshot(string Id, string ShortId, DateTimeOffset Time, long? Size = null);
}
