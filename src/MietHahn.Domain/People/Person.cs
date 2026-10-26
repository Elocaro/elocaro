using MietHahn.Domain.Common;

namespace MietHahn.Domain.People;

public enum Salutation
{
    None = 0,
    Mrs = 1,
    Mr = 2,
    Diverse = 3,
}

/// <summary>Contacts with a special function. Tenants and guarantors are derived from the leases.</summary>
public enum ContactType
{
    None = 0,
    Caretaker = 1,
    Tradesperson = 2,
    Utility = 3,
    Insurance = 4,
    Authority = 5,
    ServiceProvider = 6,
    Other = 9,
}

/// <summary>
/// Natural person or company: tenant, guarantor, caretaker, tradesperson, utility ... Addresses are time-dependent
/// (previous address, current address, new address after move-out for delivering the statement).
/// </summary>
public sealed class Person : Entity
{
    private Person()
    {
        LastName = "";
    }

    public Person(string lastName, string? firstName = null)
    {
        LastName = lastName;
        FirstName = firstName;
        Validate();
    }

    public Salutation Salutation { get; set; }

    public string? Title { get; set; }

    public string? FirstName { get; set; }

    /// <summary>Last name or, for companies, the company name.</summary>
    public string LastName { get; set; }

    /// <summary>Company (for contacts) or addition, e.g. "Stadtwerke Köln GmbH".</summary>
    public string? Company { get; set; }

    public ContactType ContactType { get; set; }

    /// <summary>Trade (tradesperson) or sector (utility), e.g. "Sanitär", "Wasser".</summary>
    public string? Occupation { get; set; }

