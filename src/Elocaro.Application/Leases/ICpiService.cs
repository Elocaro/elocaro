using Elocaro.Application.Common;

namespace Elocaro.Application.Leases;

/// <summary>Consumer price index (Destatis 61111-0002, base 2020 = 100) for index rents.</summary>
public interface ICpiService
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<CpiView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or changes the value of a month.</summary>
    Task<Result> SetAsync(DateOnly month, decimal value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports values from a CSV file (e.g. download from GENESIS-Online, table 61111-0002). Recognized are rows
    /// like "2026;Januar;121,2", "2026-01;121,2" or "01.2026;121,2"; everything else is skipped.
    /// </summary>
    Task<CpiImportResult> ImportAsync(string csv, CancellationToken cancellationToken = default);
}

public sealed record CpiView(Guid Id, DateOnly Month, decimal Value, string Source);

public sealed record CpiImportResult(int Added, int Modified, int Unchanged, IReadOnlyList<string> Errors);
