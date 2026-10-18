using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Application.Vertraege;

/// <summary>Mietverträge mit zeitabhängigen Bestandteilen, Kündigung und Mieterwechsel.</summary>
public interface IVertragsVerwaltung
{
    Task<IReadOnlyList<VertragUebersicht>> ListeAsync(bool nurLaufende = true, CancellationToken cancellationToken = default);

    Task<VertragDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Alle vermietbaren Objekte (Einheiten ohne Zimmer, WG-Zimmer) für die Auswahl.</summary>
    Task<IReadOnlyList<MietobjektAuswahl>> MietobjekteAsync(CancellationToken cancellationToken = default);

    /// <summary>Vertragshistorie eines Mietobjekts (neueste zuerst), inkl. Leerstandszeiten dazwischen.</summary>
    Task<IReadOnlyList<VertragsHistorieEintrag>> HistorieAsync(Guid mietobjektId, CancellationToken cancellationToken = default);

    Task<Ergebnis> AnlegenAsync(VertragAnlage anlage, CancellationToken cancellationToken = default);

    Task<Ergebnis> AendernAsync(Guid id, VertragDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> BestandteilSetzenAsync(Guid id, BestandteilEingabe eingabe, CancellationToken cancellationToken = default);

    Task<Ergebnis> BestandteilBeendenAsync(Guid id, Bestandteilart art, DateOnly letzterTag, CancellationToken cancellationToken = default);

    Task<Ergebnis> PersonenSetzenAsync(Guid id, PersonenEingabe eingabe, CancellationToken cancellationToken = default);

    Task<Ergebnis> ParteiHinzufuegenAsync(Guid id, Guid personId, Parteirolle rolle, CancellationToken cancellationToken = default);

    Task<Ergebnis> ParteiEntfernenAsync(Guid id, Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Erfasst die Kündigung; ohne Termin wird er nach § 573c BGB berechnet.</summary>
    Task<Ergebnis> KuendigenAsync(Guid id, KuendigungEingabe eingabe, CancellationToken cancellationToken = default);

    Task<Ergebnis> KuendigungAufhebenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mieterwechsel: beendet den Vertrag und schreibt die neuen Anschriften der Mieter fort.</summary>
    Task<Ergebnis> AuszugAsync(Guid id, AuszugEingabe eingabe, CancellationToken cancellationToken = default);
}

public sealed record VertragUebersicht(
    Guid Id,
    string Mietobjekt,
    string Objekt,
    string Mieter,
    DateOnly Beginn,
    DateOnly? Ende,
    Vertragsstatus Status,
    decimal Monatsmiete,
    Abrechnungsart Abrechnungsart);

public sealed record MietobjektAuswahl(Guid Id, string Bezeichnung, string Objekt, bool IstZimmer, Belegt? AktuellBelegt);

/// <param name="Bis">Ende des laufenden Vertrags, <c>null</c> = unbefristet.</param>
public sealed record Belegt(string Mieter, DateOnly? Bis);

public sealed record VertragsPartei(Guid PersonId, string Name, Parteirolle Rolle, string? Email, string? Telefon);

public sealed record BestandteilStand(Guid Id, Bestandteilart Art, string? Bezeichnung, Zeitraum Gueltigkeit, decimal Betrag);

public sealed record PersonenStandAnsicht(Zeitraum Gueltigkeit, int Anzahl);

public sealed record VertragDetails(
    Guid Id,
    Guid MietobjektId,
    Guid EinheitId,
    Guid? ZimmerId,
    Guid ObjektId,
    string Mietobjekt,
    string Objekt,
    Zeitraum Laufzeit,
    DateOnly? BefristetBis,
    Vertragsstatus Status,
    DateOnly? KuendigungEingegangenAm,
    Kuendigender? KuendigungDurch,
    VertragDaten Daten,
    IReadOnlyList<VertragsPartei> Parteien,
    IReadOnlyList<BestandteilStand> AktuelleBestandteile,
    IReadOnlyList<BestandteilStand> Bestandteile,
    IReadOnlyList<PersonenStandAnsicht> Personen,
    decimal Monatsmiete,
    DateOnly Stichtag,
    DateOnly? NaechsteFaelligkeit,
    int KuendigungsfristVermieterMonate);

/// <summary>Ein Vertrag oder eine Leerstandszeit in der Historie eines Mietobjekts.</summary>
public sealed record VertragsHistorieEintrag(Guid? VertragId, Zeitraum Zeitraum, string? Mieter, decimal? Kaltmiete)
{
    public bool IstLeerstand => VertragId is null;
}
