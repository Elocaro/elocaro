using MietHahn.Domain.Common;

namespace MietHahn.Domain.People;

public enum Salutation
{
    None = 0,
    Mrs = 1,
    Mr = 2,
    Diverse = 3,
}

/// <summary>Kontakte mit besonderer Funktion. Mieter und Bürgen ergeben sich aus den Verträgen.</summary>
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
/// Natürliche Person oder Firma: Mieter, Bürge, Hausmeister, Handwerker, Versorger … Anschriften sind zeitabhängig
/// (Voranschrift, aktuelle Anschrift, neue Anschrift nach Auszug für die Zustellung der Abrechnung).
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

    /// <summary>Nachname bzw. bei Firmen der Firmenname.</summary>
    public string LastName { get; set; }

    /// <summary>Firma (bei Kontakten) bzw. Zusatz, z. B. „Stadtwerke Köln GmbH“.</summary>
    public string? Company { get; set; }

    public ContactType ContactType { get; set; }

    /// <summary>Gewerk (Handwerker) oder Sparte (Versorger), z. B. „Sanitär“, „Wasser“.</summary>
    public string? Occupation { get; set; }

    [Confidential]
    public DateOnly? DateOfBirth { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Mobile { get; set; }

    public string? Notes { get; set; }

    /// <summary>Verknüpftes Benutzerkonto (Portal, Servicebereich).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Gesetzt, wenn die personenbezogenen Daten nach Ablauf der Aufbewahrungsfrist entfernt wurden.</summary>
    public DateTimeOffset? AnonymizedAt { get; private set; }

    public List<PersonAddress> Addresses { get; private set; } = [];

    public List<BankDetails> BankDetails { get; private set; } = [];

    public bool IsAnonymized => AnonymizedAt is not null;

    /// <summary>„Dr. Anna Schmidt“ bzw. „Stadtwerke Köln GmbH (Max Müller)“.</summary>
    public string DisplayName
    {
        get
        {
            var name = string.Join(' ', new[] { Title, FirstName, LastName }.Where(t => !string.IsNullOrWhiteSpace(t)));
            return Company is null ? name : $"{Company} ({name})";
        }
    }

    /// <summary>Für sortierte Listen: „Schmidt, Anna“.</summary>
    public string SortName => FirstName is null ? LastName : $"{LastName}, {FirstName}";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LastName);
        if (IsAnonymized)
        {
            throw new RuleViolationException("Anonymisierte Personen können nicht mehr geändert werden.");
        }
    }

    /// <summary>Neue Anschrift ab Stichtag (Umzug); die bisherige endet am Vortag.</summary>
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

    /// <summary>
    /// Entfernt alle personenbezogenen Daten (DSGVO, nach Ablauf der Aufbewahrungsfrist). Die Person bleibt als Datensatz
    /// erhalten, damit Buchungen und Verträge auswertbar bleiben.
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

    /// <summary>z. B. „c/o Müller“ oder „Wohnung 4“.</summary>
    public string? AddressLine2 { get; private set; }

    public string Street { get; private set; }

    public string PostalCode { get; private set; }

    public string City { get; private set; }

    public string Country { get; private set; }

    public string SingleLine => string.Join(", ", new[] { AddressLine2, Street, $"{PostalCode} {City}", Country == "Deutschland" ? null : Country }.Where(t => t is not null));

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
}

/// <summary>Konto einer Person: für Lastschrift/Erstattung und die automatische Zuordnung eingehender Zahlungen.</summary>
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

    /// <summary>Formatiert in Vierergruppen.</summary>
    [Confidential]
    public string Iban { get; private set; }

    /// <summary>Falls abweichend, z. B. Eltern zahlen die Miete.</summary>
    public string? AccountHolder { get; private set; }

    public string? Bic { get; private set; }
}
