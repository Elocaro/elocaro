using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Domain.Tests.Vertraege;

public class MietvertragTests
{
    private static readonly DateOnly Beginn = new(2026, 1, 1);

    [Fact]
    public void Mieterhoehung_zum_1_3_erzeugt_neuen_Bestandteil_alte_Miete_bleibt()
    {
        var vertrag = Vertrag();

        vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, new DateOnly(2026, 3, 1), 700m);

        Assert.Equal(650m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2026, 2, 28)));
        Assert.Equal(700m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2026, 3, 1)));
        var kaltmieten = vertrag.Bestandteile.Where(b => b.Art == Bestandteilart.Kaltmiete).OrderBy(b => b.Gueltigkeit.Von).ToList();
        Assert.Equal([new Zeitraum(Beginn, new DateOnly(2026, 2, 28)), new Zeitraum(new DateOnly(2026, 3, 1))], kaltmieten.Select(k => k.Gueltigkeit));
        Assert.Equal(650m + 180m + 90m, vertrag.MonatsmieteAm(new DateOnly(2026, 2, 1)));
        Assert.Equal(700m + 180m + 90m, vertrag.MonatsmieteAm(new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void Vorauszahlung_aendern_beruehrt_die_Kaltmiete_nicht()
    {
        var vertrag = Vertrag();

        vertrag.BestandteilSetzen(Bestandteilart.BetriebskostenVorauszahlung, new DateOnly(2027, 1, 1), 210m);

        Assert.Equal(650m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2027, 1, 1)));
        Assert.Equal(210m, vertrag.BetragAm(Bestandteilart.BetriebskostenVorauszahlung, new DateOnly(2027, 1, 1)));
        Assert.Equal(180m, vertrag.BetragAm(Bestandteilart.BetriebskostenVorauszahlung, new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void Pauschale_bekommt_keine_Nebenkostenabrechnung()
    {
        var wg = new Mietvertrag(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Beginn) { Abrechnungsart = Abrechnungsart.Pauschale };

        Assert.False(wg.ErhaeltNebenkostenabrechnung);
        Assert.False(wg.ErhaeltHeizkostenabrechnung);
        wg.HeizkostenVerbrauchsabhaengig = true;
        Assert.True(wg.ErhaeltHeizkostenabrechnung);
        Assert.True(Vertrag().ErhaeltNebenkostenabrechnung);
    }

    [Fact]
    public void Bestandteil_ausserhalb_der_Laufzeit_wird_abgelehnt()
    {
        var vertrag = Vertrag();

        var fehler = Assert.Throws<RegelverletzungException>(() => vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, new DateOnly(2025, 12, 1), 600m));
        Assert.Contains("außerhalb der Vertragslaufzeit", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 10, 5, 2026, 12, 31)] // Zugang am 3. Werktag → Ablauf des übernächsten Monats
    [InlineData(2026, 10, 6, 2027, 1, 31)] // einen Tag später → ein Monat mehr
    [InlineData(2026, 2, 3, 2026, 4, 30)]
    [InlineData(2026, 2, 28, 2026, 5, 31)]
    public void Kuendigungstermin_fuer_den_Mieter(int j, int m, int t, int ej, int em, int et) =>
        Assert.Equal(new DateOnly(ej, em, et), Mietvertrag.KuendigungsterminFuerMieter(new DateOnly(j, m, t)));

    [Theory]
    [InlineData(2026, 3)]
    [InlineData(2021, 6)] // 5 Jahre
    [InlineData(2018, 9)] // 8 Jahre
    public void Kuendigungsfrist_des_Vermieters_nach_Mietdauer(int beginnJahr, int monate)
    {
        var vertrag = new Mietvertrag(Guid.CreateVersion7(), Guid.CreateVersion7(), null, new DateOnly(beginnJahr, 1, 1));

        Assert.Equal(monate, vertrag.KuendigungsfristVermieterMonate(new DateOnly(2026, 10, 6)));
    }

    [Fact]
    public void Kuendigung_beendet_Vertrag_und_Bestandteile_Aufheben_oeffnet_wieder()
    {
        var vertrag = Vertrag();
        var ende = new DateOnly(2026, 12, 31);

        vertrag.Kuendigen(new DateOnly(2026, 10, 5), Kuendigender.Mieter, ende);

        Assert.Equal(ende, vertrag.Ende);
        Assert.Equal(Vertragsstatus.Gekuendigt, vertrag.StatusAm(new DateOnly(2026, 11, 1)));
        Assert.Equal(Vertragsstatus.Beendet, vertrag.StatusAm(new DateOnly(2027, 1, 1)));
        Assert.All(vertrag.Bestandteile.Concat<IZeitabhaengig>(vertrag.Personen), b => Assert.Equal(ende, b.Gueltigkeit.Bis));
        Assert.Equal(0m, vertrag.MonatsmieteAm(new DateOnly(2027, 1, 1)));

        vertrag.KuendigungAufheben();

        Assert.Null(vertrag.Ende);
        Assert.Equal(Vertragsstatus.Aktiv, vertrag.StatusAm(new DateOnly(2027, 1, 1)));
        Assert.Equal(650m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2027, 6, 1)));
        Assert.Equal(2, vertrag.PersonenAm(new DateOnly(2027, 6, 1)));
    }

    [Fact]
    public void Befristeter_Vertrag_endet_mit_der_Befristung()
    {
        var vertrag = new Mietvertrag(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Beginn, new DateOnly(2027, 12, 31));

        Assert.Equal(new DateOnly(2027, 12, 31), vertrag.Ende);
        Assert.Equal(Vertragsstatus.Gekuendigt, vertrag.StatusAm(Beginn)); // Ende steht fest
        Assert.Equal(Vertragsstatus.Kuenftig, vertrag.StatusAm(Beginn.AddDays(-1)));
        Assert.Throws<RegelverletzungException>(() => new Mietvertrag(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Beginn, Beginn.AddDays(-1)));
    }

    [Fact]
    public void Mehrere_Mieter_und_Buerge_mindestens_ein_Mieter_bleibt()
    {
        var vertrag = Vertrag();
        var zweiter = Guid.CreateVersion7();
        var buerge = Guid.CreateVersion7();

        vertrag.ParteiHinzufuegen(zweiter, Parteirolle.Mieter);
        vertrag.ParteiHinzufuegen(buerge, Parteirolle.Buerge);

        Assert.Equal(2, vertrag.Mieter.Count());
        Assert.Throws<RegelverletzungException>(() => vertrag.ParteiHinzufuegen(zweiter, Parteirolle.Buerge));
        vertrag.ParteiEntfernen(zweiter);
        Assert.Throws<RegelverletzungException>(() => vertrag.ParteiEntfernen(vertrag.Mieter.Single().PersonId));
        vertrag.ParteiEntfernen(buerge);
    }

    [Fact]
    public void Personenanzahl_ist_zeitabhaengig()
    {
        var vertrag = Vertrag();

        vertrag.PersonenSetzen(new DateOnly(2026, 7, 1), 3);

        Assert.Equal(2, vertrag.PersonenAm(new DateOnly(2026, 6, 30)));
        Assert.Equal(3, vertrag.PersonenAm(new DateOnly(2026, 7, 1)));
        Assert.Throws<RegelverletzungException>(() => vertrag.PersonenSetzen(new DateOnly(2026, 8, 1), 0));
    }

    [Fact]
    public void Stellplatz_beenden()
    {
        var vertrag = Vertrag();
        vertrag.BestandteilSetzen(Bestandteilart.Stellplatz, Beginn, 40m);

        vertrag.BestandteilBeenden(Bestandteilart.Stellplatz, new DateOnly(2026, 6, 30));

        Assert.Equal(40m, vertrag.BetragAm(Bestandteilart.Stellplatz, new DateOnly(2026, 6, 30)));
        Assert.Null(vertrag.BetragAm(Bestandteilart.Stellplatz, new DateOnly(2026, 7, 1)));
    }

    private static Mietvertrag Vertrag()
    {
        var vertrag = new Mietvertrag(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Beginn);
        vertrag.ParteiHinzufuegen(Guid.CreateVersion7(), Parteirolle.Mieter);
        vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, Beginn, 650m);
        vertrag.BestandteilSetzen(Bestandteilart.BetriebskostenVorauszahlung, Beginn, 180m);
        vertrag.BestandteilSetzen(Bestandteilart.HeizkostenVorauszahlung, Beginn, 90m);
        vertrag.PersonenSetzen(Beginn, 2);
        return vertrag;
    }
}
