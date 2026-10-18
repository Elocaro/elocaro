using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Personen;

namespace MietHahn.Application.Personen;

public sealed class PersonDaten
{
    public Anrede Anrede { get; set; }

    [StringLength(50)]
    public string? Titel { get; set; }

    [StringLength(100)]
    public string? Vorname { get; set; }

    [Required(ErrorMessage = "Bitte den Nachnamen bzw. Firmennamen angeben.")]
    [StringLength(200)]
    public string Nachname { get; set; } = "";

    [StringLength(200)]
    public string? Firma { get; set; }

    public Kontaktart Kontaktart { get; set; }

    [StringLength(100)]
    public string? Taetigkeit { get; set; }

    public DateOnly? Geburtsdatum { get; set; }

    [OptionaleEmail]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Telefon { get; set; }

    [StringLength(50)]
    public string? Mobil { get; set; }

    [StringLength(4000)]
    public string? Notizen { get; set; }
}

public sealed class AnschriftEingabe
{
    [Required(ErrorMessage = "Bitte angeben, ab wann die Anschrift gilt.")]
    public DateOnly? Ab { get; set; }

    [StringLength(200)]
    public string? Zusatz { get; set; }

    [Required(ErrorMessage = "Bitte Straße und Hausnummer angeben.")]
    [StringLength(200)]
    public string Strasse { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Postleitzahl angeben.")]
    [StringLength(10)]
    public string Plz { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Ort angeben.")]
    [StringLength(100)]
    public string Ort { get; set; } = "";

    [StringLength(100)]
    public string? Land { get; set; }
}

public sealed class BankverbindungEingabe
{
    [Required(ErrorMessage = "Bitte die IBAN angeben.")]
    [Iban]
    public string Iban { get; set; } = "";

    [StringLength(200)]
    public string? Kontoinhaber { get; set; }

    [StringLength(11)]
    public string? Bic { get; set; }
}
