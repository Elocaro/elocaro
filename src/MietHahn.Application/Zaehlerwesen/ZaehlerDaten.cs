using System.ComponentModel.DataAnnotations;
using MietHahn.Domain.Zaehlerwesen;

namespace MietHahn.Application.Zaehlerwesen;

public sealed class ZaehlerDaten
{
    public Zaehlertyp Typ { get; set; }

    public Messeinheit? Messeinheit { get; set; }

    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben, z. B. „Kaltwasser Bad“.")]
    [StringLength(200)]
    public string Bezeichnung { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Zählernummer angeben.")]
    [StringLength(50)]
    public string Nummer { get; set; } = "";

    [StringLength(200)]
    public string? Lage { get; set; }

    /// <summary>Einheit oder Zimmer, dem der Zähler zugeordnet ist; leer = Hauszähler.</summary>
    public Guid? ZuordnungId { get; set; }

    public Guid? HauptzaehlerId { get; set; }

    public DateOnly? EichfristBis { get; set; }

    [Range(0, 999)]
    public int Reihenfolge { get; set; }

    [StringLength(2000)]
    public string? Notizen { get; set; }
}

public sealed class ZaehlerAnlage
{
    public ZaehlerDaten Daten { get; set; } = new();

    [Required(ErrorMessage = "Bitte das Einbaudatum angeben.")]
    public DateOnly? EingebautAm { get; set; }

    [Required(ErrorMessage = "Bitte den Anfangsstand angeben.")]
    public decimal? Anfangsstand { get; set; }
}

public sealed class StandEingabe
{
    [Required(ErrorMessage = "Bitte das Ablesedatum angeben.")]
    public DateOnly? Datum { get; set; }

    [Required(ErrorMessage = "Bitte den Zählerstand angeben.")]
    public decimal? Wert { get; set; }

    public Standart Art { get; set; }

    [StringLength(1000)]
    public string? Notiz { get; set; }

    /// <summary>Warnungen (Rückwärtslauf, Ausreißer) bestätigt: trotzdem speichern.</summary>
    public bool Bestaetigt { get; set; }
}

public sealed class TauschEingabe
{
    [Required(ErrorMessage = "Bitte das Tauschdatum angeben.")]
    public DateOnly? Datum { get; set; }

    [Required(ErrorMessage = "Bitte den Endstand des alten Zählers angeben.")]
    public decimal? Endstand { get; set; }

    [Required(ErrorMessage = "Bitte die Nummer des neuen Zählers angeben.")]
    [StringLength(50)]
    public string NeueNummer { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Anfangsstand des neuen Zählers angeben.")]
    public decimal? Anfangsstand { get; set; }

    public DateOnly? EichfristBis { get; set; }
}
