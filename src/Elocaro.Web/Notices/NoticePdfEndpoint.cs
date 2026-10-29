using Elocaro.Application.Accounts;
using Elocaro.Application.Notices;

namespace Elocaro.Web.Notices;

internal static class NoticePdfEndpoint
{
    /// <summary>Printable notice for the stairwell.</summary>
    public static IEndpointRouteBuilder MapNoticePdfEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/aushaenge/{id:guid}/aushang.pdf", async (Guid id, INotices notices, HttpContext context) =>
        {
            var pdf = await notices.PrintAsync(context.User, id, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return pdf is null ? Results.NotFound() : Results.File(pdf, "application/pdf", $"aushang-{id:N}.pdf");
        }).RequireAuthorization(Policies.MasterData);
        return endpoints;
    }
}
