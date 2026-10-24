using MietHahn.Application.Leases;

namespace MietHahn.Web.Signatures;

internal static class SignatureDocumentEndpoint
{
    /// <summary>The contract draft behind a signing link; anonymous, protected by the secret token.</summary>
    public static IEndpointRouteBuilder MapSignatureDocumentEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/unterschrift/{token}/dokument", async (string token, HttpContext context, IContractSignatures signatures) =>
        {
            if (await signatures.OpenDocumentAsync(token, context.RequestAborted) is not { } opened)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'self'";
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(opened.Content, "application/pdf", enableRangeProcessing: true);
        }).AllowAnonymous();
        return endpoints;
    }
}
