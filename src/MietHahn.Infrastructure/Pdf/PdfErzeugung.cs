using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Dateien;
using MietHahn.Application.Dokumente;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Verwaltung;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Vorlagen;

namespace MietHahn.Infrastructure.Pdf;

internal sealed class PdfErzeugung(
    MietHahnDbContext db,
    ScribanRenderer renderer,
    GotenbergKonverter gotenberg,
    IDateiablage dateiablage,
    TimeProvider uhr) : IPdfErzeugung
{
    public async Task<byte[]> ErzeugenAsync(PdfAuftrag auftrag, CancellationToken cancellationToken = default)
    {
        var (html, fusszeile, absender) = await HtmlErzeugenAsync(auftrag, cancellationToken);
        return await gotenberg.HtmlZuPdfAsync(html, fusszeile, new PdfMetadaten(auftrag.Titel, absender.Name), cancellationToken);
    }

    public async Task<Datei> ErzeugenUndAblegenAsync(PdfAuftrag auftrag, string dateiname, CancellationToken cancellationToken = default)
    {
        var pdf = await ErzeugenAsync(auftrag, cancellationToken);
        using var inhalt = new MemoryStream(pdf);
        return await dateiablage.SpeichernAsync(inhalt, dateiname, cancellationToken);
    }

    /// <summary>Erzeugt das HTML von Dokument und Fußzeile (ohne Gotenberg; auch für Vorschau und Tests).</summary>
    internal async Task<(string Html, string Fusszeile, Vermieterprofil Absender)> HtmlErzeugenAsync(PdfAuftrag auftrag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        ArgumentException.ThrowIfNullOrWhiteSpace(auftrag.Titel);
        if (auftrag.Empfaenger is { Zeilen.Count: 0 or > 6 })
        {
            throw new ArgumentException("Eine Briefanschrift hat 1 bis 6 Zeilen.", nameof(auftrag));
        }

        var absender = await db.Set<Vermieterprofil>().AsNoTracking().OrderBy(v => v.ErstelltAm).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Es ist noch kein Vermieterprofil hinterlegt (Einstellungen → Vermieter). Es wird für Briefkopf und Fußzeile benötigt.");

        var inhalt = auftrag switch
        {
            { VorlagenText: { } text } => ScribanRenderer.RendernText(text, auftrag.Modell, name: auftrag.Titel),
            { Vorlage: { } name } => renderer.RendernEingebettet($"Pdf/Vorlagen/{name}.sbn", auftrag.Modell),
            _ => throw new ArgumentException("Vorlage oder Vorlagentext ist erforderlich.", nameof(auftrag)),
        };

        var rahmen = new Dictionary<string, object?>
        {
            ["titel"] = auftrag.Titel,
            ["absender"] = absender,
            ["empfaenger"] = auftrag.Empfaenger,
            ["datum_brief"] = auftrag.Datum ?? Zeitzone.Heute(uhr.GetUtcNow()),
            ["inhalt"] = new RohesHtml(inhalt.Html),
        };

        var html = renderer.RendernEingebettet("Pdf/Vorlagen/_layout.sbn", null, rahmen).Html;
        var fusszeile = renderer.RendernEingebettet("Pdf/Vorlagen/_fusszeile.sbn", null, rahmen).Html;
        return (html, fusszeile, absender);
    }
}
