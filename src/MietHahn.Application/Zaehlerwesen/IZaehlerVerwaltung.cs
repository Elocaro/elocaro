using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Zaehlerwesen;

namespace MietHahn.Application.Zaehlerwesen;

/// <summary>Zähler eines Objekts, Zählerstände (mit Foto), Zählertausch und Verbrauch.</summary>
public interface IZaehlerVerwaltung
{
    Task<IReadOnlyList<ZaehlerZeile>> ListeAsync(Guid objektId, bool mitAusgebauten = false, CancellationToken cancellationToken = default);

    Task<ZaehlerDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> AnlegenAsync(Guid objektId, ZaehlerAnlage anlage, CancellationToken cancellationToken = default);

    Task<Ergebnis> AendernAsync(Guid id, ZaehlerDaten daten, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erfasst einen Stand. Bei Auffälligkeiten (Rückwärtslauf, Ausreißer) wird ohne <see cref="StandEingabe.Bestaetigt"/>
    /// nicht gespeichert, sondern die Warnungen kommen zurück.
    /// </summary>
    Task<StandErgebnis> StandErfassenAsync(Guid id, StandEingabe eingabe, Guid? fotoDateiId = null, CancellationToken cancellationToken = default);

    Task<Ergebnis> StandEntfernenAsync(Guid id, Guid standId, CancellationToken cancellationToken = default);

    /// <summary>Baut den Zähler aus und legt den neuen Zähler der Messstelle an (liefert dessen Id).</summary>
    Task<Ergebnis> TauschenAsync(Guid id, TauschEingabe eingabe, CancellationToken cancellationToken = default);

    /// <summary>Aktive Zähler in Ablesereihenfolge, optional nur einer Einheit (z. B. bei Mieterwechsel).</summary>
    Task<IReadOnlyList<ZaehlerZeile>> RundeAsync(Guid objektId, Guid? einheitId = null, CancellationToken cancellationToken = default);

    /// <summary>Verbrauch der Messstelle (über Zählertausch hinweg) im Zeitraum.</summary>
    Task<Verbrauch?> VerbrauchAsync(Guid id, Zeitraum zeitraum, CancellationToken cancellationToken = default);
}

public sealed record StandErgebnis(bool Erfolgreich, IReadOnlyList<string> Fehler, IReadOnlyList<string> Warnungen)
{
    public bool BrauchtBestaetigung => !Erfolgreich && Warnungen.Count > 0 && Fehler.Count == 0;
}

public sealed record StandAnsicht(Guid Id, DateOnly Datum, decimal Wert, Standart Art, string? Ableser, Guid? FotoDateiId, string? Notiz);

public sealed record ZaehlerZeile(
    Guid Id,
    string Bezeichnung,
    Zaehlertyp Typ,
    string Nummer,
    Messeinheit Messeinheit,
    string Zuordnung,
    Guid? EinheitId,
    string? Lage,
    StandAnsicht? LetzterStand,
    DateOnly? EichfristBis,
    DateOnly EingebautAm,
    DateOnly? AusgebautAm,
    Guid? HauptzaehlerId,
    int Reihenfolge);

public sealed record Jahresverbrauch(int Jahr, Verbrauch Verbrauch);

/// <summary>Differenz Hauptzähler − Summe der Unterzähler (Allgemeinverbrauch, Schwund) für ein Jahr.</summary>
public sealed record Unterzaehlerbilanz(int Jahr, decimal? Hauptzaehler, decimal? SummeUnterzaehler, decimal? Differenz, IReadOnlyList<string> Hinweise);

public sealed record ZaehlerDetails(
    ZaehlerZeile Zaehler,
    Guid ObjektId,
    string ObjektBezeichnung,
    ZaehlerDaten Daten,
    IReadOnlyList<StandAnsicht> Staende,
    IReadOnlyList<ZaehlerZeile> Messstelle,
    IReadOnlyList<Jahresverbrauch> Jahresverbraeuche,
    IReadOnlyList<ZaehlerZeile> Unterzaehler,
    Unterzaehlerbilanz? Bilanz);
