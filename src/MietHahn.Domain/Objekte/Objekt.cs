using System.Globalization;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Objekte;

public enum Heizungsart
{
    Unbekannt = 0,
    Zentralheizung = 1,
    Etagenheizung = 2,
    Fernwaerme = 3,
    Waermepumpe = 4,
    Einzeloefen = 5,
    Sonstige = 9,
}

public enum Energietraeger
{
    Unbekannt = 0,
    Erdgas = 1,
    Heizoel = 2,
    Fernwaerme = 3,
    Strom = 4,
    Holz = 5,
    Fluessiggas = 6,
    Sonstige = 9,
}

/// <summary>
/// Ein Haus bzw. Grundstück. Aggregatwurzel für <see cref="Einheit"/>en und deren <see cref="Zimmer"/>.
/// Flächen sind zeitabhängig (<see cref="ObjektFlaeche"/>), damit Abrechnungen nach einem Umbau stimmen.
/// </summary>
public sealed class Objekt : Entitaet
{
    private Objekt()
    {
        Bezeichnung = "";
        Strasse = "";
        Plz = "";
        Ort = "";
    }

    public Objekt(string bezeichnung, string strasse, string plz, string ort)
    {
        Bezeichnung = bezeichnung;
        Strasse = strasse;
        Plz = plz;
        Ort = ort;
        Pruefen();
    }

    /// <summary>Kurzname, z. B. „Hauptstraße 5“ oder „MFH Lindenweg“.</summary>
    public string Bezeichnung { get; set; }

    public string Strasse { get; set; }

    public string Plz { get; set; }

    public string Ort { get; set; }

    public int? Baujahr { get; set; }

    public string? Gemarkung { get; set; }

    public string? Flurstueck { get; set; }

    public string? GrundsteuerAktenzeichen { get; set; }

    public Heizungsart Heizungsart { get; set; }

    public Energietraeger Energietraeger { get; set; }

    /// <summary>Foto des Hauses aus der Dateiablage.</summary>
    public Guid? BildDateiId { get; set; }

    public string? Notizen { get; set; }

    public List<Einheit> Einheiten { get; private set; } = [];

    public List<ObjektFlaeche> Flaechen { get; private set; } = [];

    public string Anschrift => $"{Strasse}, {Plz} {Ort}";

    public void Pruefen()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Bezeichnung);
        ArgumentException.ThrowIfNullOrWhiteSpace(Strasse);
        ArgumentException.ThrowIfNullOrWhiteSpace(Plz);
        ArgumentException.ThrowIfNullOrWhiteSpace(Ort);
    }

    public Einheit EinheitHinzufuegen(string bezeichnung, Einheitstyp typ)
    {
        if (Einheiten.Any(e => string.Equals(e.Bezeichnung, bezeichnung.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new RegelverletzungException($"Im Objekt gibt es bereits eine Einheit „{bezeichnung.Trim()}“.");
        }

        var einheit = new Einheit(Id, bezeichnung.Trim(), typ, Einheiten.Count == 0 ? 1 : Einheiten.Max(e => e.Reihenfolge) + 1);
        Einheiten.Add(einheit);
        return einheit;
    }

    /// <summary>Gesamt- und Gemeinschaftsfläche ab dem Stichtag (Wohnfläche des Hauses bzw. Treppenhaus, Keller …).</summary>
    public ObjektFlaeche FlaecheSetzen(DateOnly ab, decimal gesamtflaeche, decimal gemeinschaftsflaeche) =>
        Zeitreihe.Fortschreiben(Flaechen, ab, g => new ObjektFlaeche(Id, g, gesamtflaeche, gemeinschaftsflaeche));

    public ObjektFlaeche? FlaecheAm(DateOnly tag) => Zeitreihe.Am(Flaechen, tag);

    /// <summary>Plausibilitätshinweise zum Stichtag (keine Fehler: Daten dürfen unvollständig sein).</summary>
    public IReadOnlyList<string> Pruefhinweise(DateOnly tag)
    {
        var hinweise = new List<string>();
        var flaeche = FlaecheAm(tag);
        var summeEinheiten = Einheiten.Sum(e => e.FlaecheAm(tag)?.Wohnflaeche ?? 0);
        if (flaeche is not null && summeEinheiten + flaeche.Gemeinschaftsflaeche > flaeche.Gesamtflaeche)
        {
            hinweise.Add(
                $"Die Wohnflächen der Einheiten ({Qm(summeEinheiten)}) und die Gemeinschaftsfläche ({Qm(flaeche.Gemeinschaftsflaeche)}) " +
                $"sind zusammen größer als die Gesamtfläche des Objekts ({Qm(flaeche.Gesamtflaeche)}).");
        }

        hinweise.AddRange(Einheiten.SelectMany(e => e.Pruefhinweise(tag)));
        return hinweise;
    }

    internal static string Qm(decimal wert) => wert.ToString("#,##0.##", CultureInfo.GetCultureInfo("de-DE")) + " m²";
}

/// <summary>Flächen eines Objekts in einem Zeitraum.</summary>
public sealed class ObjektFlaeche : Entitaet, IZeitabhaengig
{
    private ObjektFlaeche()
    {
    }

    internal ObjektFlaeche(Guid objektId, Zeitraum gueltigkeit, decimal gesamtflaeche, decimal gemeinschaftsflaeche)
    {
        Flaechenwert.Pruefen(gesamtflaeche, "Gesamtfläche");
        Flaechenwert.Pruefen(gemeinschaftsflaeche, "Gemeinschaftsfläche", nullErlaubt: true);
        ObjektId = objektId;
        Gueltigkeit = gueltigkeit;
        Gesamtflaeche = gesamtflaeche;
        Gemeinschaftsflaeche = gemeinschaftsflaeche;
    }

    public Guid ObjektId { get; private set; }

    public Zeitraum Gueltigkeit { get; private set; }

    /// <summary>Summe aller Wohn-/Nutzflächen des Hauses in m².</summary>
    public decimal Gesamtflaeche { get; private set; }

    /// <summary>Gemeinschaftlich genutzte Flächen (Treppenhaus, Waschküche …) in m².</summary>
    public decimal Gemeinschaftsflaeche { get; private set; }

    public void GueltigkeitBeenden(DateOnly letzterTag) => Gueltigkeit = Gueltigkeit.BeendenZum(letzterTag);
}

internal static class Flaechenwert
{
    public static void Pruefen(decimal wert, string name, bool nullErlaubt = false)
    {
        if (wert < 0 || (!nullErlaubt && wert == 0))
        {
            throw new RegelverletzungException($"Die {name} muss größer als 0 sein.");
        }

        if (wert > 100_000)
        {
            throw new RegelverletzungException($"Die {name} ist unplausibel groß.");
        }

        if (decimal.Round(wert, 2) != wert)
        {
            throw new RegelverletzungException($"Die {name} hat höchstens zwei Nachkommastellen.");
        }
    }
}
