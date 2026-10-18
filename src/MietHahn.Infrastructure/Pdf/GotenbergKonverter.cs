using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MietHahn.Infrastructure.Pdf;

/// <summary>Wandelt HTML über Gotenberg (Chromium) in PDF um.</summary>
internal sealed class GotenbergKonverter(HttpClient http, IOptions<PdfOptionen> optionen)
{
    // A4 in Zoll; Ränder links/rechts/oben steuert das CSS (@page), unten Platz für die Fußzeile (25 mm).
    private const string A4Breite = "8.27";
    private const string A4Hoehe = "11.69";
    private const decimal FusszeileZoll = 25m / 25.4m;

    public async Task<byte[]> HtmlZuPdfAsync(string html, string fusszeileHtml, PdfMetadaten metadaten, CancellationToken cancellationToken)
    {
        using var formular = new MultipartFormDataContent();
        Datei(formular, "index.html", html);
        Datei(formular, "footer.html", fusszeileHtml);
        Feld(formular, "paperWidth", A4Breite);
        Feld(formular, "paperHeight", A4Hoehe);
        Feld(formular, "preferCssPageSize", "true");
        Feld(formular, "marginTop", "0");
        Feld(formular, "marginLeft", "0");
        Feld(formular, "marginRight", "0");
        Feld(formular, "marginBottom", FusszeileZoll.ToString("0.00", CultureInfo.InvariantCulture));
        Feld(formular, "printBackground", "true");
        Feld(formular, "metadata", JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["Title"] = metadaten.Titel,
            ["Author"] = metadaten.Autor,
            ["Creator"] = "MietHahn",
        }));
        if (!string.IsNullOrWhiteSpace(optionen.Value.PdfA))
        {
            Feld(formular, "pdfa", optionen.Value.PdfA);
        }

        using var antwort = await http.PostAsync(new Uri("forms/chromium/convert/html", UriKind.Relative), formular, cancellationToken);
        if (!antwort.IsSuccessStatusCode)
        {
            var fehler = await antwort.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Gotenberg meldet {(int)antwort.StatusCode}: {fehler}");
        }

        return await antwort.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static void Datei(MultipartFormDataContent formular, string name, string inhalt)
    {
        var teil = new ByteArrayContent(Encoding.UTF8.GetBytes(inhalt));
        teil.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        formular.Add(teil, "files", name);
    }

    private static void Feld(MultipartFormDataContent formular, string name, string wert) => formular.Add(new StringContent(wert), name);
}

internal sealed record PdfMetadaten(string Titel, string? Autor);
