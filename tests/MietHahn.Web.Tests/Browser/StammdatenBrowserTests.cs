using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;

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
