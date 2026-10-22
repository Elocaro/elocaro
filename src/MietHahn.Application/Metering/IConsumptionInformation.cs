using System.Security.Claims;
using MietHahn.Domain.Metering;

namespace MietHahn.Application.Metering;

/// <summary>
/// Monthly consumption information for tenants (§ 6a HeizkostenV): heat and hot water of the apartment in a month, compared with
/// the previous month, the same month of the year before and the average of the apartments in the house.
/// </summary>
public interface IConsumptionInformation
{
    /// <summary>Administration: the last full months of a lease, newest first (not before the start of the lease).</summary>
    Task<IReadOnlyList<MonthlyConsumption>> ForLeaseAsync(Guid leaseId, int months, CancellationToken cancellationToken = default);

    /// <summary>One month of a lease; <c>null</c> if the lease has no heat or hot water meter of its own.</summary>
    Task<MonthlyConsumption?> ForLeaseMonthAsync(Guid leaseId, DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>Tenant: the same for the own current leases. Other accounts: empty.</summary>
    Task<IReadOnlyList<TenantConsumption>> ForTenantAsync(ClaimsPrincipal user, int months, CancellationToken cancellationToken = default);
}

/// <param name="Month">First day of the month.</param>
public sealed record MonthlyConsumption(DateOnly Month, IReadOnlyList<ConsumptionLine> Lines);

/// <param name="Label">E.g. "Heizung (Wärme)".</param>
/// <param name="Current">Consumption in the month; <c>null</c> if readings are missing (see <paramref name="Hint"/>).</param>
/// <param name="Own">Own consumption per reference unit (see <paramref name="BasisText"/>).</param>
/// <param name="Average">Average of the apartments in the house per reference unit; <c>null</c> with fewer than three comparable apartments.</param>
/// <param name="BasisText">"je m²" or "je Person".</param>
public sealed record ConsumptionLine(
    MeterType Type,
    string Label,
    MeasurementUnit Unit,
    decimal? Current,
    decimal? Previous,
    decimal? LastYear,
    decimal? Own,
    decimal? Average,
    int Comparisons,
    string BasisText,
    string? Hint);

public sealed record TenantConsumption(Guid LeaseId, string Unit, IReadOnlyList<MonthlyConsumption> Months);
