using System.Globalization;
using System.Text;
using MietHahn.Application.Finanzen;
using MietHahn.Application.Konten;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Web.Anzeige;

namespace MietHahn.Web.Finanzen;

internal static class OffenePostenEndpunkt
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Offene Posten als CSV (Semikolon, deutsche Zahlen, UTF-8 mit BOM für Excel).</summary>
    public static IEndpointRouteBuilder MapOffenePostenEndpunkt(this IEndpointRouteBuilder endpunkte)
    {
        endpunkte.MapGet("/verwaltung/finanzen/offene-posten.csv", async (IMieterkonten konten, TimeProvider uhr, HttpContext kontext) =>
        {
            var posten = await konten.OffenePostenAsync(kontext.RequestAborted);
            kontext.Response.Headers.CacheControl = "no-store";
            return Results.File(Csv(posten), "text/csv; charset=utf-8", $"offene-posten-{Zeitzone.Heute(uhr.GetUtcNow()):yyyy-MM-dd}.csv");
        }).RequireAuthorization(Richtlinien.Finanzen);
        return endpunkte;
    }

    internal static byte[] Csv(IEnumerable<OffenerPosten> posten)
    {
        var text = new StringBuilder();
        // Zeilenende nach RFC 4180 unabhängig vom Betriebssystem.
        text.Append("Mietobjekt;Mieter;Art;Forderung;Fällig;Betrag;Offen;Tage überfällig\r\n");
        foreach (var p in posten)
        {
            text.AppendJoin(';',
                Feld(p.Mietobjekt),
                Feld(p.Mieter),
                Feld(Bezeichnungen.Von(p.Art)),
                Feld(p.Text),
                p.Faellig.ToString("dd.MM.yyyy", Deutsch),
                p.Betrag.ToString("0.00", Deutsch),
                p.Offen.ToString("0.00", Deutsch),
                p.TageUeberfaellig.ToString(Deutsch)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>Quotiert bei Bedarf und entschärft Formeln (CSV-Injection).</summary>
    private static string Feld(string wert)
    {
        if (wert.Length > 0 && wert[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            wert = "'" + wert;
        }

        return wert.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + wert.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : wert;
    }
}
