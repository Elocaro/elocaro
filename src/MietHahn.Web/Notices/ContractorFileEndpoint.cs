using MietHahn.Application.Files;
using MietHahn.Application.Tickets;

namespace MietHahn.Web.Notices;

internal static class ContractorFileEndpoint
{
    /// <summary>Photos and invoices of the one ticket behind a contractor link; anonymous, protected by the secret token.</summary>
    public static IEndpointRouteBuilder MapContractorFileEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auftrag/{token}/datei/{id:guid}", async (string token, Guid id, HttpContext context, IContractorLinks links, IFileStore fileStore) =>
        {
            if (await links.FileOfAsync(token, id, context.RequestAborted) is null || await fileStore.OpenAsync(id, context.RequestAborted) is not { } opened)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            var sandbox = opened.StoredFile.MimeType == "application/pdf" ? "" : "; sandbox";
            context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'self'{sandbox}";
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(opened.Content, opened.StoredFile.MimeType, enableRangeProcessing: true);
        }).AllowAnonymous();
        return endpoints;
    }
}
