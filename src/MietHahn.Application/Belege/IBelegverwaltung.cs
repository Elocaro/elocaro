using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Belege;

namespace MietHahn.Application.Belege;

/// <summary>Eingangsrechnungen mit Dateien und Aufteilung in Kostenpositionen.</summary>
public interface IBelegverwaltung
{
    Task<IReadOnlyList<BelegZeile>> SucheAsync(BelegSuche suche, CancellationToken cancellationToken = default);

    Task<BelegDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Legt den Beleg zu bereits gespeicherten Dateien an, optional gleich mit einer Position über den ganzen Betrag.</summary>
    Task<Ergebnis> AnlegenAsync(BelegDaten daten, IReadOnlyList<Guid> dateiIds, PositionEingabe? position = null, CancellationToken cancellationToken = default);

    Task<Ergebnis> AendernAsync(Guid id, BelegDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> DateienHinzufuegenAsync(Guid id, IReadOnlyList<Guid> dateiIds, CancellationToken cancellationToken = default);

    Task<Ergebnis> DateiEntfernenAsync(Guid id, Guid dateiId, CancellationToken cancellationToken = default);

    Task<Ergebnis> PositionHinzufuegenAsync(Guid id, PositionEingabe eingabe, CancellationToken cancellationToken = default);

    Task<Ergebnis> PositionEntfernenAsync(Guid id, Guid positionId, CancellationToken cancellationToken = default);

    Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<BelegAuswahl> AuswahlAsync(CancellationToken cancellationToken = default);
}

/// <param name="Jahr">Rechnungsdatum oder Leistungszeitraum im Jahr.</param>
/// <param name="OhneZahlung">Weder Zahlungsdatum noch zugeordneter Umsatz.</param>
public sealed record BelegSuche(
    int? Jahr = null,
    Guid? ObjektId = null,
    string? Kostenart = null,
    bool OhneZahlung = false,
    bool NichtAufgeteilt = false,
    string? Text = null);

public sealed class BelegDaten
{
    public Guid? LieferantId { get; set; }

    [StringLength(200)]
    public string? LieferantName { get; set; }

    [StringLength(100)]
    public string? Rechnungsnummer { get; set; }

    [Required(ErrorMessage = "Bitte das Rechnungsdatum angeben.")]
    public DateOnly? Rechnungsdatum { get; set; }

    public DateOnly? LeistungVon { get; set; }

    public DateOnly? LeistungBis { get; set; }

    [Required(ErrorMessage = "Bitte den Rechnungsbetrag (brutto) angeben.")]
    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Brutto { get; set; }

    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Netto { get; set; }

    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Umsatzsteuer { get; set; }

    public DateOnly? FaelligAm { get; set; }

    public DateOnly? BezahltAm { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Brennstoffmenge { get; set; }

    [StringLength(10)]
    public string? Brennstoffeinheit { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Co2Kg { get; set; }

    [StringLength(2000)]
    public string? Notiz { get; set; }
}

public sealed class PositionEingabe
{
    [Required(ErrorMessage = "Bitte die Kostenart wählen.")]
    public string Kostenart { get; set; } = "";

    /// <summary>Leer = noch nicht aufgeteilter Rest.</summary>
    [Range(typeof(decimal), "-1000000", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Betrag { get; set; }

    [Required(ErrorMessage = "Bitte das Objekt wählen.")]
    public Guid? ObjektId { get; set; }

    public Guid? EinheitId { get; set; }

    /// <summary>Leer = Leistungszeitraum bzw. Kalenderjahr des Rechnungsdatums.</summary>
    public DateOnly? Von { get; set; }

    public DateOnly? Bis { get; set; }

    /// <summary>Leer = laut Kostenart.</summary>
    public bool? Umlagefaehig { get; set; }

    public Paragraf35a Paragraf35a { get; set; }

    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Arbeitskosten { get; set; }

    [StringLength(500)]
    public string? Notiz { get; set; }
}

public sealed record BelegZeile(
    Guid Id,
    string Lieferant,
    string? Rechnungsnummer,
    DateOnly Rechnungsdatum,
    decimal Brutto,
    decimal Aufgeteilt,
    IReadOnlyList<string> Kostenarten,
    IReadOnlyList<string> Objekte,
    DateOnly? BezahltAm,
    int Dateien);

public sealed record BelegDetails(
    Guid Id,
    BelegDaten Daten,
    string Lieferant,
    IReadOnlyList<BelegDateiAnsicht> Dateien,
    IReadOnlyList<PositionAnsicht> Positionen,
    decimal Aufgeteilt,
    decimal Offen);

public sealed record BelegDateiAnsicht(Guid DateiId, string Name, string MimeTyp, int Seite);

public sealed record PositionAnsicht(
    Guid Id,
    string Kostenart,
    string KostenartText,
    decimal Betrag,
    string Objekt,
    string? Einheit,
    Domain.Gemeinsam.Zeitraum Abrechnungszeitraum,
    bool Umlagefaehig,
    Paragraf35a Paragraf35a,
    decimal? Arbeitskosten,
    string? Notiz);

public sealed record BelegAuswahl(IReadOnlyList<BezugOption> Lieferanten, IReadOnlyList<BezugOption> Objekte, IReadOnlyList<BezugOption> Einheiten);

public sealed record BezugOption(Guid Id, string Bezeichnung);
