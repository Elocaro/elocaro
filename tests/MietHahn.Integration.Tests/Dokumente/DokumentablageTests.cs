using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Dateien;
using MietHahn.Application.Dokumente;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Dokumente;
using MietHahn.Domain.Personen;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Dokumente;

public class DokumentablageTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Startzeit der Testumgebung: 01.03.2026
    private static readonly DateOnly Heute = new(2026, 3, 1);

    private InfrastrukturUmgebung umgebung = null!;
    private Guid haus;
    private Guid anderesHaus;
    private Guid wohnung1;
    private Guid wohnung2;
    private Guid anna;
    private Guid ben;
    private Guid vertragAnna;
    private Guid vertragBen;
    private ClaimsPrincipal annaKonto = null!;
    private ClaimsPrincipal benKonto = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekte = scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>();
        haus = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, Ct));
        anderesHaus = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Birkenallee 9", Strasse = "Birkenallee 9", Plz = "50668", Ort = "Köln" }, Ct));
        wohnung1 = Pruefen(await objekte.EinheitAnlegenAsync(haus, new EinheitDaten { Bezeichnung = "Wohnung 1" }, Ct));
        wohnung2 = Pruefen(await objekte.EinheitAnlegenAsync(haus, new EinheitDaten { Bezeichnung = "Wohnung 2" }, Ct));

        var personen = scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>();
        anna = Pruefen(await personen.AnlegenAsync(new PersonDaten { Vorname = "Anna", Nachname = "Schmidt" }, null, Ct));
        ben = Pruefen(await personen.AnlegenAsync(new PersonDaten { Vorname = "Ben", Nachname = "Weber" }, null, Ct));

        var vertraege = scope.ServiceProvider.GetRequiredService<IVertragsVerwaltung>();
        vertragAnna = Pruefen(await vertraege.AnlegenAsync(new VertragAnlage { MietobjektId = wohnung1, Beginn = Heute, MieterIds = [anna], Kaltmiete = 650m }, Ct));
        vertragBen = Pruefen(await vertraege.AnlegenAsync(new VertragAnlage { MietobjektId = wohnung2, Beginn = Heute, MieterIds = [ben], Kaltmiete = 700m }, Ct));

        annaKonto = await MieterKontoAsync(anna, "anna@example.de");
        benKonto = await MieterKontoAsync(ben, "ben@example.de");
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Fuer_Mieter_freigegebene_Dokumente_sehen_nur_die_Mieter_des_Vertrags_bzw_Objekts()
    {
        var mietvertrag = await DokumentAsync("Mietvertrag Anna", new DokumentDaten { MietvertragId = vertragAnna, FuerMieterFreigegeben = true });
        var hausordnung = await DokumentAsync("Hausordnung", new DokumentDaten { ObjektId = haus, FuerMieterFreigegeben = true });
        var protokoll = await DokumentAsync("Übergabeprotokoll Wohnung 1", new DokumentDaten { EinheitId = wohnung1, FuerMieterFreigegeben = true });
        await DokumentAsync("Interne Notiz", new DokumentDaten { MietvertragId = vertragAnna });
        await DokumentAsync("Hausordnung Birkenallee", new DokumentDaten { ObjektId = anderesHaus, FuerMieterFreigegeben = true });

        Assert.Equal(
            ["Hausordnung", "Mietvertrag Anna", "Übergabeprotokoll Wohnung 1"],
            (await MitAsync(a => a.FreigegebeneAsync(annaKonto, null, Ct))).Select(d => d.Titel).Order(StringComparer.Ordinal));
        Assert.Equal(["Hausordnung"], (await MitAsync(a => a.FreigegebeneAsync(benKonto, null, Ct))).Select(d => d.Titel));

        // Gleiche Regel für den Dateiabruf über /dateien/{id}
        Assert.True(await IstFreigegebenAsync(mietvertrag.DateiId, annaKonto));
        Assert.False(await IstFreigegebenAsync(mietvertrag.DateiId, benKonto));
        Assert.True(await IstFreigegebenAsync(hausordnung.DateiId, benKonto));
        Assert.False(await IstFreigegebenAsync(protokoll.DateiId, benKonto));
    }

    [Fact]
    public async Task Nach_dem_Auszug_sieht_der_Mieter_nur_noch_Dokumente_seines_Vertrags()
    {
        await DokumentAsync("Hausordnung", new DokumentDaten { ObjektId = haus, FuerMieterFreigegeben = true });
        await DokumentAsync("Mietvertrag Anna", new DokumentDaten { MietvertragId = vertragAnna, FuerMieterFreigegeben = true });
        Pruefen(await Mit<IVertragsVerwaltung, Ergebnis>(v => v.AuszugAsync(vertragAnna, new AuszugEingabe { Ende = new DateOnly(2026, 6, 30) }, Ct)));

        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(["Mietvertrag Anna"], (await MitAsync(a => a.FreigegebeneAsync(annaKonto, null, Ct))).Select(d => d.Titel));
    }

    [Fact]
    public async Task Hausmeister_sieht_nur_freigegebene_Dokumente_zugeordneter_Objekte()
    {
        var hausmeisterId = await KontoAsync("hm@example.de", Rollen.Hausmeister);
        Pruefen(await Mit<IObjektZuordnungen, Ergebnis>(z => z.SetzenAsync(hausmeisterId, [haus], Ct)));
        var hausmeister = Konto(hausmeisterId, Rollen.Hausmeister);
        var wartung = await DokumentAsync("Wartungsvertrag Heizung", new DokumentDaten { ObjektId = haus, FuerHausmeisterFreigegeben = true });
        await DokumentAsync("Grundriss Wohnung 2", new DokumentDaten { EinheitId = wohnung2, FuerHausmeisterFreigegeben = true });
        await DokumentAsync("Hausordnung", new DokumentDaten { ObjektId = haus, FuerMieterFreigegeben = true });
        await DokumentAsync("Wartung Birkenallee", new DokumentDaten { ObjektId = anderesHaus, FuerHausmeisterFreigegeben = true });

        Assert.Equal(
            ["Grundriss Wohnung 2", "Wartungsvertrag Heizung"],
            (await MitAsync(a => a.FreigegebeneAsync(hausmeister, haus, Ct))).Select(d => d.Titel).Order(StringComparer.Ordinal));
        Assert.Empty(await MitAsync(a => a.FreigegebeneAsync(hausmeister, anderesHaus, Ct)));
        Assert.True(await IstFreigegebenAsync(wartung.DateiId, hausmeister));
        Assert.Equal(["Hausordnung"], (await MitAsync(a => a.FreigegebeneAsync(annaKonto, null, Ct))).Select(d => d.Titel));
    }

    [Fact]
    public async Task Suche_nach_Titel_Schlagwort_Dateiname_und_Bezug()
    {
        await DokumentAsync("Gasrechnung 2025", new DokumentDaten { ObjektId = haus, Kategorie = Dokumentkategorie.Rechnung, Tags = "Heizung, Gas" }, "stadtwerke_2025.pdf");
        await DokumentAsync("Mietvertrag", new DokumentDaten { MietvertragId = vertragAnna, Kategorie = Dokumentkategorie.Vertrag });
        await DokumentAsync("Personalausweis Kopie", new DokumentDaten { PersonId = ben, Notiz = "Nach Einzug vernichten" });

        Assert.Equal(["Gasrechnung 2025"], await TitelAsync(new DokumentSuche("heizung")));
        Assert.Equal(["Gasrechnung 2025"], await TitelAsync(new DokumentSuche("STADTWERKE")));
        Assert.Equal(["Personalausweis Kopie"], await TitelAsync(new DokumentSuche("vernichten")));
        Assert.Equal(["Mietvertrag"], await TitelAsync(new DokumentSuche(Kategorie: Dokumentkategorie.Vertrag)));
        Assert.Empty(await TitelAsync(new DokumentSuche("100%")));

        // Dokumente eines Vertrags gehören auch zur Einheit und zum Objekt; Personen sehen die ihrer Verträge.
        Assert.Equal(2, (await TitelAsync(new DokumentSuche(ObjektId: haus))).Count);
        Assert.Equal(["Mietvertrag"], await TitelAsync(new DokumentSuche(EinheitId: wohnung1)));
        Assert.Equal(["Mietvertrag"], await TitelAsync(new DokumentSuche(PersonId: anna)));
        Assert.Equal(["Personalausweis Kopie"], await TitelAsync(new DokumentSuche(PersonId: ben)));
    }

    [Fact]
    public async Task Bezug_ist_Pflicht_und_Tags_werden_bereinigt()
    {
        var ohneBezug = await AnlegenAsync("Lose Datei", new DokumentDaten());
        Assert.False(ohneBezug.Erfolgreich);
        Assert.Contains("mindestens einen Bezug", ohneBezug.Fehler.Single(), StringComparison.Ordinal);

        var unbekannt = await AnlegenAsync("Falsch", new DokumentDaten { ObjektId = Guid.CreateVersion7() });
        Assert.False(unbekannt.Erfolgreich);

        var dokument = await DokumentAsync("Versicherungspolice", new DokumentDaten { ObjektId = haus, Tags = " Gebäude ; gebäude, Sturm,, " });
        Assert.Equal(["Gebäude", "Sturm"], dokument.Tags);

        var details = (await MitAsync(a => a.LadenAsync(dokument.Id, Ct)))!;
        details.Daten.Titel = "Gebäudeversicherung";
        details.Daten.MietvertragId = vertragBen;
        Pruefen(await MitAsync(a => a.AendernAsync(dokument.Id, details.Daten, Ct)));
        var geaendert = (await MitAsync(a => a.LadenAsync(dokument.Id, Ct)))!;
        Assert.Equal("Gebäudeversicherung", geaendert.Dokument.Titel);
        Assert.Equal("Gebäude, Sturm", geaendert.Daten.Tags);
        Assert.Contains("Lindenweg 3", geaendert.Dokument.Bezug, StringComparison.Ordinal);
        Assert.Contains("Vertrag Wohnung 2", geaendert.Dokument.Bezug, StringComparison.Ordinal);

        Pruefen(await MitAsync(a => a.LoeschenAsync(dokument.Id, Ct)));
        Assert.Null(await MitAsync(a => a.LadenAsync(dokument.Id, Ct)));
    }

    [Fact]
    public async Task Ablaufende_Dokumente_erscheinen_auf_dem_Dashboard()
    {
        await DokumentAsync("Energieausweis", new DokumentDaten { ObjektId = haus, Kategorie = Dokumentkategorie.Energieausweis, AblaufAm = Heute.AddDays(30) });
        await DokumentAsync("Alte Police", new DokumentDaten { ObjektId = haus, AblaufAm = Heute.AddDays(-1) });
        await DokumentAsync("Neue Police", new DokumentDaten { ObjektId = haus, AblaufAm = Heute.AddYears(1) });

        await using var scope = umgebung.Services.CreateAsyncScope();
        var quelle = scope.ServiceProvider.GetServices<IAufgabenQuelle>().Single(q => q.GetType().Name == "DokumentAblaufAufgaben");
        var aufgaben = await quelle.AufgabenAsync(Konto(Guid.CreateVersion7(), Rollen.Admin), Ct);

        Assert.Equal(["Abgelaufen: Alte Police", "Läuft ab: Energieausweis"], aufgaben.Select(a => a.Titel));
        Assert.Equal(Dringlichkeit.Dringend, aufgaben[0].Dringlichkeit);
        Assert.Empty(await quelle.AufgabenAsync(benKonto, Ct));
    }

    private async Task<List<string>> TitelAsync(DokumentSuche suche) => (await MitAsync(a => a.SucheAsync(suche, Ct))).Select(d => d.Titel).ToList();

    private async Task<DokumentZeile> DokumentAsync(string titel, DokumentDaten daten, string dateiname = "dokument.pdf")
    {
        var id = Pruefen(await AnlegenAsync(titel, daten, dateiname));
        return (await MitAsync(a => a.LadenAsync(id, Ct)))!.Dokument;
    }

    private async Task<Ergebnis> AnlegenAsync(string titel, DokumentDaten daten, string dateiname = "dokument.pdf")
    {
        daten.Titel = titel;
        await using var scope = umgebung.Services.CreateAsyncScope();
        var datei = await scope.ServiceProvider.GetRequiredService<IDateiablage>().SpeichernAsync(new MemoryStream(TestDateien.Pdf(titel)), dateiname, Ct);
        return await scope.ServiceProvider.GetRequiredService<IDokumentablage>().AnlegenAsync(datei.Id, daten, Ct);
    }

    private async Task<bool> IstFreigegebenAsync(Guid dateiId, ClaimsPrincipal benutzer)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        foreach (var freigabe in scope.ServiceProvider.GetServices<IDateiFreigabe>())
        {
            if (await freigabe.IstFreigegebenAsync(dateiId, benutzer, Ct))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<ClaimsPrincipal> MieterKontoAsync(Guid personId, string email)
    {
        var id = await KontoAsync(email, Rollen.Mieter);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var person = await db.Set<Person>().SingleAsync(p => p.Id == personId, Ct);
        person.BenutzerId = id;
        await db.SaveChangesAsync(Ct);
        return Konto(id, Rollen.Mieter);
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

    private static ClaimsPrincipal Konto(Guid id, string rolle) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, rolle)], "test"));

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private Task<T> MitAsync<T>(Func<IDokumentablage, Task<T>> aktion) => Mit<IDokumentablage, T>(aktion);

    private async Task<T> Mit<TDienst, T>(Func<TDienst, Task<T>> aktion)
        where TDienst : notnull
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<TDienst>());
    }
}
