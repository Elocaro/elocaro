using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Accounts;
using MietHahn.Application.Files;
using MietHahn.Domain.Files;

namespace MietHahn.Web.AccessControl;

/// <summary>Requirement "read file" (resource-based).</summary>
internal sealed class FileReadRequirement : IAuthorizationRequirement
{
    public static readonly FileReadRequirement Requirement = new();
}

/// <summary>
/// Administrators (with the master data or finance permission) may read all files; everyone else only if a feature module
/// shares the file via <see cref="IFileShare"/>.
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
        // Without a policy name the fallback policy (signed in) applies; the handler checks the file itself.
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

            // Never execute uploaded content as active content in the app context.
            context.Response.Headers.XContentTypeOptions = "nosniff";
            // PDFs: the browser's built-in PDF viewer does not run in a sandbox, so there is no "sandbox" here;
            // scripts stay blocked via default-src 'none'. Embedding (preview) only in pages of the app itself.
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
