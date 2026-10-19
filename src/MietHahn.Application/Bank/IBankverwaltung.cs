using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Bank;

namespace MietHahn.Application.Bank;

/// <summary>Bankkonten, Datei-Import (camt.053, MT940, CSV) und Umsätze.</summary>
public interface IBankverwaltung
{
    Task<IReadOnlyList<BankkontoZeile>> KontenAsync(CancellationToken cancellationToken = default);

    Task<BankkontoDaten?> KontoLadenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> KontoAnlegenAsync(BankkontoDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> KontoAendernAsync(Guid id, BankkontoDaten daten, CancellationToken cancellationToken = default);

    /// <summary>Liest die Datei, speichert sie bis zur Bestätigung und liefert die Id der Vorschau.</summary>
    Task<Ergebnis> VorschauAnlegenAsync(Guid bankkontoId, string dateiname, byte[] inhalt, string? profil = null, CancellationToken cancellationToken = default);

    Task<ImportVorschau?> VorschauAsync(Guid bankimportId, CancellationToken cancellationToken = default);

    /// <summary>Übernimmt die neuen Umsätze; bereits vorhandene (gleicher Fingerabdruck) werden übersprungen.</summary>
    Task<Ergebnis> ImportierenAsync(Guid bankimportId, CancellationToken cancellationToken = default);

    Task<Ergebnis> VerwerfenAsync(Guid bankimportId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportZeile>> ImporteAsync(Guid bankkontoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UmsatzZeile>> UmsaetzeAsync(UmsatzSuche suche, CancellationToken cancellationToken = default);

    Task<Ergebnis> IgnorierenAsync(Guid umsatzId, string? notiz, CancellationToken cancellationToken = default);

    Task<Ergebnis> WiederOeffnenAsync(Guid umsatzId, CancellationToken cancellationToken = default);

    /// <summary>Mitgelieferte und eigene CSV-Profile.</summary>
    Task<IReadOnlyList<CsvProfilDaten>> CsvProfileAsync(CancellationToken cancellationToken = default);

    Task<Ergebnis> CsvProfilSpeichernAsync(Guid? id, CsvProfilDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> CsvProfilLoeschenAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class BankkontoDaten
{
    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Bitte die IBAN angeben.")]
    [StringLength(42)]
    public string Iban { get; set; } = "";

    [StringLength(11)]
    public string? Bic { get; set; }

    [StringLength(100)]
    public string? Bank { get; set; }

    public Kontotyp Typ { get; set; }

    public List<Guid> ObjektIds { get; set; } = [];
}

public sealed record BankkontoZeile(Guid Id, string Name, string Iban, string? Bank, Kontotyp Typ, IReadOnlyList<string> Objekte, int OffeneUmsaetze, DateOnly? LetzterUmsatz);

/// <param name="AbweichendeIban">Die Datei gehört laut ihrer Kontoangabe zu einer anderen IBAN.</param>
public sealed record ImportVorschau(
    Guid Id,
    Guid BankkontoId,
    string Konto,
    string Dateiname,
    Bankformat Format,
    string? Profil,
    Importstatus Status,
    string? AbweichendeIban,
    IReadOnlyList<VorschauZeile> Zeilen,
    IReadOnlyList<string> Hinweise);

public sealed record VorschauZeile(GelesenerUmsatz Umsatz, bool Duplikat);

public sealed record ImportZeile(Guid Id, DateTimeOffset Zeitpunkt, string? Von, string Dateiname, Bankformat Format, string? Profil, Importstatus Status, int Gelesen, int Neu, int Duplikate, DateOnly? Ab, DateOnly? Bis);

public sealed record UmsatzSuche(Guid? BankkontoId = null, Umsatzstatus? Status = null, string? Text = null);

public sealed record UmsatzZeile(
    Guid Id,
    Guid BankkontoId,
    DateOnly Buchungsdatum,
    DateOnly? Valutadatum,
    decimal Betrag,
    string? GegenName,
    string? GegenIban,
    string Verwendungszweck,
    string? Buchungstext,
    string? EndToEndId,
    string? Mandatsreferenz,
    string? GlaeubigerId,
    Umsatzstatus Status,
    string? Notiz);

public sealed class CsvProfilDaten
{
    public Guid? Id { get; set; }

    public bool Vorgabe { get; set; }

    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(1)]
    public string Trennzeichen { get; set; } = ";";

    public string Zeichensatz { get; set; } = "auto";

    [Required]
    [StringLength(100)]
    public string Datumsformat { get; set; } = "dd.MM.yyyy|dd.MM.yy";

    [Required]
    [StringLength(1)]
    public string Dezimaltrennzeichen { get; set; } = ",";

    [Required(ErrorMessage = "Bitte die Spalte mit dem Buchungsdatum angeben.")]
    [StringLength(200)]
    public string SpalteBuchungsdatum { get; set; } = "";

    [StringLength(200)]
    public string? SpalteValutadatum { get; set; }

    [StringLength(200)]
    public string? SpalteBetrag { get; set; }

    [StringLength(200)]
    public string? SpalteSoll { get; set; }

    [StringLength(200)]
    public string? SpalteHaben { get; set; }

    [StringLength(200)]
    public string? SpalteName { get; set; }

    [StringLength(200)]
    public string? SpalteIban { get; set; }

    [StringLength(200)]
    public string? SpalteBic { get; set; }

    [StringLength(200)]
    public string? SpalteVerwendungszweck { get; set; }

    [StringLength(200)]
    public string? SpalteBuchungstext { get; set; }

    [StringLength(200)]
    public string? SpalteEndToEnd { get; set; }

    [StringLength(200)]
    public string? SpalteMandatsreferenz { get; set; }

    [StringLength(200)]
    public string? SpalteGlaeubigerId { get; set; }
}
