using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Gemeinsam;

namespace MietHahn.Application.Verwaltung;

/// <summary>Pflege der Absenderangaben (Briefkopf, Fußzeile, Bankverbindung).</summary>
public interface IVermieterprofilVerwaltung
{
    Task<VermieterprofilDaten?> LadenAsync(CancellationToken cancellationToken = default);

    Task SpeichernAsync(VermieterprofilDaten daten, CancellationToken cancellationToken = default);
}

public sealed class VermieterprofilDaten
{
    [Required(ErrorMessage = "Bitte den Namen angeben.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(200)]
    public string? Zusatz { get; set; }

    [Required(ErrorMessage = "Bitte Straße und Hausnummer angeben.")]
    [StringLength(200)]
    public string Strasse { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Postleitzahl angeben.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Die Postleitzahl hat 5 Ziffern.")]
    public string Plz { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Ort angeben.")]
    [StringLength(100)]
    public string Ort { get; set; } = "";

    [StringLength(50)]
    public string? Telefon { get; set; }

    [OptionaleEmail]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(42)]
    public string? Iban { get; set; }

    [StringLength(11)]
    public string? Bic { get; set; }

    [StringLength(100)]
    public string? Bank { get; set; }

    [StringLength(30)]
    public string? Steuernummer { get; set; }
}
