using System.ComponentModel.DataAnnotations;
using Elocaro.Domain.Properties;

namespace Elocaro.Application.Properties;

/// <summary>Form and display data of a property.</summary>
public sealed class PropertyInput
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Bitte Straße und Hausnummer angeben.")]
    [StringLength(200)]
    public string Street { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Postleitzahl angeben.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Die Postleitzahl hat 5 Ziffern.")]
    public string PostalCode { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Ort angeben.")]
    [StringLength(100)]
    public string City { get; set; } = "";

    [Range(1500, 2100, ErrorMessage = "Bitte ein Baujahr zwischen 1500 und 2100 angeben.")]
    public int? YearBuilt { get; set; }

    [StringLength(100)]
    public string? CadastralDistrict { get; set; }

    [StringLength(50)]
    public string? ParcelNumber { get; set; }

    [StringLength(50)]
    public string? PropertyTaxReference { get; set; }

    public HeatingType HeatingType { get; set; }

    public EnergySource EnergySource { get; set; }

    /// <summary>Cap limit of rent increases in three years in percent: 20, or 15 in areas with a tight housing market (§ 558 Abs. 3 BGB).</summary>
    [Range(15, 20, ErrorMessage = "Die Kappungsgrenze beträgt 15 oder 20 Prozent.")]
    public int RentCapPercent { get; set; } = 20;

    [StringLength(4000)]
    public string? Notes { get; set; }

    /// <summary>Sender details for the documents of this property; empty = the standard profile.</summary>
    public Guid? LandlordProfileId { get; set; }
}

public sealed class UnitInput
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben, z. B. „Wohnung 1“.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(100)]
    public string? Location { get; set; }

    /// <summary>Floor for the building plan; empty = read from the location.</summary>
    public int? Floor { get; set; }

    public UnitType Type { get; set; }

    [Range(typeof(decimal), "0.5", "50", ErrorMessage = "Bitte eine Zimmeranzahl zwischen 0,5 und 50 angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? RoomCount { get; set; }

    [StringLength(100)]
    public string? Bathroom { get; set; }

    public bool Balcony { get; set; }

    public bool FittedKitchen { get; set; }

    [StringLength(50)]
    public string? EnergyStandard { get; set; }

    [StringLength(2000)]
    public string? Amenities { get; set; }

    /// <summary>Cost types the tenants pay directly to the supplier (e.g. waste): the unit is left out of their allocation.</summary>
    public List<string> SelfPaidCostTypes { get; set; } = [];
}

public sealed class RoomInput
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben, z. B. „Zimmer 1“.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    public Furnishing Furnishing { get; set; }

    /// <summary>Furniture and equipment handed over with the room, one item per line.</summary>
    [StringLength(4000)]
    public string? FurnishingList { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

/// <summary>
/// New area state from the effective date. Meaning per level: property = total/common area,
/// unit = living/heated area (empty = same as living area), room = room area/share of common area.
/// </summary>
public sealed class AreaInput
{
    [Required(ErrorMessage = "Bitte angeben, ab wann die Fläche gilt.")]
    public DateOnly? From { get; set; }

    [Required(ErrorMessage = "Bitte die Fläche angeben.")]
    public decimal? Area { get; set; }

    public decimal? SecondaryArea { get; set; }
}