    [Confidential]
    public DateOnly? DateOfBirth { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Mobile { get; set; }

    public string? Notes { get; set; }

    /// <summary>Linked user account (portal, service area).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Set when the personal data was removed after the retention period expired.</summary>
    public DateTimeOffset? AnonymizedAt { get; private set; }

    public List<PersonAddress> Addresses { get; private set; } = [];

    public List<BankDetails> BankDetails { get; private set; } = [];

    public bool IsAnonymized => AnonymizedAt is not null;

    /// <summary>"Dr. Anna Schmidt" or "Stadtwerke Köln GmbH (Max Müller)".</summary>
    public string DisplayName
    {
        get
        {
            var name = string.Join(' ', new[] { Title, FirstName, LastName }.Where(t => !string.IsNullOrWhiteSpace(t)));
            return Company is null ? name : $"{Company} ({name})";
        }
    }

    /// <summary>For sorted lists: "Schmidt, Anna".</summary>
    public string SortName => FirstName is null ? LastName : $"{LastName}, {FirstName}";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LastName);
        if (IsAnonymized)
        {
            throw new RuleViolationException("Anonymisierte Personen können nicht mehr geändert werden.");
        }
    }

    /// <summary>New address from the effective date (move); the previous one ends the day before.</summary>
    public PersonAddress SetAddress(DateOnly from, string street, string postalCode, string city, string? supplement = null, string country = "Deutschland")
    {
        Validate();
        return TimeSeries.CarryForward(Addresses, from, g => new PersonAddress(Id, g, street, postalCode, city, supplement, country));
    }

    public PersonAddress? AddressAt(DateOnly day) => TimeSeries.At(Addresses, day);

    public BankDetails AddBankDetails(string iban, string? accountHolder = null, string? bic = null)
    {
        Validate();
        if (!Iban.IsValid(iban))
        {
            throw new RuleViolationException($"„{iban}“ ist keine gültige IBAN (Prüfsumme stimmt nicht).");
        }

        var formatted = Iban.Format(iban);
        if (BankDetails.Any(b => b.Iban == formatted))
        {
            throw new RuleViolationException("Diese IBAN ist bereits hinterlegt.");
        }

        var newItem = new BankDetails(Id, formatted, string.IsNullOrWhiteSpace(accountHolder) ? null : accountHolder.Trim(), string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant());
        BankDetails.Add(newItem);
        return newItem;
    }

    /// <summary>Corrects IBAN, account holder and BIC of an entry.</summary>
    public void ChangeBankDetails(Guid bankDetailsId, string iban, string? accountHolder, string? bic)
    {
        Validate();
        var bank = BankDetails.SingleOrDefault(b => b.Id == bankDetailsId) ?? throw new RuleViolationException("Die Bankverbindung wurde nicht gefunden.");
        if (!Iban.IsValid(iban))
        {
            throw new RuleViolationException($"„{iban}“ ist keine gültige IBAN (Prüfsumme stimmt nicht).");
        }

        var formatted = Iban.Format(iban);
        if (BankDetails.Any(b => b.Id != bankDetailsId && b.Iban == formatted))
        {
            throw new RuleViolationException("Diese IBAN ist bereits hinterlegt.");
        }

        bank.Change(formatted, string.IsNullOrWhiteSpace(accountHolder) ? null : accountHolder.Trim(), string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Removes all personal data (GDPR, after the retention period has expired). The person remains as a record
    /// so that postings and leases stay analyzable.
    /// </summary>
    public void Anonymize(DateTimeOffset timestamp)
    {
        if (IsAnonymized)
        {
            return;
        }

        Salutation = Salutation.None;
        Title = null;
        FirstName = null;
        LastName = $"Anonymisiert {Id.ToString()[..8]}";
        Company = null;
        Occupation = null;
        DateOfBirth = null;
        Email = null;
        Phone = null;
        Mobile = null;
        Notes = null;
        UserId = null;
        Addresses.Clear();
        BankDetails.Clear();
        AnonymizedAt = timestamp;
    }
}

public sealed class PersonAddress : Entity, ITimeDependent
{
    private PersonAddress()
    {
        Street = "";
        PostalCode = "";
        City = "";
        Country = "";
    }

    internal PersonAddress(Guid personId, Period validity, string street, string postalCode, string city, string? supplement, string country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(street);
        ArgumentException.ThrowIfNullOrWhiteSpace(postalCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        PersonId = personId;
        Validity = validity;
        Street = street.Trim();
        PostalCode = postalCode.Trim();
        City = city.Trim();
        AddressLine2 = string.IsNullOrWhiteSpace(supplement) ? null : supplement.Trim();
        Country = string.IsNullOrWhiteSpace(country) ? "Deutschland" : country.Trim();
    }

    public Guid PersonId { get; private set; }

    public Period Validity { get; private set; }

    /// <summary>E.g. "c/o Müller" or "Wohnung 4".</summary>
    public string? AddressLine2 { get; private set; }

    public string Street { get; private set; }

    public string PostalCode { get; private set; }

    public string City { get; private set; }

    public string Country { get; private set; }

    public string SingleLine => string.Join(", ", new[] { AddressLine2, Street, $"{PostalCode} {City}", Country == "Deutschland" ? null : Country }.Where(t => t is not null));

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
}

/// <summary>Account of a person: for direct debit/refund and the automatic matching of incoming payments.</summary>
public sealed class BankDetails : Entity
{
    private BankDetails()
    {
        Iban = "";
    }

    internal BankDetails(Guid personId, string iban, string? accountHolder, string? bic)
    {
        PersonId = personId;
        Iban = iban;
        AccountHolder = accountHolder;
        Bic = bic;
    }

    public Guid PersonId { get; private set; }

    /// <summary>Formatted in groups of four.</summary>
    [Confidential]
    public string Iban { get; private set; }

    /// <summary>If different, e.g. parents pay the rent.</summary>
    public string? AccountHolder { get; private set; }

    public string? Bic { get; private set; }

    internal void Change(string iban, string? accountHolder, string? bic)
    {
        Iban = iban;
        AccountHolder = accountHolder;
        Bic = bic;
    }
}
