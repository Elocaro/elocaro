namespace MietHahn.Application.Betrieb;

/// <summary>
/// Vollexport aller Daten als ZIP (JSON und CSV je Tabelle, dazu alle Dateien), lesbar ohne MietHahn.
/// Geheimnisse (Passwort-Hashes, Tokens, Schlüssel) sind ausgenommen.
/// </summary>
public interface IDatenexport
{
    /// <summary>Vorschlag für den Dateinamen, z. B. <c>miethahn-export-2026-10-18.zip</c>.</summary>
    string Dateiname();

    /// <summary>Schreibt das ZIP in <paramref name="ziel"/>, nur asynchron (geeignet für den Response-Body).</summary>
    Task ExportierenAsync(Stream ziel, CancellationToken cancellationToken = default);
}
