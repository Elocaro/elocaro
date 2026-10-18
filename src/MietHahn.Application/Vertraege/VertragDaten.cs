using System.ComponentModel.DataAnnotations;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Application.Vertraege;

/// <summary>Neuer Mietvertrag mit den Anfangswerten.</summary>
public sealed class VertragAnlage
{
    [Required(ErrorMessage = "Bitte das Mietobjekt wählen.")]
    public Guid? MietobjektId { get; set; }

    [Required(ErrorMessage = "Bitte den Mietbeginn angeben.")]
    public DateOnly? Beginn { get; set; }

    /// <summary>Nur bei Zeitmietverträgen (§ 575 BGB).</summary>
    public DateOnly? BefristetBis { get; set; }

    [MinLength(1, ErrorMessage = "Bitte mindestens einen Mieter wählen.")]
    public List<Guid> MieterIds { get; set; } = [];

    public List<Guid> BuergenIds { get; set; } = [];

    public Abrechnungsart Abrechnungsart { get; set; }

    public bool HeizkostenVerbrauchsabhaengig { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte eine Kaltmiete angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Kaltmiete { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? BetriebskostenVorauszahlung { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? HeizkostenVorauszahlung { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? Betriebskostenpauschale { get; set; }

    [Range(1, 20, ErrorMessage = "Bitte eine Personenanzahl zwischen 1 und 20 angeben.")]
    public int Personen { get; set; } = 1;

    public VertragDaten Daten { get; set; } = new();
}

/// <summary>Nicht zeitabhängige Vertragsangaben.</summary>
public sealed class VertragDaten
{
    [Range(1, 10, ErrorMessage = "Bitte einen Werktag zwischen 1 und 10 angeben.")]
    public int FaelligAmWerktag { get; set; } = 3;

    public Zahlungsart Zahlungsart { get; set; }

    [Range(1, 24, ErrorMessage = "Bitte eine Kündigungsfrist zwischen 1 und 24 Monaten angeben.")]
    public int KuendigungsfristMonate { get; set; } = 3;

    public Abrechnungsart Abrechnungsart { get; set; }

    public bool HeizkostenVerbrauchsabhaengig { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? Kaution { get; set; }

    [StringLength(1000)]
    public string? KautionNotiz { get; set; }

    [StringLength(4000)]
    public string? Notizen { get; set; }
}

public sealed class BestandteilEingabe
{
    public Bestandteilart Art { get; set; }

    [Required(ErrorMessage = "Bitte angeben, ab wann der Betrag gilt.")]
    public DateOnly? Ab { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Betrag { get; set; }

    [StringLength(100)]
    public string? Bezeichnung { get; set; }
}

public sealed class PersonenEingabe
{
    [Required(ErrorMessage = "Bitte angeben, ab wann die Anzahl gilt.")]
    public DateOnly? Ab { get; set; }

    [Range(1, 20, ErrorMessage = "Bitte eine Personenanzahl zwischen 1 und 20 angeben.")]
    public int Anzahl { get; set; } = 1;
}

public sealed class KuendigungEingabe
{
    [Required(ErrorMessage = "Bitte das Eingangsdatum der Kündigung angeben.")]
    public DateOnly? EingegangenAm { get; set; }

    public Kuendigender Durch { get; set; }

    /// <summary>Leer = gesetzliche Frist berechnen (§ 573c BGB).</summary>
    public DateOnly? Zum { get; set; }
}

/// <summary>Mieterwechsel, Teil 1: Auszug (Vertragsende, neue Anschriften der Mieter).</summary>
public sealed class AuszugEingabe
{
    [Required(ErrorMessage = "Bitte den letzten Tag des Mietverhältnisses angeben.")]
    public DateOnly? Ende { get; set; }

    /// <summary>Neue Anschrift für alle Mieter (für die Zustellung der Abrechnung), optional.</summary>
    [StringLength(200)]
    public string? NeueStrasse { get; set; }

    [StringLength(10)]
    public string? NeuePlz { get; set; }

    [StringLength(100)]
    public string? NeuerOrt { get; set; }
}
