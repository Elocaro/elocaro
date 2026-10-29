using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;

namespace Elocaro.Application.OperatingCosts;

/// <summary>Preview of the operating cost statement from stored receipts, cost items, keys, leases and the tenant account.</summary>
public interface IOperatingCosts
{
    /// <summary>Calculates the calendar year; <c>null</c> if the property does not exist. Saves nothing.</summary>
    Task<OperatingCostPreview?> PreviewAsync(Guid propertyId, int year, CancellationToken cancellationToken = default);

    /// <summary>Calculates an arbitrary closed billing period of at most 12 months.</summary>
    Task<OperatingCostPreview?> PreviewAsync(Guid propertyId, Period billingPeriod, CancellationToken cancellationToken = default);
}

/// <param name="Hints">Hints about the data basis (not part of the calculation), e.g. prepayments before the accounting start.</param>
public sealed record OperatingCostPreview(
    Guid PropertyId,
    string PropertyName,
    BillingResult Result,
    IReadOnlyList<string> Hints,
    IReadOnlyList<MeterCheck>? MeterChecks = null);

public enum CheckLevel
{
    Ok = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>Plausibility of a supply contract: main meter against the meters of the units, and the consumption of each unit.</summary>
/// <param name="Main">Main meter in the billing period; <c>null</c> if the contract has no main meter basis.</param>
/// <param name="UnitsTotal">Sum of the meters assigned to units.</param>
public sealed record MeterCheck(string Contract, string Unit, decimal? Main, decimal? UnitsTotal, decimal? DifferencePercent, CheckLevel Level, IReadOnlyList<UnitConsumptionRow> Consumption);

public sealed record UnitConsumptionRow(string Unit, decimal Quantity);
