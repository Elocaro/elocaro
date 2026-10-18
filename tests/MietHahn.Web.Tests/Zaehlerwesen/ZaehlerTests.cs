using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Application.Zaehlerwesen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Zaehlerwesen;
using MietHahn.Web.Tests.Konto;
using SkiaSharp;

namespace MietHahn.Web.Tests.Zaehlerwesen;

public class ZaehlerTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private DateOnly Heute => Zeitzone.Heute(App.Uhr.GetUtcNow());

    private static string Iso(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Zaehler_anlegen_Stand_mit_Foto_und_Bestaetigung_bei_Rueckwaertslauf()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (objekt, wohnung) = await ObjektAsync();

        var liste = await admin.AbsendenAsync(await admin.LadenAsync($"/verwaltung/objekte/{objekt}/zaehler"), "zaehler", new Dictionary<string, string>
        {
            ["Daten.Typ"] = "Kaltwasser",
            ["Daten.Bezeichnung"] = "Kaltwasser Bad",
            ["Daten.Nummer"] = "WZ-4711",
            ["Daten.ZuordnungId"] = wohnung.ToString(),
            ["Anlage.EingebautAm"] = Iso(Heute.AddDays(-60)),
            ["Anlage.Anfangsstand"] = "100.5",
        });
        Assert.Contains("Der Zähler wurde angelegt.", liste.Text, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1", liste.Text, StringComparison.Ordinal);
        Assert.Contains("100,5 m³", liste.Text, StringComparison.Ordinal);

        var seite = await admin.LadenAsync(liste.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Kaltwasser Bad").GetAttribute("href")!);
        seite = await admin.AbsendenMitDateiAsync(seite, "stand", "Foto", Jpeg(), "zaehler.jpg");
        Assert.Contains("Bitte den Zählerstand angeben.", seite.Text, StringComparison.Ordinal);

        var felder = new Dictionary<string, string> { ["Stand.Datum"] = Iso(Heute.AddDays(-30)), ["Stand.Wert"] = "130" };
        seite = await admin.AbsendenAsync(await admin.LadenAsync(seite.Pfad), "stand", felder);
        Assert.Contains("Der Zählerstand wurde gespeichert.", seite.Text, StringComparison.Ordinal);

        var warnung = await admin.AbsendenAsync(seite, "stand", new Dictionary<string, string> { ["Stand.Datum"] = Iso(Heute), ["Stand.Wert"] = "120" });
        Assert.Contains("Rückwärtslauf", warnung.Text, StringComparison.Ordinal);
        Assert.Contains("Trotzdem speichern", warnung.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("120 ", warnung.Dokument.QuerySelector("table.tabelle")!.TextContent, StringComparison.Ordinal);

        var bestaetigt = await admin.AbsendenAsync(warnung, "stand", new Dictionary<string, string> { ["Stand.Datum"] = Iso(Heute), ["Stand.Wert"] = "120" });
        Assert.Contains("Der Zählerstand wurde gespeichert.", bestaetigt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foto_wird_beim_Stand_gespeichert()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (objekt, wohnung) = await ObjektAsync();
        var zaehler = await ZaehlerAsync(objekt, wohnung, "WZ-1", 100m);
        var seite = await admin.LadenAsync($"/verwaltung/zaehler/{zaehler}");

        // Formularfelder setzen und Foto anhängen: Testbrowser sendet die Eingaben mit
        seite.Formular("stand").Elements.OfType<AngleSharp.Html.Dom.IHtmlInputElement>().Single(e => e.Name == "Stand.Wert").Value = "150";
        var gespeichert = await admin.AbsendenMitDateiAsync(seite, "stand", "Foto", Jpeg(), "zaehler.jpg");

        Assert.Contains("Der Zählerstand wurde gespeichert.", gespeichert.Text, StringComparison.Ordinal);
        var foto = gespeichert.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Foto").GetAttribute("href")!;
        using var antwort = await admin.Http.GetAsync(new Uri(foto, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("image/jpeg", antwort.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Ablese_Runde_Schritt_fuer_Schritt_mit_Ueberspringen_und_Zusammenfassung()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (objekt, wohnung) = await ObjektAsync();
        await ZaehlerAsync(objekt, null, "HWZ", 1000m);
        await ZaehlerAsync(objekt, wohnung, "WZ-1", 10m);
        await ZaehlerAsync(objekt, wohnung, "WZ-2", 20m);

        var schritt = await admin.LadenAsync($"/verwaltung/objekte/{objekt}/ablesung");
        Assert.Contains("Zähler 1 von 3", schritt.Text, StringComparison.Ordinal);
        Assert.Contains("Nr. HWZ", schritt.Text, StringComparison.Ordinal);

        schritt = await admin.AbsendenAsync(schritt, "ablesung", new Dictionary<string, string> { ["Stand.Wert"] = "1010" });
        Assert.Contains("Zähler 2 von 3", schritt.Text, StringComparison.Ordinal);
        Assert.Contains("gespeichert.", schritt.Text, StringComparison.Ordinal);

        schritt = await admin.LadenAsync(schritt.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Überspringen").GetAttribute("href")!);
        Assert.Contains("Zähler 3 von 3", schritt.Text, StringComparison.Ordinal);

        var ende = await admin.AbsendenAsync(schritt, "ablesung", new Dictionary<string, string> { ["Stand.Wert"] = "21.5" });
        Assert.Contains("Runde abgeschlossen: 2 von 3 Zählern erfasst.", ende.Text, StringComparison.Ordinal);
        Assert.Contains("nicht erfasst", ende.Text, StringComparison.Ordinal);
        Assert.Contains("21,5 m³", ende.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zwischenablesung_nur_fuer_die_Zaehler_einer_Einheit()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (objekt, wohnung) = await ObjektAsync();
        await ZaehlerAsync(objekt, null, "HWZ", 1000m);
        var wz = await ZaehlerAsync(objekt, wohnung, "WZ-1", 10m);

        var schritt = await admin.LadenAsync($"/verwaltung/objekte/{objekt}/ablesung?einheit={wohnung}&datum={Iso(Heute)}&art=Zwischenablesung");
        Assert.Contains("Zähler 1 von 1", schritt.Text, StringComparison.Ordinal);
        Assert.Contains("Zwischenablesung", schritt.Text, StringComparison.Ordinal);
        await admin.AbsendenAsync(schritt, "ablesung", new Dictionary<string, string> { ["Stand.Wert"] = "12" });

        var stand = (await App.MitScopeAsync(sp => sp.GetRequiredService<IZaehlerVerwaltung>().LadenAsync(wz, TestContext.Current.CancellationToken)))!.Staende[0];
        Assert.Equal(Standart.Zwischenablesung, stand.Art);
    }

    [Fact]
    public async Task Zaehlertausch_ueber_die_Oberflaeche()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var (objekt, wohnung) = await ObjektAsync();
        var zaehler = await ZaehlerAsync(objekt, wohnung, "WZ-ALT", 100m);

        var neu = await admin.AbsendenAsync(await admin.LadenAsync($"/verwaltung/zaehler/{zaehler}"), "tausch", new Dictionary<string, string>
        {
            ["Tausch.Datum"] = Iso(Heute),
            ["Tausch.Endstand"] = "150",
            ["Tausch.NeueNummer"] = "WZ-NEU",
            ["Tausch.Anfangsstand"] = "0",
        });

        Assert.Contains("Der Zähler wurde getauscht.", neu.Text, StringComparison.Ordinal);
        Assert.Contains("Nr. WZ-NEU", neu.Text, StringComparison.Ordinal);
        Assert.Contains("WZ-ALT", neu.Text, StringComparison.Ordinal); // Messstelle
        Assert.DoesNotContain(zaehler.ToString(), neu.Pfad, StringComparison.Ordinal);
    }

    private async Task<(Guid Objekt, Guid Wohnung)> ObjektAsync() =>
        await App.MitScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var objekte = sp.GetRequiredService<IObjektVerwaltung>();
            var objekt = (await objekte.AnlegenAsync(new ObjektDaten { Bezeichnung = "Lindenweg 3", Strasse = "Lindenweg 3", Plz = "50667", Ort = "Köln" }, ct)).Id!.Value;
            var wohnung = (await objekte.EinheitAnlegenAsync(objekt, new EinheitDaten { Bezeichnung = "Wohnung 1" }, ct)).Id!.Value;
            return (objekt, wohnung);
        });

    private async Task<Guid> ZaehlerAsync(Guid objekt, Guid? zuordnung, string nummer, decimal anfang) =>
        await App.MitScopeAsync(async sp => (await sp.GetRequiredService<IZaehlerVerwaltung>().AnlegenAsync(objekt, new ZaehlerAnlage
        {
            Daten = new ZaehlerDaten { Typ = Zaehlertyp.Kaltwasser, Bezeichnung = $"Zähler {nummer}", Nummer = nummer, ZuordnungId = zuordnung },
            EingebautAm = Heute.AddDays(-30),
            Anfangsstand = anfang,
        }, TestContext.Current.CancellationToken)).Id!.Value);

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.SteelBlue);
        using var bild = SKImage.FromBitmap(bitmap);
        return bild.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }
}
