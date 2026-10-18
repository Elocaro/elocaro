using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Objekte;

public enum Moeblierung
{
    Unmoebliert = 0,
    Teilmoebliert = 1,
    Moebliert = 2,
}

/// <summary>Einzeln vermietetes Zimmer einer WG-<see cref="Einheit"/>.</summary>
public sealed class Zimmer : Entitaet
{
    private Zimmer()
    {
        Bezeichnung = "";
    }

    internal Zimmer(Guid einheitId, string bezeichnung, int reihenfolge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bezeichnung);
        EinheitId = einheitId;
        Bezeichnung = bezeichnung;
        Reihenfolge = reihenfolge;
    }

    public Guid EinheitId { get; private set; }

    /// <summary>z. B. „Zimmer 1“ oder „Zimmer Nord“.</summary>
    public string Bezeichnung { get; set; }

    public int Reihenfolge { get; set; }

    public Moeblierung Moeblierung { get; set; }

    public string? Notizen { get; set; }

    public List<ZimmerFlaeche> Flaechen { get; private set; } = [];

    /// <summary>Zimmerfläche und anteilige Gemeinschaftsfläche der WG (Küche, Bad, Flur) ab dem Stichtag.</summary>
    public ZimmerFlaeche FlaecheSetzen(DateOnly ab, decimal flaeche, decimal anteilGemeinschaftsflaeche = 0) =>
        Zeitreihe.Fortschreiben(Flaechen, ab, g => new ZimmerFlaeche(Id, g, flaeche, anteilGemeinschaftsflaeche));

    public ZimmerFlaeche? FlaecheAm(DateOnly tag) => Zeitreihe.Am(Flaechen, tag);
}

public sealed class ZimmerFlaeche : Entitaet, IZeitabhaengig
{
    private ZimmerFlaeche()
    {
    }

    internal ZimmerFlaeche(Guid zimmerId, Zeitraum gueltigkeit, decimal flaeche, decimal anteilGemeinschaftsflaeche)
    {
        Flaechenwert.Pruefen(flaeche, "Zimmerfläche");
        Flaechenwert.Pruefen(anteilGemeinschaftsflaeche, "anteilige Gemeinschaftsfläche", nullErlaubt: true);
        ZimmerId = zimmerId;
        Gueltigkeit = gueltigkeit;
        Flaeche = flaeche;
        AnteilGemeinschaftsflaeche = anteilGemeinschaftsflaeche;
    }

    public Guid ZimmerId { get; private set; }

    public Zeitraum Gueltigkeit { get; private set; }

    public decimal Flaeche { get; private set; }

    /// <summary>Auf das Zimmer entfallender Teil der WG-Gemeinschaftsflächen in m².</summary>
    public decimal AnteilGemeinschaftsflaeche { get; private set; }

    /// <summary>Fläche für Umlagen nach Fläche: Zimmer + Anteil an Gemeinschaftsflächen.</summary>
    public decimal Umlageflaeche => Flaeche + AnteilGemeinschaftsflaeche;

    public void GueltigkeitBeenden(DateOnly letzterTag) => Gueltigkeit = Gueltigkeit.BeendenZum(letzterTag);
}
