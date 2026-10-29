using Elocaro.Application.Accounts;
using Elocaro.Application.Tax;

namespace Elocaro.Web.Finance;

internal static class DatevEndpoints
{
    /// <summary>Download of a logged DATEV posting batch and the plain list of bookings.</summary>
    public static IEndpointRouteBuilder MapDatevEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/finanzen/datev/{id:guid}.csv", async (Guid id, IDatevExport datev, HttpContext context) =>
        {
            if (await datev.FileAsync(id, context.RequestAborted) is not { } file)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(file.Content, "text/csv", file.FileName);
        }).RequireAuthorization(Policies.Finance);

        endpoints.MapGet("/verwaltung/finanzen/datev/buchungen.csv", async (DateOnly von, DateOnly bis, IDatevExport datev, HttpContext context) =>
        {
            if (bis < von)
            {
                return Results.BadRequest();
            }

            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(await datev.BookingsCsvAsync(von, bis, context.RequestAborted), "text/csv; charset=utf-8", $"buchungen-{von:yyyyMMdd}-{bis:yyyyMMdd}.csv");
        }).RequireAuthorization(Policies.Finance);
        return endpoints;
    }
}
