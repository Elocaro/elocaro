using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Zaehlerwesen;
using ZaehlerEntitaet = MietHahn.Domain.Zaehlerwesen.Zaehler;

namespace MietHahn.Domain.Tests.Zaehlerwesen;

public class ZaehlerTests
{
    private static DateOnly D(int jahr, int monat, int tag) => new(jahr, monat, tag);

    [Fact]
    public void Einbau_legt_Anfangsstand_und_Eichfrist_an()
    {
        var zaehler = Wasserzaehler(D(2025, 12, 31), 100m);

        var stand = Assert.Single(zaehler.Staende);
        Assert.Equal((D(2025, 12, 31), 100m, Standart.Einbau), (stand.Datum, stand.Wert, stand.Art));
        Assert.Equal(D(2031, 12, 31), zaehler.EichfristBis);
        Assert.Equal(Messeinheit.Kubikmeter, zaehler.Messeinheit);
        Assert.Equal(Messeinheit.Kilowattstunden, ZaehlerEntitaet.StandardEinheit(Zaehlertyp.Waerme));
        Assert.Equal(D(2030, 12, 31), ZaehlerEntitaet.StandardEichfrist(Zaehlertyp.Waerme, D(2025, 3, 1)));
    }

    [Fact]
    public void Jahresverbrauch_aus_Ablesungen_zum_Jahresende()
    {
        var zaehler = Wasserzaehler(D(2025, 12, 31), 100m);
        zaehler.StandErfassen(D(2026, 12, 31), 465.5m, Standart.Ablesung, "Andy");

        var verbrauch = Verbrauchsrechner.Berechnen([zaehler], Zeitraum.Kalenderjahr(2026));

        Assert.Equal(365.5m, verbrauch.Menge);
        Assert.False(verbrauch.EnthaeltInterpolation);
        Assert.Null(verbrauch.Fehlt);
    }

    [Fact]
    public void Mieterwechsel_ohne_Zwischenablesung_wird_interpoliert_und_gekennzeichnet()
    {
        var zaehler = Wasserzaehler(D(2025, 12, 31), 100m);
        zaehler.StandErfassen(D(2026, 12, 31), 465m, Standart.Ablesung, "Andy");

        var bisAuszug = Verbrauchsrechner.Berechnen([zaehler], new Zeitraum(D(2026, 1, 1), D(2026, 6, 30)));
        var abEinzug = Verbrauchsrechner.Berechnen([zaehler], new Zeitraum(D(2026, 7, 1), D(2026, 12, 31)));

        Assert.True(bisAuszug.EnthaeltInterpolation);
        Assert.Equal(181m, bisAuszug.Menge); // 181 von 365 Tagen
        Assert.Equal(184m, abEinzug.Menge);
        Assert.Equal(365m, bisAuszug.Menge + abEinzug.Menge);
    }

    [Fact]
    public void Zaehlertausch_im_Jahr_summiert_den_Verbrauch_durchgaengig()
    {
        var alt = Wasserzaehler(D(2025, 12, 31), 100m);
        var neu = alt.Tauschen(D(2026, 6, 30), 280m, "NEU-2", 0m);
        neu.StandErfassen(D(2026, 12, 31), 190m, Standart.Ablesung, "Andy");

        var verbrauch = Verbrauchsrechner.Berechnen([alt, neu], Zeitraum.Kalenderjahr(2026));

        Assert.Equal(370m, verbrauch.Menge); // 180 alter Zähler + 190 neuer Zähler
        Assert.Equal(D(2026, 6, 30), alt.AusgebautAm);
        Assert.Equal(alt.Id, neu.VorgaengerId);
        Assert.Equal(Standart.Ausbau, alt.Staende.MaxBy(s => s.Datum)!.Art);
        Assert.Equal(D(2032, 12, 31), neu.EichfristBis); // 6 Jahre ab Einbau 2026
        Assert.Throws<RegelverletzungException>(() => alt.StandErfassen(D(2026, 8, 1), 300m, Standart.Ablesung, "Andy"));
    }

    [Fact]
    public void Fehlender_Endstand_wird_benannt_statt_hochgerechnet()
    {
        var zaehler = Wasserzaehler(D(2025, 12, 31), 100m);
        zaehler.StandErfassen(D(2026, 10, 1), 380m, Standart.Ablesung, "Andy");

        var verbrauch = Verbrauchsrechner.Berechnen([zaehler], Zeitraum.Kalenderjahr(2026));

        Assert.Null(verbrauch.Menge);
        Assert.Contains("fehlt ein Stand um den 31.12.2026", verbrauch.Fehlt, StringComparison.Ordinal);
    }

    [Fact]
    public void Plausibilitaet_meldet_Rueckwaertslauf_und_Ausreisser()
    {
        var zaehler = Wasserzaehler(D(2025, 12, 31), 100m);
        zaehler.StandErfassen(D(2026, 12, 31), 465m, Standart.Ablesung, "Andy"); // 1 m³/Tag

        Assert.Contains("Rückwärtslauf", Assert.Single(zaehler.Pruefen(D(2027, 3, 1), 400m)), StringComparison.Ordinal);
        Assert.Empty(zaehler.Pruefen(D(2027, 3, 1), 524m)); // 59 Tage × 1 m³
        var ausreisser = Assert.Single(zaehler.Pruefen(D(2027, 3, 1), 700m));
        Assert.StartsWith("Ungewöhnlicher Verbrauch", ausreisser, StringComparison.Ordinal);
        Assert.Contains("statt bisher 1", ausreisser, StringComparison.Ordinal);
    }

    [Fact]
    public void Staende_vor_dem_Einbau_oder_doppelt_werden_abgelehnt()
    {
        var zaehler = Wasserzaehler(D(2025, 12, 31), 100m);

        Assert.Throws<RegelverletzungException>(() => zaehler.StandErfassen(D(2025, 12, 1), 90m, Standart.Ablesung, "Andy"));
        Assert.Throws<RegelverletzungException>(() => zaehler.StandErfassen(D(2025, 12, 31), 100m, Standart.Ablesung, "Andy"));
        Assert.Throws<RegelverletzungException>(() => zaehler.StandErfassen(D(2026, 1, 31), -1m, Standart.Ablesung, "Andy"));
        Assert.Throws<RegelverletzungException>(() => zaehler.StandEntfernen(zaehler.Staende[0].Id));
    }

    [Fact]
    public void Stand_am_Tag()
    {
        var zaehler = Wasserzaehler(D(2026, 1, 1), 0m);
        zaehler.StandErfassen(D(2026, 1, 11), 10m, Standart.Ablesung, "Andy");

        Assert.Equal(new StandAmTag(D(2026, 1, 6), 5m, true), Verbrauchsrechner.StandAm(zaehler.Staende, D(2026, 1, 6)));
        Assert.Equal(new StandAmTag(D(2026, 1, 11), 10m, false), Verbrauchsrechner.StandAm(zaehler.Staende, D(2026, 1, 11)));
        Assert.Null(Verbrauchsrechner.StandAm(zaehler.Staende, D(2026, 1, 12)));
    }

    private static ZaehlerEntitaet Wasserzaehler(DateOnly einbau, decimal anfang) =>
        new(Guid.CreateVersion7(), Zaehlertyp.Kaltwasser, "Kaltwasser Bad", "WZ-1", einbau, anfang);
}
