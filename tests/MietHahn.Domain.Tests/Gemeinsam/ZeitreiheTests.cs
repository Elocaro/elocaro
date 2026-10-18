using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Tests.Gemeinsam;

public class ZeitreiheTests
{
    private static DateOnly D(int jahr, int monat, int tag) => new(jahr, monat, tag);

    [Fact]
    public void Erster_Stand_ist_unbefristet()
    {
        var staende = new List<Stand>();

        Zeitreihe.Fortschreiben(staende, D(2026, 1, 1), g => new Stand(g, 1));

        Assert.Equal(new Zeitraum(D(2026, 1, 1)), Assert.Single(staende).Gueltigkeit);
    }

    [Fact]
    public void Neuer_Stand_beendet_den_bisherigen_am_Vortag()
    {
        var staende = new List<Stand>();
        Zeitreihe.Fortschreiben(staende, D(2026, 1, 1), g => new Stand(g, 1));

        Zeitreihe.Fortschreiben(staende, D(2026, 3, 1), g => new Stand(g, 2));

        Assert.Equal([new Zeitraum(D(2026, 1, 1), D(2026, 2, 28)), new Zeitraum(D(2026, 3, 1))], staende.Select(s => s.Gueltigkeit));
        Assert.Equal(1, Zeitreihe.Am(staende, D(2026, 2, 28))!.Wert);
        Assert.Equal(2, Zeitreihe.Am(staende, D(2026, 3, 1))!.Wert);
        Assert.Null(Zeitreihe.Am(staende, D(2025, 12, 31)));
        Assert.True(Zeitraum.SindUeberlappungsfrei(staende.Select(s => s.Gueltigkeit)));
    }

    [Fact]
    public void Gleicher_Stichtag_korrigiert_den_juengsten_Stand()
    {
        var staende = new List<Stand>();
        Zeitreihe.Fortschreiben(staende, D(2026, 1, 1), g => new Stand(g, 1));
        Zeitreihe.Fortschreiben(staende, D(2026, 3, 1), g => new Stand(g, 2));

        Zeitreihe.Fortschreiben(staende, D(2026, 3, 1), g => new Stand(g, 3));

        Assert.Equal([1, 3], staende.Select(s => s.Wert));
        Assert.Equal(D(2026, 2, 28), staende[0].Gueltigkeit.Bis);
    }

    [Fact]
    public void Rueckwirkend_vor_den_juengsten_Stand_ist_nicht_moeglich()
    {
        var staende = new List<Stand>();
        Zeitreihe.Fortschreiben(staende, D(2026, 3, 1), g => new Stand(g, 1));

        var fehler = Assert.Throws<RegelverletzungException>(() => Zeitreihe.Fortschreiben(staende, D(2026, 2, 1), g => new Stand(g, 2)));

        Assert.Contains("01.03.2026", fehler.Message, StringComparison.Ordinal);
        Assert.Single(staende);
    }

    [Fact]
    public void Nach_einem_beendeten_Stand_bleibt_die_Luecke()
    {
        var staende = new List<Stand>();
        Zeitreihe.Fortschreiben(staende, D(2026, 1, 1), g => new Stand(g, 1)).GueltigkeitBeenden(D(2026, 1, 31));

        Zeitreihe.Fortschreiben(staende, D(2026, 3, 1), g => new Stand(g, 2));

        Assert.Equal(D(2026, 1, 31), staende[0].Gueltigkeit.Bis);
        Assert.Null(Zeitreihe.Am(staende, D(2026, 2, 15)));
    }

    private sealed class Stand(Zeitraum gueltigkeit, int wert) : IZeitabhaengig
    {
        public Zeitraum Gueltigkeit { get; private set; } = gueltigkeit;

        public int Wert { get; } = wert;

        public void GueltigkeitBeenden(DateOnly letzterTag) => Gueltigkeit = Gueltigkeit.BeendenZum(letzterTag);
    }
}
