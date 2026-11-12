using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;

namespace Elocaro.Backup;

public sealed record TargetRequest(BackupTarget Target, string? TargetId = null);

public sealed record EnvironmentInfo(string Repository, string Time, int KeepDaily, int KeepWeekly, int KeepMonthly, bool Paused);

public sealed record ManagedRequest(bool Managed);

public sealed record EnvironmentRestoreRequest(string? Snapshot, bool Force = true);

public sealed record ExportRequest(BackupTarget Target, string Snapshot, string? TargetId = null);

public sealed record EnvironmentExportRequest(string Snapshot);

public sealed record UploadPasswordRequest(string Password);

public sealed record UploadRestoreRequest(string Password, bool Force = true);

public sealed record InspectResponse(RepositoryState State, string Message, IReadOnlyList<SnapshotInfo> Snapshots);

public sealed record DeleteSnapshotRequest(BackupTarget Target, string Snapshot, string? TargetId = null);

public sealed record EnvironmentDeleteRequest(string Snapshot);

public sealed record RestoreRequest(BackupTarget Target, string? Snapshot, bool Force = true, string? TargetId = null);

/// <summary>
/// Internal API for the Elocaro app (compose network only, Bearer token). Every call carries the target, the sidecar stores no
/// credentials. Long operations run in the background; <c>GET /status</c> reports the progress.
/// </summary>
internal static partial class BackupApi
{
    private static readonly string[] MissingTarget = ["Das Ziel fehlt."];
    private static readonly string[] InvalidTargetId = ["Die Kennung des Ziels ist ungültig."];

    public static void MapBackupApi(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok("ok"));

        var api = app.MapGroup(string.Empty).AddEndpointFilter(Authorize);

        api.MapGet("/status", (BackupCoordinator coordinator) => Results.Ok(coordinator.Status()));

