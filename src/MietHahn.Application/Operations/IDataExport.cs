namespace MietHahn.Application.Operations;

/// <summary>
/// Vollexport aller Daten als ZIP (JSON und CSV je Tabelle, dazu alle Dateien), lesbar ohne MietHahn.
/// Geheimnisse (Passwort-Hashes, Tokens, Schlüssel) sind ausgenommen.
/// </summary>
public interface IDataExport
{
    /// <summary>Vorschlag für den Dateinamen, z. B. <c>miethahn-export-2026-10-18.zip</c>.</summary>
    string FileName();

    /// <summary>Schreibt das ZIP in <paramref name="destination"/>, nur asynchron (geeignet für den Response-Body).</summary>
    Task ExportAsync(Stream destination, CancellationToken cancellationToken = default);
}
