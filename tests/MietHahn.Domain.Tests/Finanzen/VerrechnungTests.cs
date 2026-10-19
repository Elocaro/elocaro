using MietHahn.Domain.Finanzen;

namespace MietHahn.Domain.Tests.Finanzen;

public class VerrechnungTests
{
    private static readonly Guid KaltMaerz = Id(1);
    private static readonly Guid BkMaerz = Id(2);
    private static readonly Guid KaltApril = Id(3);
    private static readonly Guid BkApril = Id(4);
    private static readonly Guid Mahngebuehr = Id(5);

    private static readonly Verrechnungsposten[] Mieten =
    [
        Miete(KaltMaerz, 3, 600m),
        Miete(BkMaerz, 3, 200m),
        Miete(KaltApril, 4, 600m),
        Miete(BkApril, 4, 200m),
    ];

    [Fact]
    public void Teilzahlung_tilgt_die_aelteste_Monatsmiete_anteilig_je_Bestandteil()
    {
        var ergebnis = Verrechnung.Verteilen(Mieten, [Zahlung(10, 4, 10, 1000m)], Verrechnungsreihenfolge.Gesetzlich);

        Assert.Equal(600m, ergebnis.Getilgt(KaltMaerz));
        Assert.Equal(200m, ergebnis.Getilgt(BkMaerz));
        Assert.Equal(150m, ergebnis.Getilgt(KaltApril));
        Assert.Equal(50m, ergebnis.Getilgt(BkApril));
        Assert.Equal(600m, ergebnis.Offen.Values.Sum());
        Assert.Equal(0m, ergebnis.Ueberzahlung);
    }

    [Fact]
    public void Kosten_vor_Zinsen_vor_Hauptforderung_nur_bei_gesetzlicher_Reihenfolge()
    {
        var forderungen = Mieten.Append(new Verrechnungsposten(Mahngebuehr, new DateOnly(2026, 4, 20), 5m, Verrechnungsrang.Kosten)).ToList();
        var zahlung = Zahlung(10, 4, 25, 805m);

        var gesetzlich = Verrechnung.Verteilen(forderungen, [zahlung], Verrechnungsreihenfolge.Gesetzlich);
        Assert.Equal(5m, gesetzlich.Getilgt(Mahngebuehr));
        Assert.Equal(800m, gesetzlich.Getilgt(KaltMaerz) + gesetzlich.Getilgt(BkMaerz));

        var mieteZuerst = Verrechnung.Verteilen(forderungen, [zahlung], Verrechnungsreihenfolge.MieteZuerst);
        Assert.Equal(0m, mieteZuerst.Getilgt(Mahngebuehr));
        Assert.Equal(5m, mieteZuerst.Getilgt(KaltApril) + mieteZuerst.Getilgt(BkApril));

        var aelteste = Verrechnung.Verteilen(forderungen, [zahlung], Verrechnungsreihenfolge.AeltesteZuerst);
        Assert.Equal(0m, aelteste.Getilgt(Mahngebuehr));
    }

    [Fact]
    public void Tilgungsbestimmung_des_Mieters_geht_vor()
    {
        var zahlung = new Verrechnungsposten(Id(10), new DateOnly(2026, 4, 10), 800m, Bestimmung: KaltApril);

        var ergebnis = Verrechnung.Verteilen(Mieten, [zahlung], Verrechnungsreihenfolge.Gesetzlich);

        Assert.Equal(600m, ergebnis.Getilgt(KaltApril));
        Assert.Equal(150m, ergebnis.Getilgt(KaltMaerz));
        Assert.Equal(50m, ergebnis.Getilgt(BkMaerz));
    }

    [Fact]
    public void Vorauszahlung_tilgt_erst_bei_Faelligkeit_mit_dem_Zahlungsdatum_als_Zufluss()
    {
        // Märzmiete schon am 28.2. gezahlt, Aprilmiete erst am 2.5.
        var ergebnis = Verrechnung.Verteilen(Mieten, [Zahlung(10, 2, 28, 800m), Zahlung(11, 5, 2, 800m)], Verrechnungsreihenfolge.Gesetzlich);

        Assert.All(ergebnis.Anteile.Where(a => a.ForderungId == KaltMaerz || a.ForderungId == BkMaerz), a => Assert.Equal(new DateOnly(2026, 2, 28), a.Datum));
        Assert.All(ergebnis.Anteile.Where(a => a.ForderungId == KaltApril), a => Assert.Equal(new DateOnly(2026, 5, 2), a.Datum));
        Assert.All(ergebnis.Offen.Values, o => Assert.Equal(0m, o));
    }

    [Fact]
    public void Ueberzahlung_bleibt_als_Guthaben_und_tilgt_spaetere_Forderungen()
    {
        var ergebnis = Verrechnung.Verteilen(Mieten.Take(2), [Zahlung(10, 3, 3, 1000m)], Verrechnungsreihenfolge.Gesetzlich);
        Assert.Equal(200m, ergebnis.Ueberzahlung);

        var spaeter = Verrechnung.Verteilen(Mieten, [Zahlung(10, 3, 3, 1000m)], Verrechnungsreihenfolge.Gesetzlich);
        Assert.Equal(0m, spaeter.Ueberzahlung);
        Assert.Equal(150m, spaeter.Getilgt(KaltApril));
        Assert.All(spaeter.Anteile, a => Assert.Equal(new DateOnly(2026, 3, 3), a.Datum));
    }

    [Fact]
    public void Rundungsreste_in_Cent_gehen_an_die_groessten_Reste()
    {
        Verrechnungsposten[] dreiTeile = [Miete(Id(1), 3, 100m), Miete(Id(2), 3, 100m), Miete(Id(3), 3, 100m)];

        var ergebnis = Verrechnung.Verteilen(dreiTeile, [Zahlung(10, 3, 5, 100m)], Verrechnungsreihenfolge.Gesetzlich);

        Assert.Equal(100m, ergebnis.Anteile.Sum(a => a.Betrag));
        Assert.Equal([33.34m, 33.33m, 33.33m], dreiTeile.Select(f => ergebnis.Getilgt(f.Id)));
    }

    private static Verrechnungsposten Miete(Guid id, int monat, decimal betrag) => new(id, new DateOnly(2026, monat, 3), betrag, IstMiete: true);

    private static Verrechnungsposten Zahlung(int id, int monat, int tag, decimal betrag) => new(Id(id), new DateOnly(2026, monat, tag), betrag);

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:000000000000}");
}
