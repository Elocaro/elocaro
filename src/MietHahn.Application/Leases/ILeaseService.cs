using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;

namespace MietHahn.Application.Leases;

/// <summary>Leases with time-dependent components, termination and tenant change.</summary>
public interface ILeaseService
{
    Task<IReadOnlyList<LeaseOverview>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<LeaseDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All rentable objects (units without rooms, shared-flat rooms) for selection.</summary>
    Task<IReadOnlyList<RentalUnitOption>> RentalUnitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lease history of a rental object (newest first), including vacancy periods in between.</summary>
    Task<IReadOnlyList<LeaseHistoryEntry>> HistoryAsync(Guid rentalUnitId, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(LeaseCreation creation, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, LeaseInput data, CancellationToken cancellationToken = default);

    /// <summary>Corrects start and fixed-term end of the lease (typing errors); the monthly rents are recalculated by the nightly job.</summary>
    Task<Result> CorrectTermAsync(Guid id, LeaseTermInput input, CancellationToken cancellationToken = default);

    Task<Result> SetComponentAsync(Guid id, ComponentInput input, CancellationToken cancellationToken = default);

    Task<Result> EndComponentAsync(Guid id, RentComponentType kind, DateOnly lastDay, CancellationToken cancellationToken = default);

    Task<Result> SetOccupantCountAsync(Guid id, OccupantCountInput input, CancellationToken cancellationToken = default);

    Task<Result> AddPartyAsync(Guid id, Guid personId, PartyRole role, CancellationToken cancellationToken = default);

    Task<Result> RemovePartyAsync(Guid id, Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Records the termination; without a date it is calculated under § 573c BGB.</summary>
    Task<Result> TerminateAsync(Guid id, TerminationInput input, CancellationToken cancellationToken = default);

    Task<Result> RevokeTerminationAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tenant change: ends the lease and carries forward the tenants' new addresses.</summary>
    Task<Result> MoveOutAsync(Guid id, MoveOutInput input, CancellationToken cancellationToken = default);

    Task<Result> SetRentModelAsync(Guid id, RentModelInput input, CancellationToken cancellationToken = default);

    Task<Result> AddRentStepAsync(Guid id, RentStepInput input, CancellationToken cancellationToken = default);

    /// <summary>Corrects a step that has not been adopted yet.</summary>
    Task<Result> UpdateRentStepAsync(Guid id, Guid rentStepId, RentStepInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveRentStepAsync(Guid id, Guid rentStepId, CancellationToken cancellationToken = default);

    /// <summary>Adopts the index adjustment: new base rent from the month after next following receipt of the declaration.</summary>
    Task<Result> AdjustIndexAsync(Guid id, IndexAdjustmentInput input, CancellationToken cancellationToken = default);
}

public sealed record LeaseOverview(
    Guid Id,
    string RentalUnit,
    string Property,
    string Tenant,
    DateOnly Start,
    DateOnly? End,
    LeaseStatus Status,
    decimal MonthlyRent,
    BillingType BillingType);

public sealed record RentalUnitOption(Guid Id, string Name, string Property, bool IsRoom, Occupied? CurrentlyOccupied);

/// <param name="To">End of the current lease, <c>null</c> = open-ended.</param>
public sealed record Occupied(string Tenant, DateOnly? To);

public sealed record LeasePartyView(Guid PersonId, string Name, PartyRole Role, string? Email, string? Phone);

public sealed record ComponentVersion(Guid Id, RentComponentType Kind, string? Name, Period Validity, decimal Amount);

public sealed record OccupantCountView(Period Validity, int Count);

public sealed record LeaseDetails(
    Guid Id,
    Guid RentalUnitId,
    Guid UnitId,
    Guid? RoomId,
    Guid PropertyId,
    string RentalUnit,
    string Property,
    Period Term,
    DateOnly? FixedTermUntil,
    LeaseStatus Status,
    DateOnly? TerminationReceivedOn,
    TerminatingParty? TerminatedBy,
    LeaseInput Data,
    IReadOnlyList<LeasePartyView> Parties,
    IReadOnlyList<ComponentVersion> CurrentComponents,
    IReadOnlyList<ComponentVersion> Components,
    IReadOnlyList<OccupantCountView> People,
    decimal MonthlyRent,
    DateOnly ReferenceDate,
    DateOnly? NextDueDate,
    int LandlordNoticePeriodMonths,
    RentModelView RentModel);

/// <param name="LastIncrease">Start of the current base rent, if changed after the lease start.</param>
/// <param name="BaseRentThreeYearsAgo">For the cap limit (§ 558 Abs. 3 BGB).</param>
public sealed record RentModelView(
    RentModel Model,
    DateOnly? LastIncrease,
    decimal? BaseRentThreeYearsAgo,
    decimal? BaseRent,
    DateOnly? IndexBaseMonth,
    decimal? IndexBaseValue,
    IReadOnlyList<RentStepView> RentSteps,
    IndexProposal? Index);

public sealed record RentStepView(Guid Id, DateOnly From, decimal BaseRent, DateTimeOffset? AppliedAt);

/// <summary>Calculated index adjustment with the latest CPI value.</summary>
public sealed record IndexProposal(DateOnly CpiMonth, decimal CpiValue, decimal ChangePercent, decimal NewBaseRent, DateOnly EarliestEffective, bool PossibleNow);

/// <summary>A lease or a vacancy period in the history of a rental object.</summary>
public sealed record LeaseHistoryEntry(Guid? LeaseId, Period Period, string? Tenant, decimal? BaseRent)
{
    public bool IsVacancy => LeaseId is null;
}
