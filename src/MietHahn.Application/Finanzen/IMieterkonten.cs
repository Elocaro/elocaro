using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Finanzen;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Application.Finanzen;

/// <summary>Sollstellungen, Zahlungen und Offene Posten je Mietverhältnis.</summary>
public interface IMieterkonten
{
    Task<Mieterkonto?> LadenAsync(Guid mietvertragId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erzeugt und gleicht die Monatsmieten ab dem Buchhaltungsbeginn bis zum laufenden Monat an (idempotent).
    /// Ohne Vertrag für alle Verträge. Liefert die Anzahl neuer, geänderter und entfernter Sollstellungen.
    /// </summary>
    Task<int> SollstellungenErzeugenAsync(Guid? mietvertragId = null, CancellationToken cancellationToken = default);

    Task<Ergebnis> SollstellungAnlegenAsync(Guid mietvertragId, SollstellungEingabe eingabe, CancellationToken cancellationToken = default);

    /// <summary>Nur von Hand angelegte Sollstellungen; Monatsmieten ergeben sich aus dem Vertrag.</summary>
    Task<Ergebnis> SollstellungLoeschenAsync(Guid sollstellungId, CancellationToken cancellationToken = default);

    Task<Ergebnis> ZahlungErfassenAsync(Guid mietvertragId, ZahlungEingabe eingabe, CancellationToken cancellationToken = default);

    /// <summary>Nur von Hand erfasste Zahlungen; Bankumsätze werden über die Zuordnung gelöst.</summary>
    Task<Ergebnis> ZahlungLoeschenAsync(Guid zahlungId, CancellationToken cancellationToken = default);

    /// <summary>Fällige, nicht (vollständig) bezahlte Forderungen über alle Mietverhältnisse.</summary>
    Task<IReadOnlyList<OffenerPosten>> OffenePostenAsync(CancellationToken cancellationToken = default);
}

public interface IFinanzeinstellungen
{
    Task<FinanzeinstellungenDaten> LadenAsync(CancellationToken cancellationToken = default);

    Task SpeichernAsync(FinanzeinstellungenDaten daten, CancellationToken cancellationToken = default);
}

public sealed class FinanzeinstellungenDaten
{
    /// <summary>Erster Monat mit automatischen Monatsmieten; frühere Rückstände als Saldovortrag erfassen.</summary>
    public DateOnly? BuchhaltungAb { get; set; }

    public Verrechnungsreihenfolge Verrechnung { get; set; }
}

public sealed class SollstellungEingabe
{
    public Postenart Art { get; set; } = Postenart.Nachzahlung;

    [Required(ErrorMessage = "Bitte das Fälligkeitsdatum angeben.")]
    public DateOnly? Faellig { get; set; }

    /// <summary>Immer positiv eingeben; Erstattungen werden als Guthaben gebucht.</summary>
    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Bitte einen Betrag über 0 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Betrag { get; set; }

    [StringLength(200)]
    public string? Bezeichnung { get; set; }

    [StringLength(1000)]
    public string? Notiz { get; set; }
}

public sealed class ZahlungEingabe
{
    [Required(ErrorMessage = "Bitte das Zahlungsdatum angeben.")]
    public DateOnly? Datum { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Bitte einen Betrag über 0 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Betrag { get; set; }

    /// <summary>Auszahlung an den Mieter (z. B. Guthaben überwiesen) statt Zahlungseingang.</summary>
    public bool Auszahlung { get; set; }

    [StringLength(500)]
    public string? Verwendungszweck { get; set; }

    /// <summary>Tilgungsbestimmung: diese Forderung zuerst.</summary>
    public Guid? SollstellungId { get; set; }
}

public enum Buchungsart
{
    Sollstellung = 0,
    Zahlung = 1,
}

/// <summary>Eine Zeile im Mieterkonto; <see cref="Soll"/> und <see cref="Haben"/> sind positiv.</summary>
public sealed record Kontobuchung(
    Guid Id,
    Buchungsart Buchungsart,
    DateOnly Datum,
    string Text,
    Postenart? Art,
    decimal Soll,
    decimal Haben,
    decimal Saldo,
    decimal? Offen,
    bool Automatisch,
    Zahlungsherkunft? Herkunft);

/// <summary>Je Jahr und Mietbestandteil: Soll nach Leistungsmonat, davon getilgt, und Zufluss nach Zahlungsdatum (§ 11 EStG).</summary>
public sealed record Jahreswert(int Jahr, Bestandteilart Bestandteil, decimal Soll, decimal Getilgt, decimal Zufluss);

/// <param name="Saldo">Positiv = Rückstand des Mieters, negativ = Guthaben.</param>
public sealed record Mieterkonto(
    Guid MietvertragId,
    string Mietobjekt,
    string Mieter,
    DateOnly? BuchhaltungAb,
    IReadOnlyList<Kontobuchung> Buchungen,
    decimal Saldo,
    decimal Faellig,
    IReadOnlyList<Jahreswert> Jahreswerte,
    Verrechnungsreihenfolge Verrechnung);

public sealed record OffenerPosten(
    Guid SollstellungId,
    Guid MietvertragId,
    string Mietobjekt,
    string Mieter,
    Postenart Art,
    string Text,
    DateOnly Faellig,
    decimal Betrag,
    decimal Offen,
    int TageUeberfaellig);
