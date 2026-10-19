using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;

namespace MietHahn.Application.Admin;

/// <summary>Pflege der Absenderangaben (Briefkopf, Fußzeile, Bankverbindung).</summary>
public interface ILandlordProfileService
{
    Task<LandlordProfileInput?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LandlordProfileInput data, CancellationToken cancellationToken = default);
}

public sealed class LandlordProfileInput
{
    [Required(ErrorMessage = "Bitte den Namen angeben.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "Bitte Straße und Hausnummer angeben.")]
    [StringLength(200)]
    public string Street { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Postleitzahl angeben.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Die Postleitzahl hat 5 Ziffern.")]
    public string PostalCode { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Ort angeben.")]
    [StringLength(100)]
    public string City { get; set; } = "";

    [StringLength(50)]
    public string? Phone { get; set; }

    [OptionalEmail]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(42)]
    public string? Iban { get; set; }

    [StringLength(11)]
    public string? Bic { get; set; }

    [StringLength(100)]
    public string? Bank { get; set; }

    [StringLength(30)]
    public string? TaxNumber { get; set; }
}
