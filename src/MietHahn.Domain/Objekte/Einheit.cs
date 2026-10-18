using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Objekte;

public enum Einheitstyp
{
    Wohnung = 0,
    Gewerbe = 1,
    Stellplatz = 2,
    Sonstiges = 9,
}

/// <summary>
/// Wohnung (bzw. Gewerbe, Stellplatz) in einem <see cref="Objekt"/>. Ohne Zimmer ist die Einheit selbst vermietbar;
/// mit Zimmern (WG) werden nur die Zimmer einzeln vermietet.
/// </summary>
public sealed class Einheit : Entitaet
{
    private Einheit()
    {
        Bezeichnung = "";
    }

    internal Einheit(Guid objektId, string bezeichnung, Einheitstyp typ, int reihenfolge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bezeichnung);
        ObjektId = objektId;
        Bezeichnung = bezeichnung;
        Typ = typ;
        Reihenfolge = reihenfolge;
    }

    public Guid ObjektId { get; private set; }

    /// <summary>Nummer oder Name, z. B. „Wohnung 1“.</summary>
    public string Bezeichnung { get; set; }

    /// <summary>Lage im Haus, z. B. „EG links“.</summary>
    public string? Lage { get; set; }

    public Einheitstyp Typ { get; set; }

    /// <summary>Anzeigereihenfolge im Objekt.</summary>
    public int Reihenfolge { get; set; }

    /// <summary>Anzahl Zimmer laut Exposé/Mietvertrag, z. B. 2,5.</summary>
    public decimal? Zimmeranzahl { get; set; }

    // Ausstattung (Mietspiegel-Merkmale)
    public string? Bad { get; set; }

    public bool Balkon { get; set; }

    public bool Einbaukueche { get; set; }

    /// <summary>z. B. Energieeffizienzklasse oder „KfW 70“.</summary>
    public string? Energiestandard { get; set; }

    public string? Ausstattung { get; set; }

    public List<Zimmer> Zimmer { get; private set; } = [];

    public List<EinheitFlaeche> Flaechen { get; private set; } = [];

    /// <summary>WG: Die Zimmer werden einzeln vermietet.</summary>
    public bool IstWg => Zimmer.Count > 0;

    public bool IstSelbstVermietbar => !IstWg;

    public Zimmer ZimmerHinzufuegen(string bezeichnung)
    {
        if (Zimmer.Any(z => string.Equals(z.Bezeichnung, bezeichnung.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new RegelverletzungException($"In der Einheit gibt es bereits ein Zimmer „{bezeichnung.Trim()}“.");
        }

        var zimmer = new Zimmer(Id, bezeichnung.Trim(), Zimmer.Count == 0 ? 1 : Zimmer.Max(z => z.Reihenfolge) + 1);
        Zimmer.Add(zimmer);
        return zimmer;
    }

    /// <summary>Wohn- und Heizfläche ab dem Stichtag. Ohne Angabe gilt die Heizfläche = Wohnfläche.</summary>
    public EinheitFlaeche FlaecheSetzen(DateOnly ab, decimal wohnflaeche, decimal? heizflaeche = null) =>
        Zeitreihe.Fortschreiben(Flaechen, ab, g => new EinheitFlaeche(Id, g, wohnflaeche, heizflaeche ?? wohnflaeche));

    public EinheitFlaeche? FlaecheAm(DateOnly tag) => Zeitreihe.Am(Flaechen, tag);

    public IEnumerable<string> Pruefhinweise(DateOnly tag)
    {
        var flaeche = FlaecheAm(tag);
        if (flaeche is null)
        {
            yield return $"{Bezeichnung}: Keine Wohnfläche zum {tag:dd.MM.yyyy} hinterlegt.";
            yield break;
        }

        var zimmer = Zimmer.Sum(z => (z.FlaecheAm(tag)?.Flaeche ?? 0) + (z.FlaecheAm(tag)?.AnteilGemeinschaftsflaeche ?? 0));
        if (IstWg && zimmer > flaeche.Wohnflaeche)
        {
            yield return $"{Bezeichnung}: Die Zimmerflächen inkl. Anteilen an Gemeinschaftsflächen ({Objekt.Qm(zimmer)}) " +
                $"sind größer als die Wohnfläche ({Objekt.Qm(flaeche.Wohnflaeche)}).";
        }
    }
}

public sealed class EinheitFlaeche : Entitaet, IZeitabhaengig
{
    private EinheitFlaeche()
    {
    }

    internal EinheitFlaeche(Guid einheitId, Zeitraum gueltigkeit, decimal wohnflaeche, decimal heizflaeche)
    {
        Flaechenwert.Pruefen(wohnflaeche, "Wohnfläche");
        Flaechenwert.Pruefen(heizflaeche, "Heizfläche", nullErlaubt: true);
        EinheitId = einheitId;
        Gueltigkeit = gueltigkeit;
        Wohnflaeche = wohnflaeche;
        Heizflaeche = heizflaeche;
    }

    public Guid EinheitId { get; private set; }

    public Zeitraum Gueltigkeit { get; private set; }

    /// <summary>Wohnfläche nach WoFlV in m² (Umlage nach Fläche).</summary>
    public decimal Wohnflaeche { get; private set; }

    /// <summary>Beheizte Fläche in m² (Grundkosten der Heizkostenabrechnung), kann von der Wohnfläche abweichen.</summary>
    public decimal Heizflaeche { get; private set; }

    public void GueltigkeitBeenden(DateOnly letzterTag) => Gueltigkeit = Gueltigkeit.BeendenZum(letzterTag);
}
