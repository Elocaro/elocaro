using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Domain.Objekte;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Objekte;

public class ObjektVerwaltungTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Heute = DateOnly.FromDateTime(InfrastrukturUmgebung.Startzeit.UtcDateTime);

    private InfrastrukturUmgebung umgebung = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Mfh_mit_vier_Wohnungen_und_WG_ist_anlegbar()
    {
        var objektId = await BeispielhausAsync();

        var details = (await MitAsync(v => v.LadenAsync(objektId, Ct)))!;

        Assert.Equal("Lindenweg 3, 50667 Köln", details.Anschrift);
        Assert.Equal(4, details.Einheiten.Count);
        var wg = details.Einheiten.Single(e => e.IstWg);
        Assert.Equal(["Zimmer 1", "Zimmer 2", "Zimmer 3", "Zimmer 4"], wg.Mietobjekte.Select(m => m.Bezeichnung));
        Assert.All(wg.Mietobjekte, m => Assert.True(m.IstZimmer));
        Assert.Equal(400m, details.Flaeche!.Flaeche);
        Assert.Empty(details.Pruefhinweise);

        var uebersicht = Assert.Single(await MitAsync(v => v.ListeAsync(null, Ct)));
        Assert.Equal(4, uebersicht.Einheiten);
        Assert.Equal(7, uebersicht.Mietobjekte); // 3 Wohnungen + 4 Zimmer
        Assert.Equal(7, uebersicht.Leerstand);
        Assert.Equal(75.5m + 68m + 92m + 110m, uebersicht.Wohnflaeche);
    }

    [Fact]
    public async Task Flaechenaenderung_schreibt_fort_und_erhaelt_die_Historie()
    {
        var objektId = await BeispielhausAsync();
        var einheitId = (await MitAsync(v => v.LadenAsync(objektId, Ct)))!.Einheiten[0].Id;

        Pruefen(await MitAsync(v => v.EinheitFlaecheSetzenAsync(einheitId, new FlaechenEingabe { Ab = Heute.AddDays(10), Flaeche = 80m, Zusatz = 78m }, Ct)));

        var einheit = (await MitAsync(v => v.EinheitLadenAsync(einheitId, Ct)))!;
        Assert.Equal(75.5m, einheit.Flaeche!.Flaeche); // heute noch die alte Fläche
        Assert.Equal(2, einheit.Flaechenhistorie.Count);
        Assert.Equal(new(80m, 78m), (einheit.Flaechenhistorie[0].Flaeche, einheit.Flaechenhistorie[0].Zusatz));
        Assert.Equal(Heute.AddDays(9), einheit.Flaechenhistorie[1].Gueltigkeit.Bis);

        var rueckwirkend = await MitAsync(v => v.EinheitFlaecheSetzenAsync(einheitId, new FlaechenEingabe { Ab = Heute.AddDays(-30), Flaeche = 70m }, Ct));
        Assert.False(rueckwirkend.Erfolgreich);
        Assert.Contains("Stichtag", rueckwirkend.Fehler[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aenderungen_stehen_im_Audit_Log()
    {
        var objektId = await BeispielhausAsync();
        var daten = (await MitAsync(v => v.LadenAsync(objektId, Ct)))!.Daten;
        daten.Baujahr = 1972;

        Pruefen(await MitAsync(v => v.AendernAsync(objektId, daten, Ct)));

        await using var scope = umgebung.Services.CreateAsyncScope();
        var eintraege = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEintrag>()
            .Where(a => a.EntitaetTyp == nameof(Objekt) && a.EntitaetId == objektId).ToListAsync(Ct);
        Assert.Contains(eintraege, e => e.AenderungenJson.Contains("1972", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Doppelte_Einheit_wird_mit_Meldung_abgelehnt()
    {
        var objektId = await BeispielhausAsync();

        var ergebnis = await MitAsync(v => v.EinheitAnlegenAsync(objektId, new EinheitDaten { Bezeichnung = "Wohnung 2" }, Ct));

        Assert.False(ergebnis.Erfolgreich);
        Assert.Equal("Im Objekt gibt es bereits eine Einheit „Wohnung 2“.", ergebnis.Fehler.Single());
    }

    [Fact]
    public async Task Loeschen_nur_von_unten_nach_oben()
    {
        var objektId = await BeispielhausAsync();
        var wg = (await MitAsync(v => v.LadenAsync(objektId, Ct)))!.Einheiten.Single(e => e.IstWg);

        Assert.False((await MitAsync(v => v.LoeschenAsync(objektId, Ct))).Erfolgreich);
        Assert.False((await MitAsync(v => v.EinheitLoeschenAsync(wg.Id, Ct))).Erfolgreich);

        foreach (var zimmer in wg.Mietobjekte)
        {
            Pruefen(await MitAsync(v => v.ZimmerLoeschenAsync(zimmer.Id, Ct)));
        }

        Pruefen(await MitAsync(v => v.EinheitLoeschenAsync(wg.Id, Ct)));
        var danach = (await MitAsync(v => v.LadenAsync(objektId, Ct)))!;
        Assert.Equal(3, danach.Einheiten.Count);

        // Logisch gelöscht: Daten bleiben für Historie/Audit erhalten.
        await using var scope = umgebung.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.True(await db.Set<Zimmer>().IgnoreQueryFilters().CountAsync(z => z.EinheitId == wg.Id && z.Geloescht, Ct) == 4);
    }

    [Fact]
    public async Task Hausmeister_bekommt_nur_zugeordnete_Objekte()
    {
        var hausA = await BeispielhausAsync();
        var hausB = Pruefen(await MitAsync(v => v.AnlegenAsync(new ObjektDaten { Bezeichnung = "Birkenallee 9", Strasse = "Birkenallee 9", Plz = "50668", Ort = "Köln" }, Ct)));
        var hausmeister = await KontoAsync("hm@example.de", Rollen.Hausmeister);
        var mieter = await KontoAsync("m@example.de", Rollen.Mieter);

        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            var zuordnungen = scope.ServiceProvider.GetRequiredService<IObjektZuordnungen>();
            Pruefen(await zuordnungen.SetzenAsync(hausmeister, [hausB], Ct));
            Assert.False((await zuordnungen.SetzenAsync(mieter, [hausA], Ct)).Erfolgreich);
            Assert.False((await zuordnungen.SetzenAsync(hausmeister, [Guid.CreateVersion7()], Ct)).Erfolgreich);

            Assert.Equal([hausB], await zuordnungen.ObjekteVonAsync(hausmeister, Ct));
            Assert.True(await zuordnungen.IstZugeordnetAsync(hausmeister, hausB, Ct));
            Assert.False(await zuordnungen.IstZugeordnetAsync(hausmeister, hausA, Ct));

            // Ersetzen und wieder hinzufügen (eindeutiger Index ignoriert gelöschte Zuordnungen)
            Pruefen(await zuordnungen.SetzenAsync(hausmeister, [hausA], Ct));
            Pruefen(await zuordnungen.SetzenAsync(hausmeister, [hausA, hausB], Ct));
            Assert.Equal(2, (await zuordnungen.ObjekteVonAsync(hausmeister, Ct)).Count);
        }

        var sichtbar = await MitAsync(v => v.ListeAsync([hausB], Ct));
        Assert.Equal("Birkenallee 9", Assert.Single(sichtbar).Bezeichnung);
    }

    private async Task<Guid> BeispielhausAsync()
    {
        var objektId = Pruefen(await MitAsync(v => v.AnlegenAsync(new ObjektDaten
        {
            Bezeichnung = "Lindenweg 3",
            Strasse = "Lindenweg 3",
            Plz = "50667",
            Ort = "Köln",
            Baujahr = 1965,
            Heizungsart = Heizungsart.Zentralheizung,
            Energietraeger = Energietraeger.Erdgas,
        }, Ct)));
        Pruefen(await MitAsync(v => v.FlaecheSetzenAsync(objektId, new FlaechenEingabe { Ab = Heute, Flaeche = 400m, Zusatz = 30m }, Ct)));

        var flaechen = new[] { 75.5m, 68m, 92m, 110m };
        for (var i = 0; i < 4; i++)
        {
            var einheit = Pruefen(await MitAsync(v => v.EinheitAnlegenAsync(objektId, new EinheitDaten { Bezeichnung = $"Wohnung {i + 1}", Lage = i < 2 ? "EG" : "OG" }, Ct)));
            Pruefen(await MitAsync(v => v.EinheitFlaecheSetzenAsync(einheit, new FlaechenEingabe { Ab = Heute, Flaeche = flaechen[i] }, Ct)));
            if (i == 3)
            {
                for (var z = 1; z <= 4; z++)
                {
                    var zimmer = Pruefen(await MitAsync(v => v.ZimmerAnlegenAsync(einheit, new ZimmerDaten { Bezeichnung = $"Zimmer {z}", Moeblierung = Moeblierung.Moebliert }, Ct)));
                    Pruefen(await MitAsync(v => v.ZimmerFlaecheSetzenAsync(zimmer, new FlaechenEingabe { Ab = Heute, Flaeche = 14m, Zusatz = 8m }, Ct)));
                }
            }
        }

        return objektId;
    }

    private async Task<Guid> KontoAsync(string email, string rolle)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<Benutzer>>();
        var benutzer = new Benutzer { UserName = email, Email = email, Anzeigename = email };
        Assert.True((await manager.CreateAsync(benutzer)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(benutzer, rolle)).Succeeded);
        return benutzer.Id;
    }

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IObjektVerwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>());
    }
}
