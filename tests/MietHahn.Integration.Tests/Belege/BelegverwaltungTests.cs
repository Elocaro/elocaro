using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Belege;
using MietHahn.Application.Dateien;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Domain.Belege;
using MietHahn.Domain.Personen;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Belege;

public class BelegverwaltungTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Rollen.Admin)], "test"));

    private InfrastrukturUmgebung umgebung = null!;
    private Guid haus;
    private Guid anderesHaus;
    private Guid wohnung;
    private Guid hausmeisterService;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Services.CreateAsyncScope();
        var objekte = scope.ServiceProvider.GetRequiredService<IObjektVerwaltung>();
        haus = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, Ct));
        anderesHaus = Pruefen(await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Birkenallee 9", Strasse = "Birkenallee 9", Plz = "50668", Ort = "Köln" }, Ct));
        wohnung = Pruefen(await objekte.EinheitAnlegenAsync(haus, new EinheitDaten { Bezeichnung = "Wohnung 1" }, Ct));
        hausmeisterService = Pruefen(await scope.ServiceProvider.GetRequiredService<IPersonenVerwaltung>().AnlegenAsync(
            new PersonDaten { Nachname = "Grünpflege Meier", Firma = "Meier GmbH", Kontaktart = Kontaktart.Dienstleister }, null, Ct));
    }

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Rechnung_in_zwei_Kostenarten_aufteilen_Summe_wird_geprueft()
    {
        var datei = await DateiAsync();
        var id = Pruefen(await MitAsync(b => b.AnlegenAsync(new BelegDaten
        {
            LieferantId = hausmeisterService,
            Rechnungsnummer = "R-2026-117",
            Rechnungsdatum = new DateOnly(2026, 4, 30),
            LeistungVon = new DateOnly(2026, 1, 1),
            LeistungBis = new DateOnly(2026, 3, 31),
            Brutto = 595m,
            Netto = 500m,
            Umsatzsteuer = 95m,
            FaelligAm = new DateOnly(2026, 5, 14),
        }, [datei], null, Ct)));

        Pruefen(await MitAsync(b => b.PositionHinzufuegenAsync(id, new PositionEingabe
        {
            Kostenart = "betrkv-14",
            Betrag = 357m,
            ObjektId = haus,
            Paragraf35a = Paragraf35a.HaushaltsnaheDienstleistung,
            Arbeitskosten = 357m,
        }, Ct)));
        var zuViel = await MitAsync(b => b.PositionHinzufuegenAsync(id, new PositionEingabe { Kostenart = "betrkv-10", Betrag = 300m, ObjektId = haus }, Ct));
        Assert.Contains("übersteigen den Rechnungsbetrag (offen: 238.00 €)", zuViel.Fehler.Single(), StringComparison.Ordinal);
        // Ohne Betrag: der Rest
        Pruefen(await MitAsync(b => b.PositionHinzufuegenAsync(id, new PositionEingabe { Kostenart = "betrkv-10", ObjektId = haus }, Ct)));

        var beleg = (await MitAsync(b => b.LadenAsync(id, Ct)))!;
        Assert.Equal((595m, 0m), (beleg.Aufgeteilt, beleg.Offen));
        Assert.Equal([("Hauswart", 357m), ("Gartenpflege", 238m)], beleg.Positionen.Select(p => (p.KostenartText, p.Betrag)));
        Assert.All(beleg.Positionen, p => Assert.Equal(new DateOnly(2026, 1, 1), p.Abrechnungszeitraum.Von)); // aus dem Leistungszeitraum
        Assert.All(beleg.Positionen, p => Assert.True(p.Umlagefaehig));
        Assert.Equal("Meier GmbH (Grünpflege Meier)", beleg.Lieferant);
        Assert.Single(beleg.Dateien);

        // Brutto unter die Summe der Positionen ist nicht möglich.
        beleg.Daten.Brutto = 500m;
        beleg.Daten.Netto = null;
        beleg.Daten.Umsatzsteuer = null;
        Assert.False((await MitAsync(b => b.AendernAsync(id, beleg.Daten, Ct))).Erfolgreich);
    }

    [Fact]
    public async Task Belegliste_filtert_nach_Jahr_Objekt_Kostenart_Zahlung_und_Aufteilung()
    {
        var grundsteuer = await BelegAsync("Stadt Köln", new DateOnly(2026, 2, 15), 480m, "betrkv-01", haus, bezahlt: new DateOnly(2026, 2, 15));
        var reparatur = await BelegAsync("Sanitär Schulz", new DateOnly(2025, 11, 3), 230m, "instandhaltung", anderesHaus);
        var offen = await BelegAsync("Versicherung AG", new DateOnly(2026, 1, 10), 900m, null, null);

        Assert.Equal([grundsteuer, offen], await IdsAsync(new BelegSuche(Jahr: 2026)));
        Assert.Equal([grundsteuer], await IdsAsync(new BelegSuche(ObjektId: haus)));
        Assert.Equal([reparatur], await IdsAsync(new BelegSuche(Kostenart: "instandhaltung")));
        Assert.Equal([offen, reparatur], await IdsAsync(new BelegSuche(OhneZahlung: true)));
        Assert.Equal([offen], await IdsAsync(new BelegSuche(NichtAufgeteilt: true)));
        Assert.Equal([reparatur], await IdsAsync(new BelegSuche(Text: "schulz")));

        var zeile = (await MitAsync(b => b.SucheAsync(new BelegSuche(Kostenart: "instandhaltung"), Ct))).Single();
        Assert.Equal(["Instandhaltung und Reparaturen"], zeile.Kostenarten);
        Assert.Equal(["Birkenallee 9"], zeile.Objekte);
    }

    [Fact]
    public async Task Brennstoffbeleg_mit_Menge_und_CO2()
    {
        var id = Pruefen(await MitAsync(b => b.AnlegenAsync(new BelegDaten
        {
            LieferantName = "Heizöl Müller",
            Rechnungsdatum = new DateOnly(2026, 10, 1),
            Brutto = 3150m,
            Brennstoffmenge = 3000m,
            Brennstoffeinheit = "l",
            Co2Kg = 7980m,
        }, [], new PositionEingabe { Kostenart = "betrkv-04", ObjektId = haus }, Ct)));

        var beleg = (await MitAsync(b => b.LadenAsync(id, Ct)))!;
        Assert.Equal((3000m, "l", 7980m), (beleg.Daten.Brennstoffmenge!.Value, beleg.Daten.Brennstoffeinheit, beleg.Daten.Co2Kg!.Value));
        Assert.Equal(Domain.Gemeinsam.Zeitraum.Kalenderjahr(2026), beleg.Positionen.Single().Abrechnungszeitraum);
        Assert.Equal(3150m, beleg.Positionen.Single().Betrag);
    }

    [Fact]
    public async Task Ungueltige_Angaben_werden_abgelehnt()
    {
        Assert.Contains("Lieferanten", (await MitAsync(b => b.AnlegenAsync(new BelegDaten { Rechnungsdatum = new DateOnly(2026, 1, 1), Brutto = 10m }, [], null, Ct))).Fehler.Single(), StringComparison.Ordinal);
        Assert.Contains("ergibt nicht den Bruttobetrag", (await MitAsync(b => b.AnlegenAsync(new BelegDaten { LieferantName = "X", Rechnungsdatum = new DateOnly(2026, 1, 1), Brutto = 119m, Netto = 100m, Umsatzsteuer = 18m }, [], null, Ct))).Fehler.Single(), StringComparison.Ordinal);
        Assert.False((await MitAsync(b => b.AnlegenAsync(new BelegDaten { LieferantName = "X", Rechnungsdatum = new DateOnly(2026, 1, 1), Brutto = 10m }, [Guid.CreateVersion7()], null, Ct))).Erfolgreich);

        var id = await BelegAsync("X", new DateOnly(2026, 1, 1), 100m, null, null);
        Assert.False((await MitAsync(b => b.PositionHinzufuegenAsync(id, new PositionEingabe { Kostenart = "gibt-es-nicht", ObjektId = haus }, Ct))).Erfolgreich);
        Assert.Contains("gehört nicht zum gewählten Objekt", (await MitAsync(b => b.PositionHinzufuegenAsync(id, new PositionEingabe { Kostenart = "betrkv-02", ObjektId = anderesHaus, EinheitId = wohnung }, Ct))).Fehler.Single(), StringComparison.Ordinal);
        Assert.Contains("Arbeitskostenanteil", (await MitAsync(b => b.PositionHinzufuegenAsync(id, new PositionEingabe { Kostenart = "instandhaltung", ObjektId = haus, Paragraf35a = Paragraf35a.Handwerkerleistung, Arbeitskosten = 150m }, Ct))).Fehler.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dateien_Positionen_entfernen_Loeschen_und_Dashboard()
    {
        umgebung.Uhr.SetUtcNow(new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.Zero));
        var erste = await DateiAsync();
        var id = Pruefen(await MitAsync(b => b.AnlegenAsync(new BelegDaten { LieferantName = "Stadtwerke", Rechnungsdatum = new DateOnly(2026, 5, 1), Brutto = 120m, FaelligAm = new DateOnly(2026, 5, 15) }, [erste], null, Ct)));
        var zweite = await DateiAsync("seite2.pdf");
        Pruefen(await MitAsync(b => b.DateienHinzufuegenAsync(id, [zweite], Ct)));
        Assert.Equal([1, 2], (await MitAsync(b => b.LadenAsync(id, Ct)))!.Dateien.Select(d => d.Seite));
        Pruefen(await MitAsync(b => b.DateiEntfernenAsync(id, zweite, Ct)));
        Assert.Single((await MitAsync(b => b.LadenAsync(id, Ct)))!.Dateien);

        var position = Pruefen(await MitAsync(b => b.PositionHinzufuegenAsync(id, new PositionEingabe { Kostenart = "betrkv-11", ObjektId = haus }, Ct)));
        Pruefen(await MitAsync(b => b.PositionEntfernenAsync(id, position, Ct)));
        Assert.Empty((await MitAsync(b => b.LadenAsync(id, Ct)))!.Positionen);

        var aufgaben = await AufgabenAsync();
        Assert.Contains(aufgaben, a => a.Titel == "Rechnung zahlen: Stadtwerke 120.00 €" && a.Dringlichkeit == Dringlichkeit.Bald);
        Assert.Contains(aufgaben, a => a.Titel == "1 Belege ohne vollständige Kostenaufteilung");

        Pruefen(await MitAsync(b => b.LoeschenAsync(id, Ct)));
        Assert.Null(await MitAsync(b => b.LadenAsync(id, Ct)));
        Assert.Empty(await AufgabenAsync());
    }

    private async Task<List<Guid>> IdsAsync(BelegSuche suche) => (await MitAsync(b => b.SucheAsync(suche, Ct))).Select(z => z.Id).ToList();

    private async Task<Guid> BelegAsync(string lieferant, DateOnly datum, decimal betrag, string? kostenart, Guid? objekt, DateOnly? bezahlt = null) =>
        Pruefen(await MitAsync(b => b.AnlegenAsync(new BelegDaten { LieferantName = lieferant, Rechnungsdatum = datum, Brutto = betrag, BezahltAm = bezahlt }, [],
            kostenart is null ? null : new PositionEingabe { Kostenart = kostenart, ObjektId = objekt }, Ct)));

    private async Task<Guid> DateiAsync(string name = "rechnung.pdf")
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IDateiablage>().SpeichernAsync(new MemoryStream(TestDateien.Pdf(name)), name, Ct)).Id;
    }

    private async Task<IReadOnlyList<Aufgabe>> AufgabenAsync()
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<IAufgabenQuelle>().Single(q => q.GetType().Name == "BelegAufgaben").AufgabenAsync(Admin, Ct);
    }

    private static Guid Pruefen(Ergebnis ergebnis)
    {
        Assert.True(ergebnis.Erfolgreich, string.Join(", ", ergebnis.Fehler));
        return ergebnis.Id!.Value;
    }

    private async Task<T> MitAsync<T>(Func<IBelegverwaltung, Task<T>> aktion)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider.GetRequiredService<IBelegverwaltung>());
    }
}
