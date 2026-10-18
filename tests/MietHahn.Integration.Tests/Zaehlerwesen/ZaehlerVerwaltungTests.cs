using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Dateien;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Zaehlerwesen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Zaehlerwesen;
using MietHahn.Infrastructure.Zaehlerwesen;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Zaehlerwesen;

public class ZaehlerVerwaltungTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Jahresende = new(2025, 12, 31);

    private InfrastrukturUmgebung umgebung = null!;
    private Guid objektId;
    private Guid wohnung1;
    private Guid wohnung2;
    private Guid haupt;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekte = scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>();
        objektId = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, Ct));
        wohnung1 = Pruefen(await objekte.EinheitAnlegenAsync(objektId, new EinheitDaten { Bezeichnung = "Wohnung 1" }, Ct));
        wohnung2 = Pruefen(await objekte.EinheitAnlegenAsync(objektId, new EinheitDaten { Bezeichnung = "Wohnung 2" }, Ct));
        haupt = await AnlegenAsync("Hauptwasserzähler", "HWZ-1", null, null, 1000m);
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Zaehler_mit_Zuordnung_und_Hauptzaehler_in_Ablesereihenfolge()
    {
        var w1 = await AnlegenAsync("Kaltwasser Bad", "WZ-1", wohnung1, haupt, 10m);
        var w2 = await AnlegenAsync("Kaltwasser Bad", "WZ-2", wohnung2, haupt, 20m);

        var runde = await MitAsync(v => v.RundeAsync(objektId, null, Ct));
        Assert.Equal(["HWZ-1", "WZ-1", "WZ-2"], runde.Select(z => z.Nummer));
        Assert.Equal(["Haus", "Wohnung 1", "Wohnung 2"], runde.Select(z => z.Zuordnung));
        Assert.Equal(["WZ-2"], (await MitAsync(v => v.RundeAsync(objektId, wohnung2, Ct))).Select(z => z.Nummer));

        var details = (await MitAsync(v => v.LadenAsync(haupt, Ct)))!;
        Assert.Equal([w1, w2], details.Unterzaehler.Select(u => u.Id));
    }

    [Fact]
    public async Task Auffaellige_Staende_brauchen_eine_Bestaetigung()
    {
        var zaehler = await AnlegenAsync("Kaltwasser Bad", "WZ-1", wohnung1, haupt, 10m);
        await StandAsync(zaehler, new DateOnly(2026, 12, 31), 375m);

        var rueckwaerts = await MitAsync(v => v.StandErfassenAsync(zaehler, new StandEingabe { Datum = new DateOnly(2027, 2, 1), Wert = 300m }, null, Ct));

        Assert.True(rueckwaerts.BrauchtBestaetigung);
        Assert.Contains("Rückwärtslauf", rueckwaerts.Warnungen.Single(), StringComparison.Ordinal);
        Assert.Equal(2, (await MitAsync(v => v.LadenAsync(zaehler, Ct)))!.Staende.Count);

        var bestaetigt = await MitAsync(v => v.StandErfassenAsync(zaehler, new StandEingabe { Datum = new DateOnly(2027, 2, 1), Wert = 300m, Bestaetigt = true, Notiz = "Zähler klemmte" }, null, Ct));
        Assert.True(bestaetigt.Erfolgreich);
        var letzter = (await MitAsync(v => v.LadenAsync(zaehler, Ct)))!.Staende[0];
        Assert.Equal(("system", "Zähler klemmte"), (letzter.Ableser, letzter.Notiz));
    }

    [Fact]
    public async Task Stand_mit_Foto_als_Nachweis()
    {
        var zaehler = await AnlegenAsync("Kaltwasser Bad", "WZ-1", wohnung1, haupt, 10m);
        Guid foto;
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            foto = (await scope.ServiceProvider.GetRequiredService<IDateiablage>().SpeichernAsync(new MemoryStream(TestDateien.ZweifarbigesBild(40, 30)), "zaehler.jpg", Ct)).Id;
        }

        Assert.True((await MitAsync(v => v.StandErfassenAsync(zaehler, new StandEingabe { Datum = new DateOnly(2026, 6, 30), Wert = 190m }, foto, Ct))).Erfolgreich);

        Assert.Equal(foto, (await MitAsync(v => v.LadenAsync(zaehler, Ct)))!.Staende[0].FotoDateiId);
    }

    [Fact]
    public async Task Zaehlertausch_Verbrauch_bleibt_durchgaengig_und_Unterzaehler_ziehen_mit()
    {
        var w1 = await AnlegenAsync("Kaltwasser Bad", "WZ-1", wohnung1, haupt, 10m);
        await StandAsync(w1, new DateOnly(2026, 12, 31), 110m);
        await StandAsync(haupt, new DateOnly(2026, 6, 1), 1200m);

        var neuerHaupt = Pruefen(await MitAsync(v => v.TauschenAsync(haupt, new TauschEingabe { Datum = new DateOnly(2026, 7, 1), Endstand = 1240m, NeueNummer = "HWZ-2", Anfangsstand = 0m }, Ct)));
        await StandAsync(neuerHaupt, new DateOnly(2026, 12, 31), 260m);

        var verbrauch = (await MitAsync(v => v.VerbrauchAsync(neuerHaupt, Zeitraum.Kalenderjahr(2026), Ct)))!;
        Assert.Equal(240m + 260m, verbrauch.Menge);
        Assert.Equal(verbrauch.Menge, (await MitAsync(v => v.VerbrauchAsync(haupt, Zeitraum.Kalenderjahr(2026), Ct)))!.Menge);

        var details = (await MitAsync(v => v.LadenAsync(neuerHaupt, Ct)))!;
        Assert.Equal(["HWZ-1", "HWZ-2"], details.Messstelle.Select(m => m.Nummer));
        Assert.Equal(w1, details.Unterzaehler.Single().Id);
        Assert.Equal(["HWZ-2", "WZ-1"], (await MitAsync(v => v.ListeAsync(objektId, false, Ct))).Select(z => z.Nummer));
        Assert.Equal(3, (await MitAsync(v => v.ListeAsync(objektId, true, Ct))).Count);
    }

    [Fact]
    public async Task Differenz_Hauptzaehler_zu_Summe_der_Unterzaehler()
    {
        var w1 = await AnlegenAsync("Kaltwasser Bad", "WZ-1", wohnung1, haupt, 10m);
        var w2 = await AnlegenAsync("Kaltwasser Bad", "WZ-2", wohnung2, haupt, 20m);
        // Die Bilanz zeigt das Vorjahr: Uhr auf Februar 2027 → Jahr 2026
        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2027, 2, 1, 10, 0, 0, TimeSpan.Zero));
        await StandAsync(haupt, new DateOnly(2026, 12, 31), 1500m);
        await StandAsync(w1, new DateOnly(2026, 12, 31), 210m);
        await StandAsync(w2, new DateOnly(2026, 12, 31), 260m);

        var bilanz = (await MitAsync(v => v.LadenAsync(haupt, Ct)))!.Bilanz!;

        Assert.Equal(2026, bilanz.Jahr);
        Assert.Equal((500m, 440m, 60m), (bilanz.Hauptzaehler!.Value, bilanz.SummeUnterzaehler!.Value, bilanz.Differenz!.Value));
        Assert.Empty(bilanz.Hinweise);
    }

    [Fact]
    public async Task Zuordnung_und_Hauptzaehler_muessen_zum_Objekt_passen()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekte = scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>();
        var anderes = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Birkenallee 9", Strasse = "Birkenallee 9", Plz = "50668", Ort = "Köln" }, Ct));
        var fremdeWohnung = Pruefen(await objekte.EinheitAnlegenAsync(anderes, new EinheitDaten { Bezeichnung = "Wohnung A" }, Ct));

        var falscheZuordnung = await MitAsync(v => v.AnlegenAsync(objektId, Anlage("X", "X-1", fremdeWohnung, null, 0m), Ct));
        var falscherTyp = await MitAsync(v => v.AnlegenAsync(objektId, new ZaehlerAnlage
        {
            Daten = new ZaehlerDaten { Typ = Zaehlertyp.Strom, Bezeichnung = "Strom", Nummer = "S-1", HauptzaehlerId = haupt },
            EingebautAm = Jahresende,
            Anfangsstand = 0m,
        }, Ct));

        Assert.Equal("Die Zuordnung gehört nicht zu diesem Objekt.", falscheZuordnung.Fehler.Single());
        Assert.Contains("dasselbe messen", falscherTyp.Fehler.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ablaufende_Eichfristen_erscheinen_im_Dashboard()
    {
        // Einbau Ende 2019 → Eichfrist Kaltwasser bis 31.12.2025, bei Testuhr 01.03.2026 abgelaufen
        var alt = Pruefen(await MitAsync(v => v.AnlegenAsync(objektId, new ZaehlerAnlage
        {
            Daten = new ZaehlerDaten { Typ = Zaehlertyp.Kaltwasser, Bezeichnung = "Alter Zähler", Nummer = "ALT-1" },
            EingebautAm = new DateOnly(2019, 12, 1),
            Anfangsstand = 0m,
        }, Ct)));

        await using var scope = umgebung.Services.CreateAsyncScope();
        var quelle = scope.ServiceProvider.GetServices<IAufgabenQuelle>().OfType<EichfristAufgaben>().Single();
        var aufgabe = Assert.Single(await quelle.AufgabenAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Rollen.Admin)], "test")), Ct));

        Assert.Equal("Eichfrist abgelaufen: Alter Zähler (ALT-1)", aufgabe.Titel);
        Assert.Equal($"/verwaltung/zaehler/{alt}", aufgabe.Link);
        Assert.Equal(Dringlichkeit.Dringend, aufgabe.Dringlichkeit);
    }

    private async Task<Guid> AnlegenAsync(string bezeichnung, string nummer, Guid? zuordnung, Guid? hauptzaehler, decimal anfang) =>
        Pruefen(await MitAsync(v => v.AnlegenAsync(objektId, Anlage(bezeichnung, nummer, zuordnung, hauptzaehler, anfang), Ct)));

    private static ZaehlerAnlage Anlage(string bezeichnung, string nummer, Guid? zuordnung, Guid? hauptzaehler, decimal anfang) => new()
    {
        Daten = new ZaehlerDaten { Typ = Zaehlertyp.Kaltwasser, Bezeichnung = bezeichnung, Nummer = nummer, ZuordnungId = zuordnung, HauptzaehlerId = hauptzaehler },
        EingebautAm = Jahresende,
        Anfangsstand = anfang,
    };

    private async Task StandAsync(Guid zaehler, DateOnly datum, decimal wert)
    {
        var ergebnis = await MitAsync(v => v.StandErfassenAsync(zaehler, new StandEingabe { Datum = datum, Wert = wert, Bestaetigt = true }, null, Ct));
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
    }

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IZaehlerVerwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IZaehlerVerwaltung>());
    }
}
