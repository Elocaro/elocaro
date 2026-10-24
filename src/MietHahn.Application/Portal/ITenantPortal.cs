using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MietHahn.Application.Common;
using MietHahn.Domain.Leases;

namespace MietHahn.Application.Portal;

/// <summary>
/// What a signed-in tenant sees in the portal. Every method derives the tenant from the account (person linked via
/// <c>Person.UserId</c>) and never trusts an id from the request: foreign leases are simply not found.
/// </summary>
public interface ITenantPortal
{
    /// <summary>The tenant's own leases (current ones first); empty if the account is not linked to a person.</summary>
    Task<IReadOnlyList<PortalLeaseRow>> LeasesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Read-only contract data; <c>null</c> if the lease does not belong to the tenant.</summary>
    Task<PortalLease?> GetLeaseAsync(ClaimsPrincipal user, Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenant account (charges and payments) of each of the tenant's own leases, current ones first. Only what the tenant needs:
    /// no internal notes, no bank data of others.
    /// </summary>
    Task<IReadOnlyList<PortalAccount>> AccountsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>The active meters of the tenant's own homes (apartment, own room) with the last reading and an open report, if any.</summary>
    Task<IReadOnlyList<PortalMeter>> MetersAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports a reading as a proposal for the landlord (replaces an open report of the same meter). Only for the tenant's own meters;
    /// a value below the last reading, a future date or one before the installation is refused.
    /// </summary>
    Task<Result> ReportReadingAsync(ClaimsPrincipal user, Guid meterId, PortalReadingInput input, CancellationToken cancellationToken = default);

    Task<PortalContacts?> ContactsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Stores a change request; nothing changes until an administrator approves it.</summary>
    Task<Result> RequestContactChangeAsync(ClaimsPrincipal user, ContactChangeInput input, CancellationToken cancellationToken = default);

    /// <summary>Remembers that the tenant opened the file of a released document (first time only).</summary>
    Task MarkReadAsync(ClaimsPrincipal user, Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Ids of the documents the account has already opened.</summary>
    Task<IReadOnlySet<Guid>> ReadDocumentsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

/// <param name="RentDate">The day the rent is shown for: today, or the lease start while the lease has not begun yet.</param>
public sealed record PortalLeaseRow(Guid Id, string RentalUnit, string Property, DateOnly Start, DateOnly? End, LeaseStatus Status, decimal MonthlyRent, DateOnly RentDate);

/// <param name="Balance">Positive = arrears of the tenant, negative = credit.</param>
/// <param name="Due">Part of the balance that is already due.</param>
/// <param name="AccountingStart">Since when the account is kept; <c>null</c> = not yet.</param>
/// <param name="Entries">Newest first.</param>
public sealed record PortalAccount(Guid LeaseId, string RentalUnit, string Property, decimal Balance, decimal Due, DateOnly? AccountingStart, IReadOnlyList<PortalAccountEntry> Entries);

public sealed record PortalAccountEntry(DateOnly Date, string Text, decimal Debit, decimal Credit, decimal Balance, bool IsPayment);

public sealed class PortalReadingInput
{
    [Required(ErrorMessage = "Bitte das Ablesedatum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Zählerstand angeben.")]
    public decimal? Value { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

/// <param name="Pending">An open report of the tenant for this meter (value and date), not yet confirmed by the landlord.</param>
public sealed record PortalMeter(
    Guid Id,
    string Name,
    MietHahn.Domain.Metering.MeterType Type,
    string Number,
    MietHahn.Domain.Metering.MeasurementUnit Unit,
    string RentalUnit,
    decimal? LastValue,
    DateOnly? LastDate,
    (decimal Value, DateOnly Date)? Pending);

public sealed record PortalRentComponent(RentComponentType Kind, string? Name, decimal Amount);

public sealed record PortalLease(
    Guid Id,
    string RentalUnit,
    string Property,
    DateOnly Start,
    DateOnly? End,
    DateOnly? FixedTermUntil,
    LeaseStatus Status,
    IReadOnlyList<string> Tenants,
    IReadOnlyList<PortalRentComponent> Components,
    decimal MonthlyRent,
    BillingType BillingType,
    int DueOnWorkingDay,
    PaymentMethod PaymentMethod,
    decimal? Deposit,
    DateOnly RentDate);

public sealed record PortalContact(string Role, string Name, string? Phone, string? Email);

/// <param name="Current">The tenant's own data as stored.</param>
/// <param name="Pending">An open change request, if any.</param>
/// <param name="Responsible">Landlord and the caretakers of the tenant's properties.</param>
public sealed record PortalContacts(ContactChangeInput Current, ContactChangeInput? Pending, IReadOnlyList<PortalContact> Responsible)
{
    /// <summary>Emergency information of the sender profile of the tenant's property.</summary>
    public string? EmergencyInfo { get; init; }
}

public sealed class ContactChangeInput
{
    [OptionalEmail]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(50)]
    public string? Mobile { get; set; }
}
