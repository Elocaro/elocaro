using MietHahn.Application.Common;
using MietHahn.Domain.Tax;

namespace MietHahn.Application.Tax;

/// <summary>Anlage V evaluation per property and year (cash-basis principle, § 11 EStG) and the line mapping per tax year.</summary>
public interface IAnlageVService
{
    /// <summary>The evaluation; <c>null</c> if the property does not exist.</summary>
    /// <param name="surplusForecastPositive">Total surplus forecast is positive: with a rent of 50 % to below 66 % of the local rent the expenses stay fully deductible.</param>
    Task<AnlageVReport?> GetAsync(Guid propertyId, int year, bool surplusForecastPositive = false, CancellationToken cancellationToken = default);

    /// <summary>Saves the local comparative rent (per year, including allocable costs) of a property and year; <c>null</c> removes it.</summary>
    Task<Result> SaveLocalRentAsync(Guid propertyId, int year, decimal? annualRent, CancellationToken cancellationToken = default);

    /// <summary>Lines of the form for a year: stored values, otherwise the built-in placeholders.</summary>
    Task<IReadOnlyList<LineMappingRow>> GetMappingAsync(int year, CancellationToken cancellationToken = default);

    Task<Result> SaveMappingAsync(int year, IReadOnlyDictionary<AnlageVCategory, int> lines, CancellationToken cancellationToken = default);
}

/// <param name="LocalRent">Local comparative rent per year, if entered.</param>
/// <param name="RentPercent">Actual rent (base rent and allocated costs) as a percentage of the local rent.</param>
public sealed record AnlageVReport(Guid PropertyId, string PropertyName, int Year, AnlageVResult Result, IReadOnlyList<string> Warnings, decimal? LocalRent = null, decimal? RentPercent = null);

public sealed record LineMappingRow(AnlageVCategory Category, AnlageVSection Section, string Label, int LineNumber, bool IsDefault);
