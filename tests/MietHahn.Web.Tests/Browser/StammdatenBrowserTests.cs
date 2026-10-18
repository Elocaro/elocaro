using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Application.Vertraege;
using MietHahn.Application.Zaehlerwesen;

namespace MietHahn.Web.Tests.Browser;

public class StammdatenBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task Objektseiten_passen_auf_360_Pixel()
    {
        var seite = await AlsAdminAngemeldetAsync();
        var (objektId, wgId, zimmerId) = await BeispielhausAsync();

        foreach (var pfad in new[] { "/verwaltung/objekte", "/verwaltung/objekte/neu", $"/verwaltung/objekte/{objektId}", $"/verwaltung/objekte/{objektId}/bearbeiten", $"/verwaltung/einheiten/{wgId}", $"/verwaltung/zimmer/{zimmerId}" })
        {
            await seite.GotoAsync(BasisUrl + pfad);
            await KeinHorizontalesScrollenAsync(seite);
        }
    }

    [Fact]
    public async Task Flaeche_im_Zahlenfeld_eingeben_und_speichern()
    {
        var seite = await AlsAdminAngemeldetAsync();
        var (_, wgId, _) = await BeispielhausAsync();

        await MitScreenshotBeiFehlerAsync(seite, async () =>
        {
            await seite.GotoAsync(BasisUrl + $"/verwaltung/einheiten/{wgId}");
            await seite.ClickAsync("summary:text('Fläche ändern')");
            await seite.FillAsync("#flaeche-ab", "2026-07-01");
            await seite.FillAsync("#flaeche-wert", "");
            // Headless-Chromium formatiert Zahlenfelder unabhängig von der Sprache mit Punkt; ein deutscher Browser zeigt ein Komma.
            // Gesendet wird in beiden Fällen „112.5“.
            await seite.Locator("#flaeche-wert").PressSequentiallyAsync("112.5");
            await seite.ClickAsync("button:text('Fläche speichern')");
            await seite.WaitForSelectorAsync("text=Die Fläche wurde gespeichert.");
        });

        var einheit = await App.MitScopeAsync(sp => sp.GetRequiredService<IObjektVerwaltung>().EinheitLadenAsync(wgId, TestContext.Current.CancellationToken));
        Assert.Equal(112.5m, einheit!.Flaechenhistorie[0].Flaeche);
    }

    [Fact]
    public async Task Personenseiten_passen_auf_360_Pixel()
    {
        var seite = await AlsAdminAngemeldetAsync();
        var personId = await App.MitScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var v = sp.GetRequiredService<IPersonenVerwaltung>();
            var id = (await v.AnlegenAsync(
                new PersonDaten { Vorname = "Maximiliane", Nachname = "Mustermann-Schnarrenberger", Email = "maximiliane.mustermann-schnarrenberger@example.de", Telefon = "0221 12345678" },
                new AnschriftEingabe { Ab = new DateOnly(2026, 1, 1), Strasse = "Sehr lange Straßenbezeichnung 123a", Plz = "50667", Ort = "Köln", Zusatz = "c/o Familie Mustermann" },
                ct)).Id!.Value;
            await v.BankverbindungHinzufuegenAsync(id, new BankverbindungEingabe { Iban = "DE89370400440532013000", Kontoinhaber = "Maximiliane Mustermann-Schnarrenberger" }, ct);
            return id;
        });

        foreach (var pfad in new[] { "/verwaltung/personen", "/verwaltung/personen/neu", $"/verwaltung/personen/{personId}", $"/verwaltung/personen/{personId}/bearbeiten", "/verwaltung/datenschutz" })
        {
            await seite.GotoAsync(BasisUrl + pfad);
            await KeinHorizontalesScrollenAsync(seite);
        }
    }

    [Fact]
    public async Task Vertragsseiten_passen_auf_360_Pixel()
    {
        var seite = await AlsAdminAngemeldetAsync();
        var (_, _, zimmer) = await BeispielhausAsync();
        var vertragId = await App.MitScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var mieter = (await sp.GetRequiredService<IPersonenVerwaltung>().AnlegenAsync(new PersonDaten { Vorname = "Maximiliane", Nachname = "Mustermann-Schnarrenberger" }, null, ct)).Id!.Value;
            var vertraege = sp.GetRequiredService<IVertragsVerwaltung>();
            var id = (await vertraege.AnlegenAsync(new VertragAnlage
            {
                MietobjektId = zimmer,
                Beginn = new DateOnly(2026, 1, 1),
                MieterIds = [mieter],
                Abrechnungsart = MietHahn.Domain.Vertraege.Abrechnungsart.Pauschale,
                Kaltmiete = 380,
                Betriebskostenpauschale = 120,
            }, ct)).Id!.Value;
            await vertraege.BestandteilSetzenAsync(id, new BestandteilEingabe { Art = MietHahn.Domain.Vertraege.Bestandteilart.Kaltmiete, Ab = new DateOnly(2026, 4, 1), Betrag = 400 }, ct);
            return id;
        });

        foreach (var pfad in new[] { "/verwaltung/vertraege", "/verwaltung/vertraege/neu", $"/verwaltung/vertraege/{vertragId}", $"/verwaltung/vertraege/{vertragId}/auszug", $"/verwaltung/zimmer/{zimmer}" })
        {
            await seite.GotoAsync(BasisUrl + pfad);
            await KeinHorizontalesScrollenAsync(seite);
        }
    }

    [Fact]
    public async Task Ablese_Runde_auf_dem_Handy_fuer_das_ganze_Haus()
    {
        var seite = await AlsAdminAngemeldetAsync();
        var (objekt, _, _) = await BeispielhausAsync();
        var zaehlerIds = await App.MitScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var zaehler = sp.GetRequiredService<IZaehlerVerwaltung>();
            var ids = new List<Guid>();
            // Hauptwasser, Allgemeinstrom, Gas, je Wohnung Wasser + Wärme (WG: 1×) = 3 + 4 + 1 = 8 Zähler
            foreach (var (bezeichnung, typ) in new[] { ("Hauptwasserzähler", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Kaltwasser), ("Allgemeinstrom", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Strom), ("Gas Heizung", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Gas), ("Wasser Wohnung 1", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Kaltwasser), ("Wärme Wohnung 1", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Waerme), ("Wasser Wohnung 2", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Kaltwasser), ("Wärme Wohnung 2", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Waerme), ("Wasser WG", MietHahn.Domain.Zaehlerwesen.Zaehlertyp.Kaltwasser) })
            {
                ids.Add((await zaehler.AnlegenAsync(objekt, new ZaehlerAnlage
                {
                    Daten = new ZaehlerDaten { Typ = typ, Bezeichnung = bezeichnung, Nummer = $"Nr-{ids.Count + 1}", Lage = "Keller, Hausanschlussraum links" },
                    EingebautAm = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
                    Anfangsstand = 100,
                }, ct)).Id!.Value);
            }

            return ids;
        });

        var dauer = System.Diagnostics.Stopwatch.StartNew();
        await MitScreenshotBeiFehlerAsync(seite, async () =>
        {
            await seite.GotoAsync(BasisUrl + $"/verwaltung/objekte/{objekt}/ablesung");
            for (var i = 0; i < zaehlerIds.Count; i++)
            {
                await seite.WaitForSelectorAsync($"text=Zähler {i + 1} von {zaehlerIds.Count}");
                await KeinHorizontalesScrollenAsync(seite);
                await seite.FillAsync("#ablesung-wert", (110 + i).ToString(System.Globalization.CultureInfo.InvariantCulture));
                await seite.ClickAsync("button:has-text('Speichern & weiter')");
            }

            await seite.WaitForSelectorAsync("text=Runde abgeschlossen: 8 von 8 Zählern erfasst.");
        });
        dauer.Stop();

        Assert.True(dauer.Elapsed < TimeSpan.FromMinutes(1), $"Runde dauerte {dauer.Elapsed}");
        foreach (var pfad in new[] { $"/verwaltung/objekte/{objekt}/zaehler", $"/verwaltung/zaehler/{zaehlerIds[0]}" })
        {
            await seite.GotoAsync(BasisUrl + pfad);
            await KeinHorizontalesScrollenAsync(seite);
        }
    }

    private async Task<(Guid Objekt, Guid Wg, Guid Zimmer)> BeispielhausAsync() =>
        await App.MitScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var v = sp.GetRequiredService<IObjektVerwaltung>();
            var start = new DateOnly(2026, 1, 1);
            var objekt = (await v.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3 mit langem Namen", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, ct)).Id!.Value;
            await v.FlaecheSetzenAsync(objekt, new FlaechenEingabe { Ab = start, Flaeche = 400, Zusatz = 30 }, ct);
            Guid wg = default, zimmer = default;
            for (var i = 1; i <= 4; i++)
            {
                var einheit = (await v.EinheitAnlegenAsync(objekt, new EinheitDaten { Bezeichnung = $"Wohnung {i}", Lage = "1. OG rechts hinten" }, ct)).Id!.Value;
                await v.EinheitFlaecheSetzenAsync(einheit, new FlaechenEingabe { Ab = start, Flaeche = 90 }, ct);
                wg = einheit;
            }

            for (var z = 1; z <= 4; z++)
            {
                zimmer = (await v.ZimmerAnlegenAsync(wg, new ZimmerDaten { Bezeichnung = $"Zimmer {z}" }, ct)).Id!.Value;
                await v.ZimmerFlaecheSetzenAsync(zimmer, new FlaechenEingabe { Ab = start, Flaeche = 14, Zusatz = 8 }, ct);
            }

            return (objekt, wg, zimmer);
        });
}
