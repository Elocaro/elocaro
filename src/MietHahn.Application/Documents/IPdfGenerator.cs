using MietHahn.Domain.Files;

namespace MietHahn.Application.Documents;

/// <summary>Erzeugt PDF-Dokumente aus Scriban-Vorlagen im einheitlichen Layout (Briefkopf, DIN-5008-Anschriftfeld, Fußzeile).</summary>
public interface IPdfGenerator
{
    /// <summary>Rendert das Dokument und liefert die PDF-Bytes (PDF/A-2b).</summary>
    Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default);

    /// <summary>Rendert das Dokument und legt es unveränderlich in der Dateiablage ab.</summary>
    Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default);
}

/// <param name="Template">Name einer eingebetteten Vorlage (<c>Pdf/Templates/{name}.sbn</c>) – oder <c>null</c>, wenn <paramref name="TemplateText"/> gesetzt ist.</param>
/// <param name="Title">Dokumenttitel (PDF-Metadaten und Überschrift im Layout).</param>
/// <param name="Model">Daten für die Vorlage (snake_case).</param>
/// <param name="Recipient">Mit Empfänger entsteht ein Brief (Anschriftfeld, Falzmarken), sonst ein schlichtes Dokument.</param>
/// <param name="TemplateText">Vorlagentext, z. B. aus der vom Benutzer gepflegten Vorlagenverwaltung.</param>
/// <param name="Date">Briefdatum; Standard ist heute (deutsche Zeit).</param>
public sealed record PdfRequest(
    string? Template,
    string Title,
    object? Model = null,
    PostalAddress? Recipient = null,
    string? TemplateText = null,
    DateOnly? Date = null);

/// <param name="Rows">Anschriftzeilen, z. B. Name, Straße, PLZ Ort (höchstens 6).</param>
/// <param name="Remark">Optionaler Vermerk über der Anschrift, z. B. „Einschreiben“.</param>
public sealed record PostalAddress(IReadOnlyList<string> Rows, string? Remark = null);
