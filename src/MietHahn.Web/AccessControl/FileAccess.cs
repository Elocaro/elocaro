using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Accounts;
using MietHahn.Application.Files;
using MietHahn.Domain.Files;

namespace MietHahn.Web.AccessControl;

/// <summary>Anforderung „Datei lesen“ (ressourcenbasiert).</summary>
internal sealed class FileReadRequirement : IAuthorizationRequirement
{
    public static readonly FileReadRequirement Requirement = new();
}

/// <summary>
/// Verwaltung (mit Recht Stammdaten oder Finanzen) darf alle Dateien lesen; alle anderen nur, wenn ein Fachmodul
/// die Datei über <see cref="IFileShare"/> freigibt.
/// </summary>
internal sealed class FileAccessHandler(IEnumerable<IFileShare> shares) : AuthorizationHandler<FileReadRequirement, StoredFile>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FileReadRequirement requirement, StoredFile resource)
    {
        if (AccessControlExtensions.HasPermission(context.User, AccessRight.MasterData) || AccessControlExtensions.HasPermission(context.User, AccessRight.Finance))
        {
            context.Succeed(requirement);
            return;
        }

        foreach (var share in shares)
        {
            if (await share.IsSharedAsync(resource.Id, context.User))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}

internal static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Ohne Policy-Angabe gilt die Fallback-Richtlinie (angemeldet); die Datei selbst prüft der Handler.
        endpoints.MapGet("/dateien/{id:guid}", async (Guid id, bool? download, HttpContext context, IFileStore fileStore, IAuthorizationService authorization) =>
        {
            var opened = await fileStore.OpenAsync(id, context.RequestAborted);
            if (opened is null)
            {
                return Results.NotFound();
            }

            if (!(await authorization.AuthorizeAsync(context.User, opened.StoredFile, FileReadRequirement.Requirement)).Succeeded)
            {
                await opened.DisposeAsync();
                return Results.Forbid();
            }

            // Hochgeladene Inhalte nie als aktive Inhalte im Kontext der App ausführen.
            context.Response.Headers.XContentTypeOptions = "nosniff";
            // PDFs: der eingebaute PDF-Betrachter des Browsers läuft nicht in einer Sandbox, daher dort ohne „sandbox“;
            // Skripte bleiben über default-src 'none' gesperrt. Einbetten (Vorschau) nur in Seiten der App selbst.
            var sandbox = opened.StoredFile.MimeType == "application/pdf" ? "" : "; sandbox";
            context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'self'{sandbox}";
            context.Response.Headers.XFrameOptions = "SAMEORIGIN";
            context.Response.Headers.CacheControl = "private, no-store";

            return Results.File(
                opened.Content,
                opened.StoredFile.MimeType,
                fileDownloadName: download == true ? opened.StoredFile.Name : null,
                enableRangeProcessing: true);
        });
        return endpoints;
    }
}
