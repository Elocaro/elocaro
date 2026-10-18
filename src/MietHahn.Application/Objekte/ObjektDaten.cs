using System.ComponentModel.DataAnnotations;
using MietHahn.Domain.Objekte;

namespace MietHahn.Application.Objekte;

/// <summary>Formular- und Anzeigedaten eines Objekts.</summary>
public sealed class ObjektDaten
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben.")]
    [StringLength(200)]
    public string Bezeichnung { get; set; } = "";

    [Required(ErrorMessage = "Bitte Straße und Hausnummer angeben.")]
    [StringLength(200)]
    public string Strasse { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Postleitzahl angeben.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Die Postleitzahl hat 5 Ziffern.")]
    public string Plz { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Ort angeben.")]
    [StringLength(100)]
    public string Ort { get; set; } = "";

    [Range(1500, 2100, ErrorMessage = "Bitte ein Baujahr zwischen 1500 und 2100 angeben.")]
    public int? Baujahr { get; set; }

    [StringLength(100)]
    public string? Gemarkung { get; set; }

    [StringLength(50)]
    public string? Flurstueck { get; set; }

    [StringLength(50)]
    public string? GrundsteuerAktenzeichen { get; set; }

    public Heizungsart Heizungsart { get; set; }

    public Energietraeger Energietraeger { get; set; }

    [StringLength(4000)]
    public string? Notizen { get; set; }
}

public sealed class EinheitDaten
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben, z. B. „Wohnung 1“.")]
    [StringLength(100)]
    public string Bezeichnung { get; set; } = "";

    [StringLength(100)]
    public string? Lage { get; set; }

    public Einheitstyp Typ { get; set; }

    [Range(typeof(decimal), "0.5", "50", ErrorMessage = "Bitte eine Zimmeranzahl zwischen 0,5 und 50 angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Zimmeranzahl { get; set; }

    [StringLength(100)]
    public string? Bad { get; set; }

    public bool Balkon { get; set; }

    public bool Einbaukueche { get; set; }

    [StringLength(50)]
    public string? Energiestandard { get; set; }

    [StringLength(2000)]
    public string? Ausstattung { get; set; }
}

public sealed class ZimmerDaten
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben, z. B. „Zimmer 1“.")]
    [StringLength(100)]
    public string Bezeichnung { get; set; } = "";

    public Moeblierung Moeblierung { get; set; }

    [StringLength(2000)]
    public string? Notizen { get; set; }
}

/// <summary>
/// Neuer Flächenstand ab Stichtag. Bedeutung je Ebene: Objekt = Gesamt-/Gemeinschaftsfläche,
/// Einheit = Wohn-/Heizfläche (leer = wie Wohnfläche), Zimmer = Zimmerfläche/Anteil Gemeinschaftsfläche.
/// </summary>
public sealed class FlaechenEingabe
{
    [Required(ErrorMessage = "Bitte angeben, ab wann die Fläche gilt.")]
    public DateOnly? Ab { get; set; }

    [Required(ErrorMessage = "Bitte die Fläche angeben.")]
    public decimal? Flaeche { get; set; }

    public decimal? Zusatz { get; set; }
}
