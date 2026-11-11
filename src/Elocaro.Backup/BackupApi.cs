using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Elocaro.Backup;

public sealed record TargetRequest(BackupTarget Target, string? TargetId = null);

public sealed record ManagedRequest(bool Managed);

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

        api.MapPost("/restore", (RestoreRequest request, BackupCoordinator coordinator, IHostApplicationLifetime lifetime) =>
            Invalid(request.Target, request.TargetId) ?? Started(coordinator.TryStartRestore(request.Target, request.Snapshot ?? "latest", request.Force, lifetime.ApplicationStopping, request.TargetId)));
    }

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
