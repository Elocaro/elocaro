using Elocaro.Application.Accounts;
using Elocaro.Application.Statements;

namespace Elocaro.Web.Finance;

internal static class StatementPreviewEndpoint
{
    /// <summary>Preview PDF of the letter to a tenancy: renders only, stores, books and sends nothing.</summary>
    public static IEndpointRouteBuilder MapStatementPreviewEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/abrechnungen/{id:guid}/mieter/{leaseId:guid}/vorschau.pdf", async (Guid id, Guid leaseId, IStatementDispatch dispatch, HttpContext context) =>
        {
            try
            {
                var pdf = await dispatch.PreviewAsync(id, leaseId, new DispatchInput(), context.RequestAborted);
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'self'";
                context.Response.Headers.XFrameOptions = "SAMEORIGIN";
                context.Response.Headers.CacheControl = "private, no-store";
                return Results.File(pdf, "application/pdf");
            }
            catch (Exception errors) when (errors is InvalidOperationException or Elocaro.Domain.Common.RuleViolationException || errors.GetType().Name == "NotFoundException")
            {
                return Results.Problem(errors.Message, statusCode: StatusCodes.Status404NotFound);
            }
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }
}
