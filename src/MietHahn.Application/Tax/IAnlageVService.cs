using MietHahn.Application.Common;
using MietHahn.Domain.Tax;

namespace MietHahn.Application.Tax;

/// <summary>Anlage V evaluation per property and year (cash-basis principle, § 11 EStG) and the line mapping per tax year.</summary>
public interface IAnlageVService
{
    /// <summary>The evaluation; <c>null</c> if the property does not exist.</summary>
    /// <param name="rentPercentOfLocal">Rent as percentage of the local comparative rent (for the hint on cheap lettings), optional.</param>
    Task<AnlageVReport?> GetAsync(Guid propertyId, int year, decimal? rentPercentOfLocal = null, CancellationToken cancellationToken = default);

    /// <summary>Lines of the form for a year: stored values, otherwise the built-in placeholders.</summary>
    Task<IReadOnlyList<LineMappingRow>> GetMappingAsync(int year, CancellationToken cancellationToken = default);

    Task<Result> SaveMappingAsync(int year, IReadOnlyDictionary<AnlageVCategory, int> lines, CancellationToken cancellationToken = default);
}

public sealed record AnlageVReport(Guid PropertyId, string PropertyName, int Year, AnlageVResult Result, IReadOnlyList<string> Warnings);

public sealed record LineMappingRow(AnlageVCategory Category, AnlageVSection Section, string Label, int LineNumber, bool IsDefault);
