using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.People;

namespace MietHahn.Application.People;

/// <summary>Maintenance of persons: tenants, guarantors and contacts (caretakers, tradespeople, utilities ...).</summary>
public interface IPersonService
{
    Task<IReadOnlyList<PersonOverview>> SearchAsync(PersonQuery query, CancellationToken cancellationToken = default);

    /// <summary>Loads the person record and logs the access (GDPR).</summary>
    Task<PersonDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(PersonInput data, AddressInput? address = null, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, PersonInput data, CancellationToken cancellationToken = default);

    Task<Result> SetAddressAsync(Guid id, AddressInput input, CancellationToken cancellationToken = default);

    Task<Result> AddBankDetailsAsync(Guid id, BankDetailsInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveBankDetailsAsync(Guid id, Guid bankDetailsId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links the person to a user account: an existing account with the same email address or a new
    /// invitation (role by contact type: caretaker, tradesperson, otherwise tenant).
    /// </summary>
    Task<Result> SetUpAccessAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Only possible without references (leases ...); otherwise anonymize after the retention period.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persons for selection in forms (e.g. tenants of a lease), without access log.</summary>
    Task<IReadOnlyList<PersonOption>> OptionsAsync(CancellationToken cancellationToken = default);
}

public enum PersonFilter
{
    All = 0,

    /// <summary>Private persons: tenants, guarantors, prospects.</summary>
    Private = 1,

    /// <summary>Contacts with contact type (caretaker, tradesperson, utility ...).</summary>
    Contacts = 2,
}

public sealed record PersonQuery(string? Text = null, PersonFilter Filter = PersonFilter.All, bool IncludeAnonymized = false);

public sealed record PersonOverview(
    Guid Id,
    string DisplayName,
    string SortName,
    ContactType ContactType,
    string? Occupation,
    string? Email,
    string? Phone,
    string? City,
    IReadOnlyList<string> Roles,
    bool HasAccess,
    bool Anonymized);

public sealed record PersonOption(Guid Id, string DisplayName, string? City);

public sealed record AddressVersion(Period Validity, string SingleLine);

public sealed record BankDetailsView(Guid Id, string Iban, string? AccountHolder, string? Bic);

public sealed record AccessView(Guid UserId, string Email, string Role, bool Locked);

public sealed record PersonDetails(
    Guid Id,
    PersonInput Data,
    string DisplayName,
    AddressVersion? Address,
    IReadOnlyList<AddressVersion> Addresses,
    IReadOnlyList<BankDetailsView> BankDetails,
    AccessView? Access,
    IReadOnlyList<PersonRelation> References,
    DateTimeOffset? AnonymizedAt);
