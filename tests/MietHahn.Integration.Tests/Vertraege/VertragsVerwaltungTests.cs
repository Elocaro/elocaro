using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Datenschutz;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Vertraege;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Vertraege;

public class VertragsVerwaltungTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Startzeit der Testumgebung: 01.03.2026
    private static readonly DateOnly Heute = new(2026, 3, 1);

    private InfrastrukturUmgebung umgebung = null!;
    private Guid objektId;
    private Guid wohnung1;
    private Guid wg;
    private Guid zimmer1;
    private Guid anna;
    private Guid ben;
    private Guid buerge;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekte = scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>();
        objektId = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, Ct));
        wohnung1 = Pruefen(await objekte.EinheitAnlegenAsync(objektId, new EinheitDaten { Bezeichnung = "Wohnung 1" }, Ct));
        wg = Pruefen(await objekte.EinheitAnlegenAsync(objektId, new EinheitDaten { Bezeichnung = "Wohnung 4" }, Ct));
        zimmer1 = Pruefen(await objekte.ZimmerAnlegenAsync(wg, new ZimmerDaten { Bezeichnung = "Zimmer 1" }, Ct));
        Pruefen(await objekte.ZimmerAnlegenAsync(wg, new ZimmerDaten { Bezeichnung = "Zimmer 2" }, Ct));

        var personen = scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>();
        anna = Pruefen(await personen.AnlegenAsync(new PersonDaten { Vorname = "Anna", Nachname = "Schmidt" }, null, Ct));
        ben = Pruefen(await personen.AnlegenAsync(new PersonDaten { Vorname = "Ben", Nachname = "Weber" }, null, Ct));
        buerge = Pruefen(await personen.AnlegenAsync(new PersonDaten { Vorname = "Hans", Nachname = "Schmidt" }, null, Ct));
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Vertrag_mit_zwei_Mietern_und_Buerge_anlegen()
    {
        var id = await WohnungsvertragAsync();

        var vertrag = (await MitAsync(v => v.LadenAsync(id, Ct)))!;

        Assert.Equal("Wohnung 1", vertrag.Mietobjekt);
        Assert.Equal("Lindenweg 3", vertrag.Objekt);
        Assert.Equal(Vertragsstatus.Aktiv, vertrag.Status);
        Assert.Equal(["Anna Schmidt", "Ben Weber"], vertrag.Parteien.Where(p => p.Rolle == Parteirolle.Mieter).Select(p => p.Name));
        Assert.Equal("Hans Schmidt", vertrag.Parteien.Single(p => p.Rolle == Parteirolle.Buerge).Name);
        Assert.Equal(650m + 180m + 90m, vertrag.Monatsmiete);
        Assert.Equal(1950m, vertrag.Daten.Kaution);
        Assert.Equal(new DateOnly(2026, 3, 4), vertrag.NaechsteFaelligkeit); // 3. Werktag im März 2026 (1.3. ist ein Sonntag)

        var liste = Assert.Single(await MitAsync(v => v.ListeAsync(true, Ct)));
        Assert.Equal("Anna Schmidt, Ben Weber", liste.Mieter);
    }

    [Fact]
    public async Task Ueberlappende_Vertraege_fuer_dasselbe_Mietobjekt_werden_abgelehnt()
    {
        await WohnungsvertragAsync();

        var zweiter = await MitAsync(v => v.AnlegenAsync(Anlage(wohnung1, new DateOnly(2027, 1, 1), [ben]), Ct));

        Assert.False(zweiter.Erfolgreich);
        Assert.Equal("Für dieses Mietobjekt gibt es in diesem Zeitraum bereits einen Vertrag.", zweiter.Fehler.Single());
    }

    [Fact]
    public async Task WG_wird_nur_zimmerweise_vermietet_Zimmervertraege_ueberlappen_nicht_einander()
    {
        var ganzeWg = await MitAsync(v => v.AnlegenAsync(Anlage(wg, Heute, [anna]), Ct));
        Assert.False(ganzeWg.Erfolgreich);
        Assert.Contains("nur zimmerweise", ganzeWg.Fehler.Single(), StringComparison.Ordinal);

        var auswahl = await MitAsync(v => v.MietobjekteAsync(Ct));
        Assert.Equal(["Wohnung 1", "Wohnung 4, Zimmer 1", "Wohnung 4, Zimmer 2"], auswahl.Select(m => m.Bezeichnung));

        var pauschale = Anlage(zimmer1, Heute, [anna]);
        pauschale.Abrechnungsart = Abrechnungsart.Pauschale;
        pauschale.Kaltmiete = 380m;
        pauschale.Betriebskostenpauschale = 120m;
        Pruefen(await MitAsync(v => v.AnlegenAsync(pauschale, Ct)));

        // weiteres Zimmer in der WG trotz bestehender Zimmerverträge anlegbar
        await using var scope = umgebung.Services.CreateAsyncScope();
        Pruefen(await scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>().ZimmerAnlegenAsync(wg, new ZimmerDaten { Bezeichnung = "Zimmer 3" }, Ct));
    }

    [Fact]
    public async Task Pauschale_ohne_Pauschalbetrag_wird_abgelehnt()
    {
        var anlage = Anlage(zimmer1, Heute, [anna]);
        anlage.Abrechnungsart = Abrechnungsart.Pauschale;

        var ergebnis = await MitAsync(v => v.AnlegenAsync(anlage, Ct));

        Assert.Equal("Bei der Abrechnungsart „Pauschale“ bitte die Betriebskostenpauschale angeben.", ergebnis.Fehler.Single());
    }

    [Fact]
    public async Task Mieterhoehung_und_Personenanzahl_bleiben_historisch_erhalten()
    {
        var id = await WohnungsvertragAsync();

        Pruefen(await MitAsync(v => v.BestandteilSetzenAsync(id, new BestandteilEingabe { Art = Bestandteilart.Kaltmiete, Ab = new DateOnly(2027, 3, 1), Betrag = 700m }, Ct)));
        Pruefen(await MitAsync(v => v.PersonenSetzenAsync(id, new PersonenEingabe { Ab = new DateOnly(2026, 9, 1), Anzahl = 3 }, Ct)));

        var vertrag = (await MitAsync(v => v.LadenAsync(id, Ct)))!;
        var kaltmieten = vertrag.Bestandteile.Where(b => b.Art == Bestandteilart.Kaltmiete).ToList();
        Assert.Equal([700m, 650m], kaltmieten.Select(k => k.Betrag));
        Assert.Equal(new DateOnly(2027, 2, 28), kaltmieten[1].Gueltigkeit.Bis);
        Assert.Equal(650m, vertrag.AktuelleBestandteile.Single(b => b.Art == Bestandteilart.Kaltmiete).Betrag);
        Assert.Equal([3, 2], vertrag.Personen.Select(p => p.Anzahl));
    }

    [Fact]
    public async Task Kuendigung_durch_den_Mieter_berechnet_den_Termin_und_Leerstand_folgt()
    {
        var id = await WohnungsvertragAsync();

        // Zugang vor dem 3. Werktag (04.03.2026) → Ende 31.05.2026
        Pruefen(await MitAsync(v => v.KuendigenAsync(id, new KuendigungEingabe { EingegangenAm = new DateOnly(2026, 3, 3), Durch = Kuendigender.Mieter }, Ct)));

        var vertrag = (await MitAsync(v => v.LadenAsync(id, Ct)))!;
        Assert.Equal(new DateOnly(2026, 5, 31), vertrag.Laufzeit.Bis);
        Assert.Equal(Vertragsstatus.Gekuendigt, vertrag.Status);

        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekt = (await scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>().LadenAsync(objektId, Ct))!;
        Assert.Null(objekt.Einheiten.Single(e => e.Id == wohnung1).Mietobjekte.Single().Belegung);
        var historie = await MitAsync(v => v.HistorieAsync(wohnung1, Ct));
        Assert.True(historie[0].IstLeerstand);
        Assert.Equal(new DateOnly(2026, 6, 1), historie[0].Zeitraum.Von);
        Assert.Empty(await MitAsync(v => v.ListeAsync(true, Ct)));
    }

    [Fact]
    public async Task Mieterwechsel_Auszug_mit_neuer_Anschrift_dann_Einzug_mit_Leerstand()
    {
        var id = await WohnungsvertragAsync();

        Pruefen(await MitAsync(v => v.AuszugAsync(id, new AuszugEingabe { Ende = new DateOnly(2026, 6, 30), NeueStrasse = "Neue Straße 5", NeuePlz = "10115", NeuerOrt = "Berlin" }, Ct)));
        Pruefen(await MitAsync(v => v.AnlegenAsync(Anlage(wohnung1, new DateOnly(2026, 8, 1), [buerge]), Ct)));

        var historie = await MitAsync(v => v.HistorieAsync(wohnung1, Ct));
        Assert.Equal(3, historie.Count);
        Assert.Equal("Hans Schmidt", historie[0].Mieter);
        Assert.True(historie[1].IstLeerstand);
        Assert.Equal(new Domain.Gemeinsam.Zeitraum(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), historie[1].Zeitraum);
        Assert.Equal("Anna Schmidt, Ben Weber", historie[2].Mieter);

        await using var scope = umgebung.Services.CreateAsyncScope();
        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2026, 7, 5, 10, 0, 0, TimeSpan.Zero));
        var annaAkte = (await scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>().LadenAsync(anna, Ct))!;
        Assert.Equal("Neue Straße 5, 10115 Berlin", annaAkte.Anschrift!.Einzeilig);
    }

    [Fact]
    public async Task Vertraege_erscheinen_als_Personenbezug_und_in_der_DSGVO_Auskunft()
    {
        var id = await WohnungsvertragAsync();
        await using var scope = umgebung.Services.CreateAsyncScope();

        var personen = scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>();
        Assert.Equal(["Mieter"], (await personen.SucheAsync(new PersonenSuche("Weber"), Ct)).Single().Rollen);
        var bezug = (await personen.LadenAsync(buerge, Ct))!.Bezuege.Single();
        Assert.Equal("Bürge", bezug.Rolle);
        Assert.True(bezug.Aktiv);
        Assert.Equal($"/verwaltung/vertraege/{id}", bezug.Link);
        Assert.False((await personen.LoeschenAsync(ben, Ct)).Erfolgreich);

        var auskunft = JsonDocument.Parse((await scope.ServiceProvider.GetRequiredService<IDatenschutz>().AuskunftAsync(anna, Ct))!).RootElement;
        var vertrag = auskunft.GetProperty("mietvertraege")[0];
        Assert.Equal("Wohnung 1", vertrag.GetProperty("mietobjekt").GetString());
        Assert.Equal(650m, vertrag.GetProperty("bestandteile").EnumerateArray().Single(b => b.GetProperty("art").GetString() == "Kaltmiete").GetProperty("betrag").GetDecimal());

        Assert.False((await scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>().EinheitLoeschenAsync(wohnung1, Ct)).Erfolgreich);
    }

    [Fact]
    public async Task Belegung_erscheint_in_der_Objektuebersicht()
    {
        await WohnungsvertragAsync();
        await using var scope = umgebung.Services.CreateAsyncScope();

        var objekt = (await scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>().LadenAsync(objektId, Ct))!;
        var belegung = objekt.Einheiten.Single(e => e.Id == wohnung1).Mietobjekte.Single().Belegung!;
        Assert.Equal("Anna Schmidt, Ben Weber", belegung.Mieter);
        Assert.Equal(Heute, belegung.Beginn);
        var uebersicht = (await scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>().ListeAsync(null, Ct)).Single();
        Assert.Equal(1, uebersicht.Vermietet);
        Assert.Equal(2, uebersicht.Leerstand);
    }

    private async Task<Guid> WohnungsvertragAsync()
    {
        var anlage = Anlage(wohnung1, Heute, [anna, ben]);
        anlage.BuergenIds = [buerge];
        anlage.Daten.Kaution = 1950m;
        return Pruefen(await MitAsync(v => v.AnlegenAsync(anlage, Ct)));
    }

    private static VertragAnlage Anlage(Guid mietobjekt, DateOnly beginn, List<Guid> mieter) => new()
    {
        MietobjektId = mietobjekt,
        Beginn = beginn,
        MieterIds = mieter,
        Kaltmiete = 650m,
        BetriebskostenVorauszahlung = 180m,
        HeizkostenVorauszahlung = 90m,
        Personen = 2,
    };

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IVertragsVerwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IVertragsVerwaltung>());
    }
}
