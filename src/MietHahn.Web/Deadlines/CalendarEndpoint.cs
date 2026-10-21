using MietHahn.Application.Deadlines;

namespace MietHahn.Web.Deadlines;

internal static class CalendarEndpoint
{
    /// <summary>Calendar feed of the deadlines; anonymous, protected only by the secret token in the path.</summary>
    public static IEndpointRouteBuilder MapCalendarEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/kalender/{token}.ics", async (string token, IDeadlines deadlines, HttpContext context) =>
        {
            var feed = await deadlines.CalendarAsync(token, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return feed is null ? Results.NotFound() : Results.Text(feed, "text/calendar; charset=utf-8");
        }).AllowAnonymous();
        return endpoints;
    }
}
