using MietHahn.Domain.Common;

namespace MietHahn.Domain.Admin;

/// <summary>
/// Absenderangaben des Vermieters für Briefkopf, Fußzeile und Zahlungsinformationen in Dokumenten.
/// Aktuell gibt es genau ein Profil; mit „Eigentümer je Objekt“ (M7) wird es je Objekt zugeordnet.
/// </summary>
public sealed class LandlordProfile : Entity
{
    private LandlordProfile()
    {
        Name = "";
        Street = "";
        PostalCode = "";
        City = "";
    }

    public LandlordProfile(string name, string street, string postalCode, string city)
    {
        Name = name;
        Street = street;
        PostalCode = postalCode;
        City = city;
        Validate();
    }

    public string Name { get; set; }

    /// <summary>Optionaler Zusatz unter dem Namen, z. B. „Hausverwaltung Hauptstraße 5“.</summary>
    public string? AddressLine2 { get; set; }

    public string Street { get; set; }

    public string PostalCode { get; set; }

    public string City { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    [Confidential]
    public string? Iban { get; set; }

    public string? Bic { get; set; }

    public string? Bank { get; set; }

    public string? TaxNumber { get; set; }

    /// <summary>Einzeilige Absenderangabe für die Rücksendezeile im Anschriftfeld.</summary>
    public string ReturnAddressLine => $"{Name} · {Street} · {PostalCode} {City}";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Street);
        ArgumentException.ThrowIfNullOrWhiteSpace(PostalCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(City);
    }
}