        // The app takes over target and schedule (or gives them back): pauses the daily backup configured through the environment.
        api.MapPost("/managed", (ManagedRequest request, BackupSettings settings) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settings.ManagedMarker)!);
            if (request.Managed)
            {
                File.WriteAllText(settings.ManagedMarker, string.Empty);
            }
            else if (File.Exists(settings.ManagedMarker))
            {
                File.Delete(settings.ManagedMarker);
            }

            return Results.NoContent();
        });

        // The target configured through the environment (RESTIC_REPOSITORY in deploy/.env): the app shows it, lists its backups and restores from it.
        api.MapGet("/environment", (BackupSettings settings) => settings.EnvironmentTarget is { } target
            ? Results.Ok(new EnvironmentInfo(MaskCredentials(target.Repository), settings.BackupTime, settings.Retention.Daily, settings.Retention.Weekly, settings.Retention.Monthly, File.Exists(settings.ManagedMarker)))
            : Results.NotFound());

        api.MapPost("/environment/snapshots", async (BackupSettings settings, BackupEngine engine, CancellationToken cancellationToken) =>
        {
            if (settings.EnvironmentTarget is not { } target)
            {
                return Results.NotFound();
            }

            try
            {
                return Results.Ok(await engine.SnapshotsAsync(target, cancellationToken));
            }
            catch (BackupException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        api.MapPost("/environment/backup", (BackupSettings settings, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            settings.EnvironmentTarget is { } target ? Started(coordinator.TryStartBackup(target, lifetime.ApplicationStopping)) : Results.NotFound());

        api.MapPost("/environment/delete", (EnvironmentDeleteRequest request, BackupSettings settings, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            settings.EnvironmentTarget is { } target ? Started(coordinator.TryStartDelete(target, request.Snapshot, lifetime.ApplicationStopping)) : Results.NotFound());

        api.MapPost("/environment/restore", (EnvironmentRestoreRequest request, BackupSettings settings, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            settings.EnvironmentTarget is { } target ? Started(coordinator.TryStartRestore(target, request.Snapshot ?? "latest", request.Force, lifetime.ApplicationStopping)) : Results.NotFound());

        api.MapPost("/test", async (TargetRequest request, BackupEngine engine, CancellationToken cancellationToken) =>
            Invalid(request.Target) ?? Results.Ok(await engine.TestAsync(request.Target, cancellationToken)));

        api.MapPost("/snapshots", async (TargetRequest request, BackupEngine engine, CancellationToken cancellationToken) =>
        {
            if (Invalid(request.Target) is { } invalid)
            {
                return invalid;
            }

            try
            {
                return Results.Ok(await engine.SnapshotsAsync(request.Target, cancellationToken));
            }
            catch (BackupException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        api.MapPost("/backup", (TargetRequest request, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            Invalid(request.Target, request.TargetId) ?? Started(coordinator.TryStartBackup(request.Target, lifetime.ApplicationStopping, request.TargetId)));

        // A backup as a file: pack one for download (a restic repository with that one snapshot, as .tar), fetch it, or hand one in again.
        api.MapPost("/export", (ExportRequest request, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            Invalid(request.Target, request.TargetId) ?? Started(coordinator.TryStartExport(request.Target, request.Snapshot, lifetime.ApplicationStopping, request.TargetId)));

        api.MapPost("/environment/export", (EnvironmentExportRequest request, BackupSettings settings, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            settings.EnvironmentTarget is { } target ? Started(coordinator.TryStartExport(target, request.Snapshot, lifetime.ApplicationStopping)) : Results.NotFound());

        // The file is removed when it was sent (the stream deletes it when it closes): a backup file does not lie around. A download that
        // breaks off is prepared again.
        api.MapGet("/export/file", (BackupTransfers transfers) =>
            transfers.Export() is { } export
                ? Results.File(new FileStream(transfers.ExportFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose), "application/x-tar", export.Name)
                : Results.NotFound());

        api.MapDelete("/export", (BackupTransfers transfers) =>
        {
            transfers.DiscardExport();
            return Results.NoContent();
        });

        api.MapPut("/upload", async (HttpContext context, BackupTransfers transfers, BackupCoordinator coordinator, string? name, CancellationToken cancellationToken) =>
        {
            if (coordinator.Status().Running)
            {
                return Results.Conflict(new { error = "Es läuft bereits eine Sicherung oder Wiederherstellung." });
            }

            // The upload is as large as the backup
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = null;
            }

            try
            {
                return Results.Ok(await transfers.SaveUploadAsync(context.Request.Body, name ?? "sicherung.tar", cancellationToken));
            }
            catch (BackupException ex)
            {
                return Results.BadRequest(new { errors = new[] { ex.Message } });
            }
        });

        api.MapDelete("/upload", (BackupTransfers transfers, BackupCoordinator coordinator) =>
        {
            if (coordinator.Status().Running)
            {
                return Results.Conflict(new { error = "Es läuft bereits eine Sicherung oder Wiederherstellung." });
            }

            transfers.DiscardUpload();
            return Results.NoContent();
        });

        api.MapPost("/upload/inspect", async (UploadPasswordRequest request, BackupTransfers transfers, BackupEngine engine, CancellationToken cancellationToken) =>
        {
            if (transfers.Upload() is null)
            {
                return Results.NotFound();
            }

            var (check, snapshots) = await engine.InspectAsync(transfers.ImportDir, request.Password ?? string.Empty, cancellationToken);
            return Results.Ok(new InspectResponse(check.State, check.Message, snapshots));
        });

        api.MapPost("/upload/restore", (UploadRestoreRequest request, BackupTransfers transfers, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            transfers.Upload() is null
                ? Results.NotFound()
                : Started(coordinator.TryStartRestoreUpload(request.Password ?? string.Empty, request.Force, lifetime.ApplicationStopping)));

        api.MapPost("/delete", (DeleteSnapshotRequest request, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            Invalid(request.Target, request.TargetId) ?? Started(coordinator.TryStartDelete(request.Target, request.Snapshot, lifetime.ApplicationStopping, request.TargetId)));

        api.MapPost("/restore", (RestoreRequest request, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            Invalid(request.Target, request.TargetId) ?? Started(coordinator.TryStartRestore(request.Target, request.Snapshot ?? "latest", request.Force, lifetime.ApplicationStopping, request.TargetId)));
    }

    /// <summary>Repository without a password in the address (<c>rest:https://user:pass@host</c> becomes <c>rest:https://host</c>).</summary>
    internal static string MaskCredentials(string repository) => UserInfo().Replace(repository, "://");

    [GeneratedRegex("://[^/@]*@", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UserInfo();

    /// <summary>The ID names a status file: only what is safe in a file name.</summary>
    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafeId();

    private static IResult? Invalid(BackupTarget? target, string? targetId = null)
    {
        if (target is null)
        {
            return Results.BadRequest(new { errors = MissingTarget });
        }

        if (targetId is not null && !SafeId().IsMatch(targetId))
        {
            return Results.BadRequest(new { errors = InvalidTargetId });
        }

        var errors = target.Validate();
        return errors.Count == 0 ? null : Results.BadRequest(new { errors });
    }

    private static IResult Started(bool started) =>
        started ? Results.Accepted() : Results.Conflict(new { error = "Es läuft bereits eine Sicherung oder Wiederherstellung." });

    private static async ValueTask<object?> Authorize(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<BackupSettings>().ApiToken;
        if (expected.Length == 0)
        {
            return Results.Problem("BACKUP_API_TOKEN ist nicht gesetzt, die API ist abgeschaltet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        var given = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..] : string.Empty;
        // Compare hashes: constant time and independent of the length.
        var equal = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        return equal ? await next(context) : Results.Unauthorized();
    }
}
