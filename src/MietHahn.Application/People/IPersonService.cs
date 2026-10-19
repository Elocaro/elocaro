using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.People;

namespace MietHahn.Application.People;

/// <summary>Pflege von Personen: Mieter, Bürgen und Kontakte (Hausmeister, Handwerker, Versorger …).</summary>
public interface IPersonService
{
    Task<IReadOnlyList<PersonOverview>> SearchAsync(PersonQuery query, CancellationToken cancellationToken = default);

    /// <summary>Lädt die Personenakte und protokolliert den Zugriff (DSGVO).</summary>
    Task<PersonDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(PersonInput data, AddressInput? address = null, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, PersonInput data, CancellationToken cancellationToken = default);

    Task<Result> SetAddressAsync(Guid id, AddressInput input, CancellationToken cancellationToken = default);

    Task<Result> AddBankDetailsAsync(Guid id, BankDetailsInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveBankDetailsAsync(Guid id, Guid bankDetailsId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verknüpft die Person mit einem Benutzerkonto: ein vorhandenes Konto mit derselben E-Mail-Adresse oder eine neue
    /// Einladung (Rolle nach Kontaktart: Hausmeister, Handwerker, sonst Mieter).
    /// </summary>
    Task<Result> SetUpAccessAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Bezüge (Verträge …) möglich; sonst nach Ablauf der Frist anonymisieren.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Personen zur Auswahl in Formularen (z. B. Mieter eines Vertrags), ohne Zugriffsprotokoll.</summary>
    Task<IReadOnlyList<PersonOption>> OptionsAsync(CancellationToken cancellationToken = default);
}

public enum PersonFilter
{
    All = 0,

    /// <summary>Privatpersonen: Mieter, Bürgen, Interessenten.</summary>
    Private = 1,

    /// <summary>Kontakte mit Kontaktart (Hausmeister, Handwerker, Versorger …).</summary>
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
