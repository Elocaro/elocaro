using MietHahn.Application.Datenschutz;
using MietHahn.Application.Konten;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Web.Personen;

internal static class AuskunftEndpunkt
{
    /// <summary>DSGVO-Auskunft (Art. 15) als JSON-Download; der Abruf wird im Audit-Log vermerkt.</summary>
    public static IEndpointRouteBuilder MapAuskunftEndpunkt(this IEndpointRouteBuilder endpunkte)
    {
        endpunkte.MapGet("/verwaltung/personen/{id:guid}/auskunft", async (Guid id, IDatenschutz datenschutz, TimeProvider uhr, HttpContext kontext) =>
        {
            var json = await datenschutz.AuskunftAsync(id, kontext.RequestAborted);
            if (json is null)
            {
                return Results.NotFound();
            }

            kontext.Response.Headers.CacheControl = "no-store";
            return Results.File(json, "application/json; charset=utf-8", $"dsgvo-auskunft-{id.ToString()[..8]}-{Zeitzone.Heute(uhr.GetUtcNow()):yyyy-MM-dd}.json");
        }).RequireAuthorization(Richtlinien.Stammdaten);
        return endpunkte;
    }
}
