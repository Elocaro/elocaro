using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Objekte;

namespace MietHahn.Domain.Tests.Objekte;

public class ObjektTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public void Mfh_mit_vier_Wohnungen_davon_eine_WG_mit_vier_Zimmern()
    {
        var haus = BeispielHaus();

        Assert.Equal(4, haus.Einheiten.Count);
        var wg = haus.Einheiten.Single(e => e.IstWg);
        Assert.Equal(4, wg.Zimmer.Count);
        Assert.False(wg.IstSelbstVermietbar);
        Assert.Equal(3, haus.Einheiten.Count(e => e.IstSelbstVermietbar));
        Assert.Equal([1, 2, 3, 4], haus.Einheiten.Select(e => e.Reihenfolge));
        Assert.Empty(haus.Pruefhinweise(Start));
    }

    [Fact]
    public void Flaeche_nach_Umbau_gilt_ab_Stichtag_alte_bleibt_erhalten()
    {
        var einheit = BeispielHaus().Einheiten[0];

        einheit.FlaecheSetzen(new DateOnly(2026, 7, 1), 82.4m, 80m);

        Assert.Equal(75.5m, einheit.FlaecheAm(new DateOnly(2026, 6, 30))!.Wohnflaeche);
        Assert.Equal(82.4m, einheit.FlaecheAm(new DateOnly(2026, 7, 1))!.Wohnflaeche);
        Assert.Equal(80m, einheit.FlaecheAm(new DateOnly(2026, 7, 1))!.Heizflaeche);
        Assert.Equal(75.5m, einheit.FlaecheAm(Start)!.Heizflaeche);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(12.345)]
    public void Unplausible_Wohnflaeche_wird_abgelehnt(double wert)
    {
        var einheit = new Objekt("Test", "Weg 1", "12345", "Ort").EinheitHinzufuegen("W1", Einheitstyp.Wohnung);

        Assert.Throws<RegelverletzungException>(() => einheit.FlaecheSetzen(Start, (decimal)wert));
    }

    [Fact]
    public void Doppelte_Bezeichnungen_werden_abgelehnt()
    {
        var haus = BeispielHaus();

        Assert.Throws<RegelverletzungException>(() => haus.EinheitHinzufuegen("wohnung 1", Einheitstyp.Wohnung));
        Assert.Throws<RegelverletzungException>(() => haus.Einheiten.Single(e => e.IstWg).ZimmerHinzufuegen("Zimmer 2"));
    }

    [Fact]
    public void Summe_der_Einheiten_groesser_als_Gesamtflaeche_ergibt_Hinweis()
    {
        var haus = BeispielHaus();

        haus.FlaecheSetzen(Start, 250m, 20m);

        var hinweis = Assert.Single(haus.Pruefhinweise(Start));
        Assert.Contains("größer als die Gesamtfläche des Objekts (250 m²)", hinweis, StringComparison.Ordinal);
    }

    [Fact]
    public void Zimmerflaechen_groesser_als_WG_Wohnflaeche_ergibt_Hinweis()
    {
        var haus = BeispielHaus();
        var wg = haus.Einheiten.Single(e => e.IstWg);

        wg.Zimmer[0].FlaecheSetzen(Start, 40m, 20m);

        Assert.Contains(haus.Pruefhinweise(Start), h => h.StartsWith("Wohnung 4: Die Zimmerflächen", StringComparison.Ordinal));
    }

    [Fact]
    public void Fehlende_Wohnflaeche_ergibt_Hinweis()
    {
        var haus = new Objekt("Test", "Weg 1", "12345", "Ort");
        haus.EinheitHinzufuegen("W1", Einheitstyp.Wohnung);

        Assert.Equal("W1: Keine Wohnfläche zum 01.01.2026 hinterlegt.", Assert.Single(haus.Pruefhinweise(Start)));
    }

    [Fact]
    public void Umlageflaeche_eines_Zimmers_enthaelt_den_Gemeinschaftsanteil()
    {
        var zimmer = BeispielHaus().Einheiten.Single(e => e.IstWg).Zimmer[0];

        Assert.Equal(14m + 8.5m, zimmer.FlaecheAm(Start)!.Umlageflaeche);
    }

    /// <summary>Das Haus aus dem Interview: MFH mit 4 Wohnungen, Wohnung 4 ist eine WG mit 4 Zimmern.</summary>
    internal static Objekt BeispielHaus()
    {
        var haus = new Objekt("Lindenweg 3", "Lindenweg 3", "50667", "Köln") { Baujahr = 1965, Heizungsart = Heizungsart.Zentralheizung, Energietraeger = Energietraeger.Erdgas };
        haus.FlaecheSetzen(Start, 400m, 30m);
        var flaechen = new[] { 75.5m, 68m, 92m, 110m };
        for (var i = 0; i < 4; i++)
        {
            var einheit = haus.EinheitHinzufuegen($"Wohnung {i + 1}", Einheitstyp.Wohnung);
            einheit.FlaecheSetzen(Start, flaechen[i]);
        }

        var wg = haus.Einheiten[3];
        foreach (var (name, flaeche) in new[] { ("Zimmer 1", 14m), ("Zimmer 2", 16m), ("Zimmer 3", 12m), ("Zimmer 4", 18m) })
        {
            wg.ZimmerHinzufuegen(name).FlaecheSetzen(Start, flaeche, 8.5m);
        }

        return haus;
    }
}
