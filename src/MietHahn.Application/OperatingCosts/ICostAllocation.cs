using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.Receipts;

namespace MietHahn.Application.OperatingCosts;

/// <summary>
/// Allocation keys per property and cost type with validity, billing mode of shared flats and the cost types agreed in a lease
/// (basis of the operating cost statement).
/// </summary>
public interface ICostAllocation
{
    /// <summary>Keys valid on the day (with history) for all allocable cost types of the property.</summary>
    Task<CostAllocationOverview?> GetAsync(Guid propertyId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Sets the key of a cost type from the effective date on; the previous key ends the day before.</summary>
    Task<Result> SetRuleAsync(Guid propertyId, CostRuleInput input, CancellationToken cancellationToken = default);

    /// <summary>Ends the key valid on the day: the cost type is no longer active (the default key applies with a hint).</summary>
    Task<Result> EndRuleAsync(Guid propertyId, string costType, DateOnly lastDay, CancellationToken cancellationToken = default);

    Task<Result> SetSharedFlatBillingAsync(Guid unitId, SharedFlatBilling mode, CancellationToken cancellationToken = default);

    Task<LeaseCostAgreement?> GetLeaseAgreementAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>Cost types that are not agreed in the lease; empty = all allocable types are agreed.</summary>
    Task<Result> SetLeaseAgreementAsync(Guid leaseId, IReadOnlyCollection<string> excludedCostTypes, CancellationToken cancellationToken = default);
}

/// <summary>Own subtypes of the cost types (e.g. "Gebäudeversicherung" under no. 13).</summary>
public interface ICostSubtypes
{
    Task<IReadOnlyList<CostSubtypeView>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result> AddAsync(string costType, string name, CancellationToken cancellationToken = default);

    /// <summary>Removes a subtype; items that use it keep their cost type and lose the subtype.</summary>
    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record CostSubtypeView(Guid Id, string CostType, string CostTypeName, string Name);

public sealed class CostRuleInput
{
    [Required(ErrorMessage = "Bitte die Kostenart wählen.")]
    public string CostType { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Stichtag angeben.")]
    public DateOnly? From { get; set; }

    public AllocationKey Key { get; set; }

    public MeterType? MeterType { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Der Vorwegabzug liegt zwischen 0 und 100 %.", ParseLimitsInInvariantCulture = true)]
    public decimal? PreDeductionPercent { get; set; }

    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 1.000.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? PreDeductionAmount { get; set; }

    [StringLength(200)]
    public string? PreDeductionNote { get; set; }

    /// <summary>Vacant rooms of a shared flat are left out of the key (otherwise the landlord bears their share).</summary>
    public bool ExcludeVacantRooms { get; set; }

    /// <summary>For the key "fixed percent": percentage per unit (units without a value get none).</summary>
    public List<UnitShareInput> Shares { get; set; } = [];
}

/// <summary>Unit id and percentage as text: the form binder rejects empty values of other types in lists.</summary>
public sealed class UnitShareInput
{
    public string? UnitId { get; set; }

    public string? Percent { get; set; }
}

public sealed record UnitPercent(Guid UnitId, string UnitName, decimal Percent);

public sealed record CostRuleView(
    Guid Id,
    Period Validity,
    AllocationKey Key,
    MeterType? MeterType,
    decimal? PreDeductionPercent,
    decimal? PreDeductionAmount,
    string? PreDeductionNote,
    IReadOnlyList<UnitPercent> Shares,
    bool ExcludeVacantRooms = false);

/// <param name="Current">Rule valid on the requested day; <c>null</c> = no key (default: living area, with a hint in the calculation).</param>
public sealed record CostKeyRow(
    string CostType,
    string Name,
    int? BetrKvNumber,
    CostGroup Group,
    CostRuleView? Current,
    IReadOnlyList<CostRuleView> History);

public sealed record SharedFlatRow(Guid UnitId, string Name, SharedFlatBilling Mode, IReadOnlyList<string> Rooms);

public sealed record CostAllocationOverview(
    Guid PropertyId,
    string PropertyName,
    DateOnly Day,
    IReadOnlyList<CostKeyRow> Rows,
    IReadOnlyList<SharedFlatRow> SharedFlats,
    IReadOnlyList<ReferenceOption> Units);

public sealed record AgreementRow(string CostType, string Name, int? BetrKvNumber, bool Agreed);

public sealed record LeaseCostAgreement(Guid LeaseId, string Description, Guid PropertyId, IReadOnlyList<AgreementRow> Rows);
