using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Domain.Tests.Vertraege;

public class MietmodellTests
{
    private static readonly DateOnly Beginn = new(2026, 1, 1);
    private static readonly DateTimeOffset Jetzt = new(2027, 1, 1, 0, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(650, 117.4, 121.7, 673.81)]
    [InlineData(650, 121.7, 117.4, 627.03)] // Index gesunken: Miete sinkt
    [InlineData(10, 100, 100.05, 10.01)] // 10,005 → kaufmännisch aufgerundet
    [InlineData(800, 110.0, 110.0, 800)]
    public void Indexmiete_neue_Miete(double miete, double alt, double neu, double erwartet) =>
        Assert.Equal((decimal)erwartet, Indexmiete.NeueMiete((decimal)miete, (decimal)alt, (decimal)neu));

    [Fact]
    public void Indexveraenderung_in_Prozent() => Assert.Equal(3.7m, Indexmiete.VeraenderungProzent(117.4m, 121.7m));

    [Theory]
    [InlineData(2026, 10, 15, 2026, 12, 1)]
    [InlineData(2026, 12, 31, 2027, 2, 1)]
    public void Indexanpassung_wirkt_ab_dem_uebernaechsten_Monat(int j, int m, int t, int ej, int em, int et) =>
        Assert.Equal(new DateOnly(ej, em, et), Indexmiete.WirksamAb(new DateOnly(j, m, t)));

    [Fact]
    public void Indexanpassung_frueher_als_12_Monate_wird_abgelehnt()
    {
        var vertrag = Vertrag();
        vertrag.MietmodellSetzen(Mietmodell.Index, new DateOnly(2025, 11, 1), 117.4m);

        var fehler = Assert.Throws<RegelverletzungException>(() => vertrag.IndexAnpassen(new DateOnly(2026, 12, 1), new DateOnly(2026, 9, 1), 121.7m));
        Assert.Contains("frühestens ab 01.01.2027", fehler.Message, StringComparison.Ordinal);

        var neu = vertrag.IndexAnpassen(new DateOnly(2027, 1, 1), new DateOnly(2026, 9, 1), 121.7m);

        Assert.Equal(673.81m, neu);
        Assert.Equal(650m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2026, 12, 31)));
        Assert.Equal(673.81m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2027, 1, 1)));
        Assert.Equal(new DateOnly(2026, 9, 1), vertrag.IndexBasisMonat);
        Assert.Equal(121.7m, vertrag.IndexBasisWert);
        Assert.Equal(new DateOnly(2027, 1, 1), vertrag.LetzteKaltmieteAenderung());

        // nächste Anpassung erst wieder ein Jahr später
        Assert.Throws<RegelverletzungException>(() => vertrag.IndexAnpassen(new DateOnly(2027, 6, 1), new DateOnly(2027, 3, 1), 123m));
    }

    [Fact]
    public void Indexmiete_braucht_einen_Ausgangsindex()
    {
        var vertrag = Vertrag();

        Assert.Throws<RegelverletzungException>(() => vertrag.MietmodellSetzen(Mietmodell.Index));
        Assert.Throws<RegelverletzungException>(() => vertrag.IndexAnpassen(new DateOnly(2027, 1, 1), new DateOnly(2026, 9, 1), 121.7m));
    }

    [Fact]
    public void Staffeln_brauchen_mindestens_ein_Jahr_Abstand()
    {
        var vertrag = Vertrag();
        vertrag.MietmodellSetzen(Mietmodell.Staffel);

        Assert.Throws<RegelverletzungException>(() => vertrag.StaffelHinzufuegen(new DateOnly(2026, 7, 1), 680m)); // < 1 Jahr nach Beginn
        vertrag.StaffelHinzufuegen(new DateOnly(2027, 1, 1), 680m);
        Assert.Throws<RegelverletzungException>(() => vertrag.StaffelHinzufuegen(new DateOnly(2027, 6, 1), 700m));
        vertrag.StaffelHinzufuegen(new DateOnly(2028, 1, 1), 710m);
        Assert.Throws<RegelverletzungException>(() => vertrag.StaffelHinzufuegen(new DateOnly(2028, 1, 1), 720m));

        Assert.Equal(2, vertrag.Staffeln.Count);
    }

    [Fact]
    public void Staffel_zum_1_1_gilt_ab_dem_1_1()
    {
        var vertrag = Vertrag();
        vertrag.MietmodellSetzen(Mietmodell.Staffel);
        vertrag.StaffelHinzufuegen(new DateOnly(2027, 1, 1), 680m);
        vertrag.StaffelHinzufuegen(new DateOnly(2028, 1, 1), 710m);

        Assert.Empty(vertrag.FaelligeStaffelnAnwenden(new DateOnly(2026, 12, 31), Jetzt));
        var angewendet = vertrag.FaelligeStaffelnAnwenden(new DateOnly(2027, 1, 1), Jetzt);

        Assert.Equal(new DateOnly(2027, 1, 1), Assert.Single(angewendet).Ab);
        Assert.Equal(650m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2026, 12, 31)));
        Assert.Equal(680m, vertrag.BetragAm(Bestandteilart.Kaltmiete, new DateOnly(2027, 1, 1)));
        Assert.Empty(vertrag.FaelligeStaffelnAnwenden(new DateOnly(2027, 1, 2), Jetzt)); // nur einmal
        Assert.Throws<RegelverletzungException>(() => vertrag.StaffelEntfernen(angewendet[0].Id));
        Assert.Throws<RegelverletzungException>(() => vertrag.MietmodellSetzen(Mietmodell.Standard)); // offene Staffel 2028
    }

    [Fact]
    public void Staffeln_nur_bei_Staffelmiete() =>
        Assert.Throws<RegelverletzungException>(() => Vertrag().StaffelHinzufuegen(new DateOnly(2027, 1, 1), 680m));

    [Fact]
    public void Letzte_Erhoehung_fuer_die_Standardmiete()
    {
        var vertrag = Vertrag();
        Assert.Null(vertrag.LetzteKaltmieteAenderung());

        vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, new DateOnly(2027, 4, 1), 700m);

        Assert.Equal(new DateOnly(2027, 4, 1), vertrag.LetzteKaltmieteAenderung());
        Assert.Null(vertrag.LetzteKaltmieteAenderung(new DateOnly(2027, 3, 31)));
    }

    private static Mietvertrag Vertrag()
    {
        var vertrag = new Mietvertrag(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Beginn);
        vertrag.ParteiHinzufuegen(Guid.CreateVersion7(), Parteirolle.Mieter);
        vertrag.BestandteilSetzen(Bestandteilart.Kaltmiete, Beginn, 650m);
        return vertrag;
    }
}
