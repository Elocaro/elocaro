using MietHahn.Application.Common;

namespace MietHahn.Application.Leases;

/// <summary>Verbraucherpreisindex (Destatis 61111-0002, Basis 2020 = 100) für Indexmieten.</summary>
public interface ICpiService
{
    /// <summary>Neueste zuerst.</summary>
    Task<IReadOnlyList<CpiView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Legt den Wert eines Monats an oder ändert ihn.</summary>
    Task<Result> SetAsync(DateOnly month, decimal value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Importiert Werte aus einer CSV-Datei (z. B. Download aus GENESIS-Online, Tabelle 61111-0002). Erkannt werden Zeilen
    /// wie „2026;Januar;121,2“, „2026-01;121,2“ oder „01.2026;121,2“; alles andere wird übersprungen.
    /// </summary>
    Task<CpiImportResult> ImportAsync(string csv, CancellationToken cancellationToken = default);
}

public sealed record CpiView(Guid Id, DateOnly Month, decimal Value, string Source);

public sealed record CpiImportResult(int Added, int Modified, int Unchanged, IReadOnlyList<string> Errors);
