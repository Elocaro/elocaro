using Elocaro.Application.Accounts;
using Elocaro.Application.Operations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Elocaro.Web.Operations;

/// <summary>
/// A backup as a file: the download of a prepared backup, and the upload of such a file (for the restore on this or on a new server).
/// Files are as big as the data, so the upload is read as a stream and never buffered; because of that the antiforgery token travels
/// as the first field of the form and is checked before the file is touched.
/// </summary>
internal static class BackupFileEndpoints
{
    public const string DownloadPath = "/verwaltung/system/datensicherung/download";
    public const string UploadPath = "/verwaltung/system/datensicherung/hochladen";
    public const string SetupUploadPath = "/konto/einrichtung/wiederherstellen/hochladen";

    private const string TokenField = "__RequestVerificationToken";
    private const string SetupCodeField = "setupCode";

    public static IEndpointRouteBuilder MapBackupFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DownloadPath, async (HttpContext context, IBackupControl control) =>
        {
            await using var download = await control.OpenExportAsync(context.RequestAborted);
            if (download is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "application/x-tar";
            context.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = download.FileName }.ToString();
            context.Response.Headers.CacheControl = "no-store";
            if (download.Length is { } length)
            {
                context.Response.ContentLength = length;
            }

            await download.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }).RequireAuthorization(Policies.System);

        endpoints.MapPost(UploadPath, (HttpContext context, IAntiforgery antiforgery, IOptions<AntiforgeryOptions> options, IBackupControl control) =>
            UploadAsync(context, antiforgery, options.Value, control, accounts: null, setupCode: null, "/verwaltung/system/datensicherung"))
            .RequireAuthorization(Policies.System);

        // Before the first account exists (restore on a new server): only with the setup code, like the creation of the first administrator.
        endpoints.MapPost(SetupUploadPath, (HttpContext context, IAntiforgery antiforgery, IOptions<AntiforgeryOptions> options, IBackupControl control, IAccountService accounts, ISetupCode setupCode) =>
            UploadAsync(context, antiforgery, options.Value, control, accounts, setupCode, "/konto/einrichtung/wiederherstellen"))
            .AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> UploadAsync(HttpContext context, IAntiforgery antiforgery, AntiforgeryOptions options, IBackupControl control, IAccountService? accounts, ISetupCode? setupCode, string back)
    {
        if (!context.Request.HasFormContentType || !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var mediaType)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 } boundary)
        {
            return Results.BadRequest();
        }

        // The upload is as big as the backup
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = null;
        }

        var reader = new MultipartReader(boundary, context.Request.Body);
        var tokenChecked = false;
        var codeGiven = string.Empty;
        while (await reader.ReadNextSectionAsync(context.RequestAborted) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                continue;
            }

            var field = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
            if (field == TokenField)
            {
                // The token is checked from the header: that does not read (and buffer) the form
                context.Request.Headers[options.HeaderName ?? "RequestVerificationToken"] = await new StreamReader(section.Body).ReadToEndAsync(context.RequestAborted);
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }

                tokenChecked = true;
            }
            else if (field == SetupCodeField)
            {
                codeGiven = await new StreamReader(section.Body).ReadToEndAsync(context.RequestAborted);
            }
            else if (disposition.FileName.HasValue || disposition.FileNameStar.HasValue)
            {
                if (!tokenChecked)
                {
                    return Results.BadRequest();
                }

                if (accounts is not null && setupCode is not null && (!setupCode.Matches(codeGiven) || !await accounts.InitialSetupPossibleAsync(context.RequestAborted)))
                {
                    return Results.Redirect(back + "?upload=code");
                }

                var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "sicherung.tar";
                var result = await control.UploadAsync(section.Body, name, context.RequestAborted);
                return Results.Redirect(back + "?upload=" + Code(result));
            }
        }

        return Results.Redirect(back + "?upload=leer");
    }

    /// <summary>The answer as a fixed word in the address (the page has the texts).</summary>
    private static string Code(Elocaro.Application.Common.Result result) =>
        result.Succeeded ? "ok"
        : result.Errors.Any(e => e.Contains("Sicherungsdatei", StringComparison.Ordinal)) ? "abgelehnt"
        : result.Errors.Any(e => e.Contains("läuft bereits", StringComparison.Ordinal)) ? "belegt"
        : "fehler";
}
