using System.Security.Claims;
using Elocaro.Application.Notifications;

namespace Elocaro.Web.Push;

internal static class PushEndpoints
{
    /// <summary>
    /// Endpoints of the installed web app: public key, subscribe/unsubscribe, and the hints that the service worker fetches
    /// after a wake-up call. Open to every signed-in user; they only ever touch the own devices.
    /// </summary>
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/push").RequireAuthorization();
        group.MapGet("/schluessel", async (IPushNotifications push, HttpContext context) =>
            Results.Text(await push.PublicKeyAsync(context.RequestAborted), "text/plain"));
        group.MapPost("/abonnieren", async (PushSubscriptionInput input, ClaimsPrincipal user, HttpContext context, IPushNotifications push) =>
        {
            var result = await push.SubscribeAsync(UserOf(user), input, context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest(new { errors = result.Errors });
        });
        group.MapPost("/abmelden", async (EndpointInput input, ClaimsPrincipal user, HttpContext context, IPushNotifications push) =>
        {
            await push.UnsubscribeAsync(UserOf(user), input.Endpoint ?? "", context.RequestAborted);
            return Results.NoContent();
        });
        group.MapPost("/status", async (EndpointInput input, ClaimsPrincipal user, HttpContext context, IPushNotifications push) =>
            Results.Json(new { subscribed = await push.IsSubscribedAsync(UserOf(user), input.Endpoint ?? "", context.RequestAborted) }));
        group.MapGet("/ausstehend", async (ClaimsPrincipal user, HttpContext context, IPushNotifications push) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json((await push.TakePendingAsync(UserOf(user), context.RequestAborted)).Select(h => new { title = h.Title, body = h.Body, url = h.Url }));
        });
        return endpoints;
    }

    private static Guid UserOf(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    internal sealed record EndpointInput(string? Endpoint);
}
