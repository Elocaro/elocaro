using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;
using MietHahn.Application.Betrieb;
using MietHahn.Application.Konten;

namespace MietHahn.Web.Betrieb;

internal static class ExportEndpunkt
{
    public const string Pfad = "/verwaltung/system/datensicherung/export";

    /// <summary>Vollexport als ZIP-Download (POST mit Antiforgery-Token, damit kein Link den Export auslösen kann).</summary>
    public static IEndpointRouteBuilder MapExportEndpunkt(this IEndpointRouteBuilder endpunkte)
    {
        endpunkte.MapPost(Pfad, async (HttpContext kontext, IDatenexport export, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(kontext))
            {
                kontext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            kontext.Response.ContentType = "application/zip";
            kontext.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = export.Dateiname() }.ToString();
            kontext.Response.Headers.CacheControl = "no-store";
            await export.ExportierenAsync(kontext.Response.Body, kontext.RequestAborted);
        }).RequireAuthorization(Richtlinien.System);
        return endpunkte;
    }
}
