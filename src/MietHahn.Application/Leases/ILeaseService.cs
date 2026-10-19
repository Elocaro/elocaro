using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;

namespace MietHahn.Application.Leases;

/// <summary>Mietverträge mit zeitabhängigen Bestandteilen, Kündigung und Mieterwechsel.</summary>
public interface ILeaseService
{
    Task<IReadOnlyList<LeaseOverview>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<LeaseDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Alle vermietbaren Objekte (Einheiten ohne Zimmer, WG-Zimmer) für die Auswahl.</summary>
    Task<IReadOnlyList<RentalUnitOption>> RentalUnitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Vertragshistorie eines Mietobjekts (neueste zuerst), inkl. Leerstandszeiten dazwischen.</summary>
    Task<IReadOnlyList<LeaseHistoryEntry>> HistoryAsync(Guid rentalUnitId, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(LeaseCreation creation, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, LeaseInput data, CancellationToken cancellationToken = default);

    Task<Result> SetComponentAsync(Guid id, ComponentInput input, CancellationToken cancellationToken = default);

    Task<Result> EndComponentAsync(Guid id, RentComponentType kind, DateOnly lastDay, CancellationToken cancellationToken = default);

    Task<Result> SetOccupantCountAsync(Guid id, OccupantCountInput input, CancellationToken cancellationToken = default);

    Task<Result> AddPartyAsync(Guid id, Guid personId, PartyRole role, CancellationToken cancellationToken = default);

    Task<Result> RemovePartyAsync(Guid id, Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Erfasst die Kündigung; ohne Termin wird er nach § 573c BGB berechnet.</summary>
    Task<Result> TerminateAsync(Guid id, TerminationInput input, CancellationToken cancellationToken = default);

    Task<Result> RevokeTerminationAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mieterwechsel: beendet den Vertrag und schreibt die neuen Anschriften der Mieter fort.</summary>
    Task<Result> MoveOutAsync(Guid id, MoveOutInput input, CancellationToken cancellationToken = default);

    Task<Result> SetRentModelAsync(Guid id, RentModelInput input, CancellationToken cancellationToken = default);

    Task<Result> AddRentStepAsync(Guid id, RentStepInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveRentStepAsync(Guid id, Guid rentStepId, CancellationToken cancellationToken = default);

    /// <summary>Übernimmt die Indexanpassung: neue Kaltmiete ab dem übernächsten Monat nach Zugang der Erklärung.</summary>
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

/// <param name="To">Ende des laufenden Vertrags, <c>null</c> = unbefristet.</param>
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

/// <param name="LastIncrease">Beginn der aktuellen Kaltmiete, falls nach Mietbeginn geändert.</param>
/// <param name="BaseRentThreeYearsAgo">Für die Kappungsgrenze (§ 558 Abs. 3 BGB).</param>
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

/// <summary>Berechnete Indexanpassung mit dem neuesten VPI-Wert.</summary>
public sealed record IndexProposal(DateOnly CpiMonth, decimal CpiValue, decimal ChangePercent, decimal NewBaseRent, DateOnly EarliestEffective, bool PossibleNow);

/// <summary>Ein Vertrag oder eine Leerstandszeit in der Historie eines Mietobjekts.</summary>
public sealed record LeaseHistoryEntry(Guid? LeaseId, Period Period, string? Tenant, decimal? BaseRent)
{
    public bool IsVacancy => LeaseId is null;
}
