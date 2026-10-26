using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.OperatingCosts;

/// <summary>Source of consumption quantities (meters with readings); implemented by the infrastructure, replaced by a table in tests.</summary>
public interface IConsumptionSource
{
    /// <summary>Consumption of the meters of the type assigned to the unit in the period.</summary>
    ConsumptionQuantity Quantity(Guid unitId, MeterType type, Period period);
}

/// <param name="Value">Consumption, <c>null</c> if readings or meters are missing.</param>
/// <param name="Interpolated">An interpolated reading was used (e.g. interim reading on tenant change missing).</param>
/// <param name="Note">Proof (readings used) or the reason why <paramref name="Value"/> is missing.</param>
/// <param name="InterpolatedDays">Days (end-of-day readings) whose reading was interpolated; <c>null</c> = unknown (then <paramref name="Interpolated"/> counts for every boundary).</param>
public sealed record ConsumptionQuantity(decimal? Value, bool Interpolated = false, string? Note = null, IReadOnlyList<DateOnly>? InterpolatedDays = null);

public sealed record AreaState(Period Validity, decimal LivingArea, decimal HeatedArea);

public sealed record RoomAreaState(Period Validity, decimal AllocationArea);

public sealed record BillingRoom(Guid Id, string Name, IReadOnlyList<RoomAreaState> Areas);

/// <param name="Rooms">Not empty for a shared flat: then the rooms are rented, not the unit.</param>
/// <param name="SelfPaidCostTypes">Cost types the tenants pay directly (the unit is left out of their allocation).</param>
public sealed record BillingUnit(
    Guid Id,
    string Name,
    IReadOnlyList<AreaState> Areas,
    IReadOnlyList<BillingRoom> Rooms,
    SharedFlatBilling SharedFlat = SharedFlatBilling.FlatRate,
    IReadOnlySet<string>? SelfPaidCostTypes = null);

public sealed record PersonsState(Period Validity, int Count);

/// <summary>A prepayment month from the tenant account: <paramref name="Paid"/> is what was actually received for the claim, not the target.</summary>
public sealed record PrepaymentMonth(DateOnly ServiceMonth, RentComponentType Component, decimal Due, decimal Paid);

/// <param name="ReceivesStatement">Prepayment lease; otherwise (flat rate, inclusive) the share stays with the landlord and is compared internally.</param>
/// <param name="ExcludedCostTypes">Cost types that are not agreed in the lease.</param>
/// <param name="ExtraRoomIds">Further rooms of a lease on several rooms of a shared flat.</param>
/// <param name="ReceivesHeatingStatement">Heating costs are billed by consumption (also with a flat rate if agreed, HeizkostenV takes precedence); <c>null</c> = like <paramref name="ReceivesStatement"/>.</param>
public sealed record BillingLease(
    Guid Id,
    string Label,
    Guid UnitId,
    Guid? RoomId,
    Period Term,
    IReadOnlyList<PersonsState> Persons,
    bool ReceivesStatement,
    IReadOnlySet<string> ExcludedCostTypes,
    IReadOnlyList<PrepaymentMonth> Prepayments,
    bool? ReceivesHeatingStatement = null,
    IReadOnlyList<Guid>? ExtraRoomIds = null)
{
    /// <summary>Whether the lease covers the room (its first room or one of the further rooms).</summary>
    public bool CoversRoom(Guid roomId) => RoomId == roomId || ExtraRoomIds?.Contains(roomId) == true;
}

public sealed record BillingCost(
    Guid Id,
    string CostType,
    string Description,
    decimal Amount,
    Period BillingPeriod,
    bool Allocable,
    Guid? UnitId = null,
    Section35a Section35a = Section35a.None,
    decimal? LaborCosts = null,
    string? Subtype = null,
    HeatingCostKind? HeatingKind = null,
    Guid? ReceiptId = null,
    decimal? FuelQuantity = null,
    string? FuelUnit = null,
    decimal? Co2Kg = null);

public sealed record FixedShare(Guid UnitId, decimal Percent);

public sealed record BillingRule(
    string CostType,
    Period Validity,
    AllocationKey Key,
    MeterType? MeterType = null,
    decimal? PreDeductionPercent = null,
    decimal? PreDeductionAmount = null,
    string? PreDeductionNote = null,
    IReadOnlyList<FixedShare>? Shares = null);

/// <param name="VacancyPersons">Persons assumed for a vacant rental object in the person key (otherwise vacancy would shift to the other units).</param>
public sealed record BillingOptions(int VacancyPersons = 1);

/// <param name="BillingPeriod">Closed period of at most 12 months.</param>
public sealed record BillingInput(
    Period BillingPeriod,
    IReadOnlyList<BillingUnit> Units,
    IReadOnlyList<BillingLease> Leases,
    IReadOnlyList<BillingCost> Costs,
    IReadOnlyList<BillingRule> Rules,
    IConsumptionSource Consumption,
    BillingOptions? Options = null);

/// <summary>Plug-in point for a further cost group (heating costs under HeizkostenV).</summary>
public interface ICostGroupCalculator
{
    CostGroup Group { get; }

    /// <param name="input">Complete input; <paramref name="costs"/> are the cost items of this group that overlap the billing period.</param>
    GroupResult Calculate(BillingInput input, IReadOnlyList<BillingCost> costs);
}

/// <summary>
/// Result of an additional cost group; shares of tenants must add up with the landlord shares to the cost totals.
/// </summary>
/// <param name="FlatRateActuals">Actual costs per lease that is not billed (flat rate); they enter the internal flat-rate comparison.</param>
/// <param name="Sections">Property-wide explanations (e.g. the split of heating and hot water, the CO₂ level) for the preview.</param>
public sealed record GroupResult(
    IReadOnlyList<CostTypeResult> CostTypes,
    IReadOnlyList<GroupTenantLine> TenantLines,
    IReadOnlyList<LandlordPosition> Landlord,
    IReadOnlyList<CalculationProblem> Problems,
    IReadOnlyDictionary<Guid, decimal>? FlatRateActuals = null,
    IReadOnlyList<GroupSection>? Sections = null);

/// <summary>A titled list of calculation steps that is not tied to a single tenant.</summary>
/// <param name="Notice">Hint that the data of the section needs verification (versioned legal tables).</param>
public sealed record GroupSection(string Id, string Title, IReadOnlyList<ProtocolEntry> Entries, string? Notice = null);

public sealed record GroupTenantLine(Guid LeaseId, TenantCostLine Line);
