using MietHahn.Domain.Dateien;

namespace MietHahn.Application.Dokumente;

/// <summary>Erzeugt PDF-Dokumente aus Scriban-Vorlagen im einheitlichen Layout (Briefkopf, DIN-5008-Anschriftfeld, Fußzeile).</summary>
public interface IPdfErzeugung
{
    /// <summary>Rendert das Dokument und liefert die PDF-Bytes (PDF/A-2b).</summary>
    Task<byte[]> ErzeugenAsync(PdfAuftrag auftrag, CancellationToken cancellationToken = default);

    /// <summary>Rendert das Dokument und legt es unveränderlich in der Dateiablage ab.</summary>
    Task<Datei> ErzeugenUndAblegenAsync(PdfAuftrag auftrag, string dateiname, CancellationToken cancellationToken = default);
}

/// <param name="Vorlage">Name einer eingebetteten Vorlage (<c>Pdf/Vorlagen/{name}.sbn</c>) – oder <c>null</c>, wenn <paramref name="VorlagenText"/> gesetzt ist.</param>
/// <param name="Titel">Dokumenttitel (PDF-Metadaten und Überschrift im Layout).</param>
/// <param name="Modell">Daten für die Vorlage (snake_case).</param>
/// <param name="Empfaenger">Mit Empfänger entsteht ein Brief (Anschriftfeld, Falzmarken), sonst ein schlichtes Dokument.</param>
/// <param name="VorlagenText">Vorlagentext, z. B. aus der vom Benutzer gepflegten Vorlagenverwaltung.</param>
/// <param name="Datum">Briefdatum; Standard ist heute (deutsche Zeit).</param>
public sealed record PdfAuftrag(
    string? Vorlage,
    string Titel,
    object? Modell = null,
    Briefanschrift? Empfaenger = null,
    string? VorlagenText = null,
    DateOnly? Datum = null);

/// <param name="Zeilen">Anschriftzeilen, z. B. Name, Straße, PLZ Ort (höchstens 6).</param>
/// <param name="Vermerk">Optionaler Vermerk über der Anschrift, z. B. „Einschreiben“.</param>
public sealed record Briefanschrift(IReadOnlyList<string> Zeilen, string? Vermerk = null);
