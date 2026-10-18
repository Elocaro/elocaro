using MietHahn.Application.Gemeinsam;

namespace MietHahn.Application.Vertraege;

/// <summary>Verbraucherpreisindex (Destatis 61111-0002, Basis 2020 = 100) für Indexmieten.</summary>
public interface IVpiVerwaltung
{
    /// <summary>Neueste zuerst.</summary>
    Task<IReadOnlyList<VpiAnsicht>> ListeAsync(CancellationToken cancellationToken = default);

    /// <summary>Legt den Wert eines Monats an oder ändert ihn.</summary>
    Task<Ergebnis> SetzenAsync(DateOnly monat, decimal wert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Importiert Werte aus einer CSV-Datei (z. B. Download aus GENESIS-Online, Tabelle 61111-0002). Erkannt werden Zeilen
    /// wie „2026;Januar;121,2“, „2026-01;121,2“ oder „01.2026;121,2“; alles andere wird übersprungen.
    /// </summary>
    Task<VpiImportErgebnis> ImportierenAsync(string csv, CancellationToken cancellationToken = default);
}

public sealed record VpiAnsicht(Guid Id, DateOnly Monat, decimal Wert, string Quelle);

public sealed record VpiImportErgebnis(int Neu, int Geaendert, int Unveraendert, IReadOnlyList<string> Fehler);
