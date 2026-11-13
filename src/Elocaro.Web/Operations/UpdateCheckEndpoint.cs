using Elocaro.Application.Accounts;
using Elocaro.Application.Operations;
using Microsoft.AspNetCore.Antiforgery;

namespace Elocaro.Web.Operations;

internal static class UpdateCheckEndpoint
{
    public const string PagePath = "/verwaltung/system/neuigkeiten";
    public const string Path = PagePath + "/pruefen";

    /// <summary>Checks for new versions right away. POST with antiforgery token, so no link can trigger it.</summary>
    public static IEndpointRouteBuilder MapUpdateCheckEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, async (HttpContext context, IUpdateInfo updates, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            await updates.RefreshAsync(context.RequestAborted);
            return Results.Redirect(PagePath);
        }).RequireAuthorization(Policies.System);
        return endpoints;
    }
}
