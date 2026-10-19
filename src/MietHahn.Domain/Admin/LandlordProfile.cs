using MietHahn.Domain.Common;

namespace MietHahn.Domain.Admin;

/// <summary>
/// Landlord's sender details for letterhead, footer and payment information in documents.
/// Currently there is exactly one profile; with "owner per property" (M7) it will be assigned per property.
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

    /// <summary>Optional addition below the name, e.g. "Hausverwaltung Hauptstraße 5".</summary>
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

    /// <summary>Single-line sender address for the return line in the address window.</summary>
    public string ReturnAddressLine => $"{Name} · {Street} · {PostalCode} {City}";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Street);
        ArgumentException.ThrowIfNullOrWhiteSpace(PostalCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(City);
    }
}
