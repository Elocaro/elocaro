using Elocaro.Application.Accounts;
using Elocaro.Application.Finance;

namespace Elocaro.Web.Finance;

internal static class DunningPreviewEndpoint
{
    /// <summary>Preview PDF of a dunning proposal: renders only, stores and sends nothing.</summary>
    public static IEndpointRouteBuilder MapDunningPreviewEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/finanzen/mahnungen/{id:guid}/vorschau.pdf", async (Guid id, bool? gebuehr, bool? zinsen, IDunning dunning, HttpContext context) =>
        {
            try
            {
                var pdf = await dunning.PreviewAsync(id, new DunningIssueInput { IncludeFee = gebuehr ?? true, IncludeInterest = zinsen ?? true }, context.RequestAborted);
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'self'";
                context.Response.Headers.XFrameOptions = "SAMEORIGIN";
                context.Response.Headers.CacheControl = "private, no-store";
                return Results.File(pdf, "application/pdf");
            }
            catch (InvalidOperationException errors)
            {
                return Results.Problem(errors.Message, statusCode: StatusCodes.Status404NotFound);
            }
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }
}
