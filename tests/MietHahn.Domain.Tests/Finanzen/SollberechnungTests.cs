using MietHahn.Domain.Finanzen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Domain.Tests.Finanzen;

public class SollberechnungTests
{
    [Fact]
    public void Einzug_am_15_Maerz_ergibt_17_31_der_Miete()
    {
        var vertrag = Vertrag(new DateOnly(2026, 3, 15));

        var maerz = Sollberechnung.Monat(vertrag, 2026, 3);

        Assert.Equal([340.00m, 82.26m], maerz.Select(p => p.Betrag));
        Assert.Equal("Kaltmiete 03/2026 (anteilig 17/31)", maerz[0].Bezeichnung);
        Assert.All(maerz, p => Assert.True(p.Anteilig));
        Assert.Equal([620m, 150m], Sollberechnung.Monat(vertrag, 2026, 4).Select(p => p.Betrag));
        Assert.Empty(Sollberechnung.Monat(vertrag, 2026, 2));
    }

    [Fact]
    public void Auszug_und_Mieterhoehung_im_Monat_werden_taggenau_aufgeteilt()
    {
        var vertrag = Vertrag(new DateOnly(2026, 1, 1));
        vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, new DateOnly(2026, 6, 16), 680m);
        vertrag.Kuendigen(new DateOnly(2026, 4, 2), Kuendigender.Mieter, new DateOnly(2026, 7, 10));

        var juni = Sollberechnung.Monat(vertrag, 2026, 6);
        var juli = Sollberechnung.Monat(vertrag, 2026, 7);

        // 15 Tage zu 620 € + 15 Tage zu 680 € im Juni (30 Tage)
        Assert.Equal(650m, juni.Single(p => p.Bestandteil == Bestandteilart.Kaltmiete).Betrag);
        Assert.Equal("Kaltmiete 06/2026", juni[0].Bezeichnung);
        Assert.True(juni[0].Anteilig);
        Assert.Equal(decimal.Round(680m * 10 / 31, 2), juli[0].Betrag);
        Assert.Empty(Sollberechnung.Monat(vertrag, 2026, 8));
    }

    [Fact]
    public void Vor_dem_Buchhaltungsbeginn_wird_nichts_gefordert()
    {
        var vertrag = Vertrag(new DateOnly(2015, 5, 1));

        Assert.Empty(Sollberechnung.Monat(vertrag, 2026, 9, new DateOnly(2026, 10, 1)));
        Assert.Equal(770m, Sollberechnung.Monat(vertrag, 2026, 10, new DateOnly(2026, 10, 1)).Sum(p => p.Betrag));
        Assert.Equal(
            [new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)],
            Sollberechnung.Monate(vertrag, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Monate_enden_mit_dem_Vertrag()
    {
        var vertrag = Vertrag(new DateOnly(2026, 3, 15));
        vertrag.Kuendigen(new DateOnly(2026, 3, 20), Kuendigender.Mieter, new DateOnly(2026, 5, 2));

        Assert.Equal(
            [new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1)],
            Sollberechnung.Monate(vertrag, new DateOnly(2020, 1, 1), new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Erstattung_ist_negativ_und_Monatsmieten_nicht_von_Hand_aenderbar()
    {
        Assert.Throws<RegelverletzungException>(() => new Sollstellung(Guid.CreateVersion7(), Postenart.Erstattung, new DateOnly(2026, 6, 1), 50m, "Guthaben NK 2025"));
        Assert.Throws<RegelverletzungException>(() => new Sollstellung(Guid.CreateVersion7(), Postenart.Mahngebuehr, new DateOnly(2026, 6, 1), 2.505m, "Mahngebühr"));

        var miete = Sollstellung.Monatsmiete(Guid.CreateVersion7(), new DateOnly(2026, 6, 1), Bestandteilart.Kaltmiete, new DateOnly(2026, 6, 3), 620m, "Kaltmiete 06/2026");
        Assert.Throws<RegelverletzungException>(() => miete.Aendern(Postenart.Sonstiges, new DateOnly(2026, 6, 3), 600m, "x"));
        Assert.False(miete.Angleichen(new DateOnly(2026, 6, 3), 620m, "Kaltmiete 06/2026"));
        Assert.True(miete.Angleichen(new DateOnly(2026, 6, 3), 650m, "Kaltmiete 06/2026"));
        Assert.Equal(Verrechnungsrang.Kosten, new Sollstellung(Guid.CreateVersion7(), Postenart.Mahngebuehr, new DateOnly(2026, 6, 1), 5m, "Mahngebühr").Rang);
    }

    private static Mietvertrag Vertrag(DateOnly beginn)
    {
        var vertrag = new Mietvertrag(Guid.CreateVersion7(), Guid.CreateVersion7(), null, beginn);
        vertrag.ParteiHinzufuegen(Guid.CreateVersion7(), Parteirolle.Mieter);
        vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, beginn, 620m);
        vertrag.BestandteilSetzen(Bestandteilart.BetriebskostenVorauszahlung, beginn, 150m);
        return vertrag;
    }
}
