using Elocaro.Application.Accounts;
using Elocaro.Application.Statements;

namespace Elocaro.Web.Finance;

internal static class StatementReceiptEndpoint
{
    /// <summary>
    /// A file of a receipt that the landlord released for a statement. <c>ITenantStatements</c> checks on every request that the dispatch
    /// belongs to the signed-in tenant, that it was sent, that the receipt is released and that the file belongs to the receipt; all
    /// other cases answer 404 (nothing is revealed about the existence of foreign data).
    /// </summary>
    public static IEndpointRouteBuilder MapStatementReceiptEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/portal/abrechnungen/{dispatchId:guid}/belege/{receiptId:guid}/{fileId:guid}", async (Guid dispatchId, Guid receiptId, Guid fileId, bool? download, HttpContext context, ITenantStatements statements) =>
        {
            var opened = await statements.OpenReceiptFileAsync(context.User, dispatchId, receiptId, fileId, context.RequestAborted);
            if (opened is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            var sandbox = opened.StoredFile.MimeType == "application/pdf" ? "" : "; sandbox";
            context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'self'{sandbox}";
            context.Response.Headers.XFrameOptions = "SAMEORIGIN";
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(opened.Content, opened.StoredFile.MimeType, fileDownloadName: download == true ? opened.StoredFile.Name : null, enableRangeProcessing: true);
        }).RequireAuthorization(Policies.Portal);
        return endpoints;
    }
}
