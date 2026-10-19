using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Dateien;
using MietHahn.Application.Konten;
using MietHahn.Domain.Dateien;

namespace MietHahn.Web.Autorisierung;

/// <summary>Anforderung „Datei lesen“ (ressourcenbasiert).</summary>
internal sealed class DateiLesen : IAuthorizationRequirement
{
    public static readonly DateiLesen Anforderung = new();
}

/// <summary>
/// Verwaltung (mit Recht Stammdaten oder Finanzen) darf alle Dateien lesen; alle anderen nur, wenn ein Fachmodul
/// die Datei über <see cref="IDateiFreigabe"/> freigibt.
/// </summary>
internal sealed class DateiZugriffHandler(IEnumerable<IDateiFreigabe> freigaben) : AuthorizationHandler<DateiLesen, Datei>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, DateiLesen requirement, Datei resource)
    {
        if (AutorisierungErweiterungen.HatRecht(context.User, Recht.Stammdaten) || AutorisierungErweiterungen.HatRecht(context.User, Recht.Finanzen))
        {
            context.Succeed(requirement);
            return;
        }

        foreach (var freigabe in freigaben)
        {
            if (await freigabe.IstFreigegebenAsync(resource.Id, context.User))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}

internal static class DateiEndpunkte
{
    public static IEndpointRouteBuilder MapDateiEndpunkte(this IEndpointRouteBuilder endpunkte)
    {
        // Ohne Policy-Angabe gilt die Fallback-Richtlinie (angemeldet); die Datei selbst prüft der Handler.
        endpunkte.MapGet("/dateien/{id:guid}", async (Guid id, bool? download, HttpContext kontext, IDateiablage ablage, IAuthorizationService autorisierung) =>
        {
            var geoeffnet = await ablage.OeffnenAsync(id, kontext.RequestAborted);
            if (geoeffnet is null)
            {
                return Results.NotFound();
            }

            if (!(await autorisierung.AuthorizeAsync(kontext.User, geoeffnet.Datei, DateiLesen.Anforderung)).Succeeded)
            {
                await geoeffnet.DisposeAsync();
                return Results.Forbid();
            }

            // Hochgeladene Inhalte nie als aktive Inhalte im Kontext der App ausführen.
            kontext.Response.Headers.XContentTypeOptions = "nosniff";
            // PDFs: der eingebaute PDF-Betrachter des Browsers läuft nicht in einer Sandbox, daher dort ohne „sandbox“;
            // Skripte bleiben über default-src 'none' gesperrt. Einbetten (Vorschau) nur in Seiten der App selbst.
            var sandbox = geoeffnet.Datei.MimeTyp == "application/pdf" ? "" : "; sandbox";
            kontext.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'self'{sandbox}";
            kontext.Response.Headers.XFrameOptions = "SAMEORIGIN";
            kontext.Response.Headers.CacheControl = "private, no-store";

            return Results.File(
                geoeffnet.Inhalt,
                geoeffnet.Datei.MimeTyp,
                fileDownloadName: download == true ? geoeffnet.Datei.Name : null,
                enableRangeProcessing: true);
        });
        return endpunkte;
    }
}
