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
