using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;
using MietHahn.Application.Accounts;
using MietHahn.Application.Operations;

namespace MietHahn.Web.Operations;

internal static class ExportEndpoint
{
    public const string Path = "/verwaltung/system/datensicherung/export";

    /// <summary>Full export as a ZIP download (POST with antiforgery token so no link can trigger the export).</summary>
    public static IEndpointRouteBuilder MapExportEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, async (HttpContext context, IDataExport export, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            context.Response.ContentType = "application/zip";
            context.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = export.FileName() }.ToString();
            context.Response.Headers.CacheControl = "no-store";
            await export.ExportAsync(context.Response.Body, context.RequestAborted);
        }).RequireAuthorization(Policies.System);
        return endpoints;
    }
}
