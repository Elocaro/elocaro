using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Tests.Gemeinsam;

public class ZeitraumTests
{
    private static DateOnly D(int jahr, int monat, int tag) => new(jahr, monat, tag);

    [Fact]
    public void Ende_vor_Beginn_ist_ungueltig()
    {
        Assert.Throws<ArgumentException>(() => new Zeitraum(D(2026, 2, 1), D(2026, 1, 31)));
    }

    [Fact]
    public void Eintaegiger_Zeitraum_ist_gueltig()
    {
        Assert.Equal(1, new Zeitraum(D(2026, 2, 1), D(2026, 2, 1)).Tage);
    }

    [Theory]
    [InlineData(2026, 365)]
    [InlineData(2028, 366)]
    public void Kalenderjahr_hat_365_oder_366_Tage(int jahr, int tage)
    {
        Assert.Equal(tage, Zeitraum.Kalenderjahr(jahr).Tage);
    }

    [Fact]
    public void Monat_endet_am_letzten_Tag()
    {
        Assert.Equal(new Zeitraum(D(2028, 2, 1), D(2028, 2, 29)), Zeitraum.Monat(2028, 2));
    }

    [Fact]
    public void Unbefristeter_Zeitraum_hat_keine_Tagesanzahl()
    {
        Assert.Throws<InvalidOperationException>(() => new Zeitraum(D(2026, 1, 1)).Tage);
    }

    [Theory]
    [InlineData(2026, 1, 1, true)]
    [InlineData(2026, 6, 30, true)]
    [InlineData(2025, 12, 31, false)]
    [InlineData(2026, 7, 1, false)]
    public void Enthaelt_prueft_inklusive_Grenzen(int jahr, int monat, int tag, bool erwartet)
    {
        Assert.Equal(erwartet, new Zeitraum(D(2026, 1, 1), D(2026, 6, 30)).Enthaelt(D(jahr, monat, tag)));
    }

    [Fact]
    public void Unbefristet_enthaelt_alle_spaeteren_Tage()
    {
        Assert.True(new Zeitraum(D(2026, 1, 1)).Enthaelt(D(2099, 12, 31)));
    }

    [Theory]
    [InlineData("2026-01-01", "2026-06-30", "2026-06-30", "2026-12-31", true)]  // gemeinsamer Tag
    [InlineData("2026-01-01", "2026-06-30", "2026-07-01", "2026-12-31", false)] // direkt anschließend
    [InlineData("2026-01-01", null, "2030-01-01", "2030-01-31", true)]          // unbefristet
    [InlineData("2026-07-01", null, "2026-01-01", "2026-06-30", false)]
    [InlineData("2026-01-01", null, "2025-01-01", null, true)]
    public void Ueberschneidung_wird_erkannt(string von1, string? bis1, string von2, string? bis2, bool erwartet)
    {
        var a = Z(von1, bis1);
        var b = Z(von2, bis2);

        Assert.Equal(erwartet, a.UeberschneidetSich(b));
        Assert.Equal(erwartet, b.UeberschneidetSich(a));
    }

    [Fact]
    public void Schnitt_liefert_den_gemeinsamen_Teil()
    {
        var mietdauer = new Zeitraum(D(2025, 6, 15));
        var abrechnungsjahr = Zeitraum.Kalenderjahr(2025);

        Assert.Equal(new Zeitraum(D(2025, 6, 15), D(2025, 12, 31)), mietdauer.Schnitt(abrechnungsjahr));
        Assert.Null(new Zeitraum(D(2024, 1, 1), D(2024, 12, 31)).Schnitt(abrechnungsjahr));
    }

    [Fact]
    public void Schnitt_zweier_unbefristeter_ist_unbefristet()
    {
        Assert.Equal(new Zeitraum(D(2026, 1, 1)), new Zeitraum(D(2025, 1, 1)).Schnitt(new Zeitraum(D(2026, 1, 1))));
    }

    [Fact]
    public void Mieterwechsel_zum_15_Juni_teilt_das_Jahr_taggenau()
    {
        var jahr = Zeitraum.Kalenderjahr(2025);
        var alt = new Zeitraum(D(2020, 1, 1), D(2025, 6, 14));
        var neu = new Zeitraum(D(2025, 6, 15));

        Assert.Equal(165, alt.TageIn(jahr));
        Assert.Equal(200, neu.TageIn(jahr));
        Assert.Equal(jahr.Tage, alt.TageIn(jahr) + neu.TageIn(jahr));
    }

    [Fact]
    public void TageIn_ohne_Ueberschneidung_ist_null()
    {
        Assert.Equal(0, new Zeitraum(D(2027, 1, 1)).TageIn(Zeitraum.Kalenderjahr(2026)));
    }

    [Fact]
    public void TageIn_braucht_befristete_Begrenzung()
    {
        Assert.Throws<ArgumentException>(() => Zeitraum.Kalenderjahr(2026).TageIn(new Zeitraum(D(2026, 1, 1))));
    }

    [Fact]
    public void Beenden_setzt_das_Ende()
    {
        var beendet = new Zeitraum(D(2026, 1, 1)).BeendenZum(D(2026, 3, 31));

        Assert.Equal(D(2026, 3, 31), beendet.Bis);
        Assert.Throws<ArgumentException>(() => new Zeitraum(D(2026, 1, 1)).BeendenZum(D(2025, 12, 31)));
    }

    [Fact]
    public void Lueckenlose_Folge_ist_ueberlappungsfrei()
    {
        Assert.True(Zeitraum.SindUeberlappungsfrei([
            Z("2026-07-01", null),
            Z("2026-01-01", "2026-03-31"),
            Z("2026-04-01", "2026-06-30"),
        ]));
    }

    [Fact]
    public void Ueberlappende_Folge_wird_erkannt()
    {
        Assert.False(Zeitraum.SindUeberlappungsfrei([
            Z("2026-01-01", "2026-03-31"),
            Z("2026-03-31", null),
        ]));
    }

    [Fact]
    public void Darstellung_ist_deutsch()
    {
        Assert.Equal("01.01.2026 – 31.12.2026", Zeitraum.Kalenderjahr(2026).ToString());
        Assert.Equal("ab 15.06.2025", new Zeitraum(D(2025, 6, 15)).ToString());
    }

    private static Zeitraum Z(string von, string? bis) =>
        new(DateOnly.Parse(von, System.Globalization.CultureInfo.InvariantCulture), bis is null ? null : DateOnly.Parse(bis, System.Globalization.CultureInfo.InvariantCulture));
}
