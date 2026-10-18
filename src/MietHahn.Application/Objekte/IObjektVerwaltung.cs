using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Objekte;

namespace MietHahn.Application.Objekte;

/// <summary>Pflege von Objekten, Einheiten und WG-Zimmern samt Flächen mit Gültigkeit.</summary>
public interface IObjektVerwaltung
{
    /// <param name="nurIds">Nur diese Objekte (z. B. die eines Hausmeisters); <c>null</c> = alle.</param>
    Task<IReadOnlyList<ObjektUebersicht>> ListeAsync(IReadOnlyCollection<Guid>? nurIds = null, CancellationToken cancellationToken = default);

    Task<ObjektDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> AnlegenAsync(ObjektDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> AendernAsync(Guid id, ObjektDaten daten, CancellationToken cancellationToken = default);

    /// <summary>Setzt das Foto des Hauses (Datei aus der Dateiablage) oder entfernt es.</summary>
    Task<Ergebnis> BildSetzenAsync(Guid id, Guid? dateiId, CancellationToken cancellationToken = default);

    Task<Ergebnis> FlaecheSetzenAsync(Guid id, FlaechenEingabe eingabe, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Einheiten möglich.</summary>
    Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> EinheitAnlegenAsync(Guid objektId, EinheitDaten daten, CancellationToken cancellationToken = default);

    Task<EinheitDetails?> EinheitLadenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> EinheitAendernAsync(Guid id, EinheitDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> EinheitFlaecheSetzenAsync(Guid id, FlaechenEingabe eingabe, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Zimmer und Verträge möglich.</summary>
    Task<Ergebnis> EinheitLoeschenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> ZimmerAnlegenAsync(Guid einheitId, ZimmerDaten daten, CancellationToken cancellationToken = default);

    Task<ZimmerDetails?> ZimmerLadenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> ZimmerAendernAsync(Guid id, ZimmerDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> ZimmerFlaecheSetzenAsync(Guid id, FlaechenEingabe eingabe, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Verträge möglich.</summary>
    Task<Ergebnis> ZimmerLoeschenAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record ObjektUebersicht(Guid Id, string Bezeichnung, string Anschrift, int Einheiten, int Mietobjekte, int Vermietet, decimal? Wohnflaeche)
{
    public int Leerstand => Mietobjekte - Vermietet;
}

/// <summary>Ein Flächenstand; <see cref="Zusatz"/> je nach Ebene Gemeinschafts-, Heiz- bzw. Anteilsfläche.</summary>
public sealed record FlaechenStand(Zeitraum Gueltigkeit, decimal Flaeche, decimal Zusatz);

/// <summary>Was vermietet werden kann: eine Einheit ohne Zimmer oder ein WG-Zimmer.</summary>
public sealed record MietobjektZeile(Guid Id, string Bezeichnung, bool IstZimmer, decimal? Flaeche, Belegung? Belegung);

public sealed record EinheitZeile(
    Guid Id,
    string Bezeichnung,
    string? Lage,
    Einheitstyp Typ,
    decimal? Wohnflaeche,
    bool IstWg,
    IReadOnlyList<MietobjektZeile> Mietobjekte);

public sealed record ObjektDetails(
    Guid Id,
    ObjektDaten Daten,
    string Anschrift,
    Guid? BildDateiId,
    FlaechenStand? Flaeche,
    IReadOnlyList<FlaechenStand> Flaechenhistorie,
    IReadOnlyList<EinheitZeile> Einheiten,
    IReadOnlyList<string> Pruefhinweise);

public sealed record ZimmerZeile(Guid Id, string Bezeichnung, Moeblierung Moeblierung, FlaechenStand? Flaeche, Belegung? Belegung);

public sealed record EinheitDetails(
    Guid Id,
    Guid ObjektId,
    string ObjektBezeichnung,
    EinheitDaten Daten,
    FlaechenStand? Flaeche,
    IReadOnlyList<FlaechenStand> Flaechenhistorie,
    IReadOnlyList<ZimmerZeile> Zimmer,
    Belegung? Belegung,
    IReadOnlyList<string> Pruefhinweise)
{
    public bool IstWg => Zimmer.Count > 0;
}

public sealed record ZimmerDetails(
    Guid Id,
    Guid EinheitId,
    string EinheitBezeichnung,
    Guid ObjektId,
    string ObjektBezeichnung,
    ZimmerDaten Daten,
    FlaechenStand? Flaeche,
    IReadOnlyList<FlaechenStand> Flaechenhistorie,
    Belegung? Belegung);
