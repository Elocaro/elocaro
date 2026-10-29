using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;

namespace Elocaro.Application.Admin;

/// <summary>Maintenance of the sender details (letterhead, footer, bank details).</summary>
public interface ILandlordProfileService
{
    /// <summary>The standard profile (the oldest one); <c>null</c> if none exists yet.</summary>
    Task<LandlordProfileInput?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LandlordProfileInput data, CancellationToken cancellationToken = default);

    /// <summary>Emergency information of the profile that applies to the property (the standard profile if none is chosen).</summary>
    Task<string?> EmergencyInfoAsync(Guid propertyId, CancellationToken cancellationToken = default);

    /// <summary>All profiles, the standard profile first. Further profiles can be chosen per property.</summary>
    Task<IReadOnlyList<LandlordProfileRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<LandlordProfileInput?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a further profile (<paramref name="id"/> empty) or changes one.</summary>
    Task<Result> SaveAsync(Guid? id, LandlordProfileInput data, CancellationToken cancellationToken = default);

    /// <summary>Deletes a further profile; not the standard profile and not one a property still uses.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record LandlordProfileRow(Guid Id, string Name, string City, bool IsDefault, int Properties);

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

    [StringLength(500)]
    public string? EmergencyInfo { get; set; }
}
