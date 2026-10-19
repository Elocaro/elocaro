using MietHahn.Domain.Common;
using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Application.OperatingCosts;

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
    IReadOnlyList<string> Hints);
