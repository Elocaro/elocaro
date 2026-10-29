using Elocaro.Application.Accounts;
using Elocaro.Application.Privacy;
using Elocaro.Domain.Common;

namespace Elocaro.Web.People;

internal static class AccessReportEndpoint
{
    /// <summary>GDPR access report (Art. 15) as a JSON download; the request is recorded in the audit log.</summary>
    public static IEndpointRouteBuilder MapAccessReportEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/personen/{id:guid}/auskunft", async (Guid id, IPrivacyService privacy, TimeProvider clock, HttpContext context) =>
        {
            var json = await privacy.AccessReportAsync(id, context.RequestAborted);
            if (json is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.File(json, "application/json; charset=utf-8", $"dsgvo-auskunft-{id.ToString()[..8]}-{GermanTime.Today(clock.GetUtcNow()):yyyy-MM-dd}.json");
        }).RequireAuthorization(Policies.MasterData);
        return endpoints;
    }
}
