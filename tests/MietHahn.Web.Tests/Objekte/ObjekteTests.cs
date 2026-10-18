using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Web.Tests.Konto;
using SkiaSharp;

namespace MietHahn.Web.Tests.Objekte;

public class ObjekteTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private static readonly Dictionary<string, string> Haus = new()
    {
        ["Daten.Bezeichnung"] = "Lindenweg 3",
        ["Daten.Strasse"] = "Lindenweg 3",
        ["Daten.Plz"] = "50667",
        ["Daten.Ort"] = "Köln",
        ["Daten.Baujahr"] = "1965",
        ["Daten.Energietraeger"] = "Erdgas",
    };

    [Fact]
    public async Task Haus_mit_Wohnungen_und_WG_ueber_die_Oberflaeche_anlegen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        Assert.Contains("Noch keine Objekte", (await admin.LadenAsync("/verwaltung/objekte")).Text, StringComparison.Ordinal);

        var objekt = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt", Haus);
        Assert.Matches("^/verwaltung/objekte/[0-9a-f-]{36}$", objekt.Pfad);
        Assert.Contains("Das Objekt wurde angelegt.", objekt.Text, StringComparison.Ordinal);
        Assert.Contains("Lindenweg 3, 50667 Köln", objekt.Text, StringComparison.Ordinal);
        Assert.Contains("Zentralheizung, Erdgas", objekt.Text, StringComparison.Ordinal);

        objekt = await admin.AbsendenAsync(objekt, "flaeche", new Dictionary<string, string> { ["Eingabe.Ab"] = "2026-01-01", ["Eingabe.Flaeche"] = "400", ["Eingabe.Zusatz"] = "30.5" });
        Assert.Contains("Die Fläche wurde gespeichert.", objekt.Text, StringComparison.Ordinal);
        Assert.Contains("30,5 m²", objekt.Text, StringComparison.Ordinal);

        // Zahlenfeld: Browser zeigt deutsch an, Wert im HTML immer mit Punkt (vorbelegt mit dem aktuellen Stand)
        var zusatz = objekt.Dokument.QuerySelector("#flaeche-zusatz")!;
        Assert.Equal("number", zusatz.GetAttribute("type"));
        Assert.Equal("0.01", zusatz.GetAttribute("step"));
        Assert.Equal("30.5", zusatz.GetAttribute("value"));

        foreach (var nummer in new[] { 1, 2, 3, 4 })
        {
            objekt = await admin.AbsendenAsync(objekt, "einheit", new Dictionary<string, string> { ["Daten.Bezeichnung"] = $"Wohnung {nummer}", ["Daten.Lage"] = nummer <= 2 ? "EG" : "OG" });
            Assert.Contains("Die Einheit wurde hinzugefügt.", objekt.Text, StringComparison.Ordinal);
        }

        var objektPfad = objekt.Pfad;
        var wgLink = objekt.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Wohnung 4").GetAttribute("href")!;
        var wg = await admin.LadenAsync(wgLink);
        wg = await admin.AbsendenAsync(wg, "flaeche", new Dictionary<string, string> { ["Eingabe.Ab"] = "2026-01-01", ["Eingabe.Flaeche"] = "110" });
        Assert.Contains("110 m²", wg.Text, StringComparison.Ordinal);
        foreach (var nummer in new[] { 1, 2, 3, 4 })
        {
            wg = await admin.AbsendenAsync(wg, "zimmer", new Dictionary<string, string> { ["Daten.Bezeichnung"] = $"Zimmer {nummer}", ["Daten.Moeblierung"] = "Moebliert" });
            Assert.Contains("Das Zimmer wurde hinzugefügt.", wg.Text, StringComparison.Ordinal);
        }

        var uebersicht = await admin.LadenAsync(objektPfad);
        Assert.Equal(7, uebersicht.Dokument.QuerySelectorAll("tbody .status-wartend").Count(e => e.TextContent == "Leerstand"));
        Assert.Contains("WG", uebersicht.Text, StringComparison.Ordinal);
        // Ohne Wohnfläche für Wohnung 1–3 und Zimmer: Plausibilitätshinweise
        Assert.Contains("Wohnung 1: Keine Wohnfläche", uebersicht.Text, StringComparison.Ordinal);

        var liste = await admin.LadenAsync("/verwaltung/objekte");
        Assert.Contains("0 von 7 vermietet", liste.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Flaechenaenderung_ab_Stichtag_zeigt_den_Verlauf()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var objekt = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt", Haus);
        objekt = await admin.AbsendenAsync(objekt, "einheit", new Dictionary<string, string> { ["Daten.Bezeichnung"] = "Wohnung 1" });
        var einheit = await admin.LadenAsync(objekt.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Wohnung 1").GetAttribute("href")!);

        einheit = await admin.AbsendenAsync(einheit, "flaeche", new Dictionary<string, string> { ["Eingabe.Ab"] = "2025-01-01", ["Eingabe.Flaeche"] = "75.5" });
        einheit = await admin.AbsendenAsync(einheit, "flaeche", new Dictionary<string, string> { ["Eingabe.Ab"] = "2026-02-01", ["Eingabe.Flaeche"] = "82.4", ["Eingabe.Zusatz"] = "80" });

        Assert.Contains("Verlauf", einheit.Text, StringComparison.Ordinal);
        Assert.Contains("01.01.2025 – 31.01.2026", einheit.Text, StringComparison.Ordinal);
        Assert.Contains("seit 01.02.2026", einheit.Text, StringComparison.Ordinal);

        var rueckwirkend = await admin.AbsendenAsync(einheit, "flaeche", new Dictionary<string, string> { ["Eingabe.Ab"] = "2025-06-01", ["Eingabe.Flaeche"] = "70" });
        Assert.Contains("Der Stichtag muss am oder nach dem 01.02.2026 liegen", rueckwirkend.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ungueltige_Eingaben_werden_gemeldet()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var fehler = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt",
            new Dictionary<string, string>(Haus) { ["Daten.Plz"] = "123", ["Daten.Baujahr"] = "1200" });
        Assert.Contains("Die Postleitzahl hat 5 Ziffern.", fehler.Text, StringComparison.Ordinal);
        Assert.Contains("Baujahr zwischen 1500 und 2100", fehler.Text, StringComparison.Ordinal);

        var objekt = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt", Haus);
        // type="number" sendet immer mit Punkt; fachlich unplausible Werte lehnt die Domäne ab.
        var flaeche = await admin.AbsendenAsync(objekt, "flaeche", new Dictionary<string, string> { ["Eingabe.Ab"] = "2026-01-01", ["Eingabe.Flaeche"] = "-5" });
        Assert.Contains("Die Gesamtfläche muss größer als 0 sein.", flaeche.Text, StringComparison.Ordinal);
        var ohneWert = await admin.AbsendenAsync(objekt, "flaeche", new Dictionary<string, string> { ["Eingabe.Ab"] = "2026-01-01", ["Eingabe.Flaeche"] = "" });
        Assert.Contains("Bitte die Fläche angeben.", ohneWert.Text, StringComparison.Ordinal);

        var doppelt = await admin.AbsendenAsync(objekt, "einheit", new Dictionary<string, string> { ["Daten.Bezeichnung"] = "W1" });
        doppelt = await admin.AbsendenAsync(doppelt, "einheit", new Dictionary<string, string> { ["Daten.Bezeichnung"] = "w1" });
        Assert.Contains("Im Objekt gibt es bereits eine Einheit „w1“.", doppelt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foto_hochladen_und_anzeigen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var objekt = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt", Haus);
        var bearbeiten = await admin.LadenAsync(objekt.Pfad + "/bearbeiten");

        var nachUpload = await admin.AbsendenMitDateiAsync(bearbeiten, "bild", "Bild", Jpeg(), "haus.jpg");

        Assert.Contains("Das Foto wurde gespeichert.", nachUpload.Text, StringComparison.Ordinal);
        var bild = nachUpload.Dokument.QuerySelector("img.objektbild")!.GetAttribute("src")!;
        using var antwort = await admin.Http.GetAsync(new Uri(bild, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("image/jpeg", antwort.Content.Headers.ContentType!.MediaType);

        var keinBild = await admin.AbsendenMitDateiAsync(bearbeiten, "bild", "Bild", "%PDF-1.4"u8.ToArray(), "plan.pdf", "application/pdf");
        Assert.Contains("Das Foto muss ein Bild sein.", keinBild.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Recht_Stammdaten_kein_Zugriff()
    {
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Finanzen]);
        using var _ = verwalter;

        var antwort = await verwalter.LadenAsync("/verwaltung/objekte", weiterleitungenFolgen: false);

        Assert.Equal("/konto/zugriff-verweigert", antwort.Weiterleitung!.AbsolutePath);
        Assert.DoesNotContain("/verwaltung/objekte", (await verwalter.LadenAsync("/verwaltung")).Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hausmeister_sieht_nur_zugewiesene_Objekte()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var hausA = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt", Haus);
        hausA = await admin.AbsendenAsync(hausA, "einheit", new Dictionary<string, string> { ["Daten.Bezeichnung"] = "Wohnung 1", ["Daten.Lage"] = "EG links" });
        var hausB = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt",
            new Dictionary<string, string>(Haus) { ["Daten.Bezeichnung"] = "Birkenallee 9", ["Daten.Strasse"] = "Birkenallee 9" });
        var idA = Guid.Parse(hausA.Pfad.Split('/')[^1]);
        var idB = Guid.Parse(hausB.Pfad.Split('/')[^1]);

        var (hausmeister, hausmeisterId) = await AngemeldetAlsAsync(Rollen.Hausmeister);
        using var __ = hausmeister;

        // Zuordnung über die Benutzerverwaltung
        var konto = await admin.LadenAsync($"/verwaltung/benutzer/{hausmeisterId}");
        Assert.Contains("Betreute Objekte", konto.Text, StringComparison.Ordinal);
        konto = await admin.AbsendenAsync(konto, "objekte", new Dictionary<string, string> { ["ObjekteEingabe.Auswahl"] = idA.ToString() });
        Assert.Contains("Die betreuten Objekte wurden gespeichert.", konto.Text, StringComparison.Ordinal);
        Assert.True(konto.Dokument.QuerySelector($"#objekt-{idA}")!.HasAttribute("checked"));
        Assert.False(konto.Dokument.QuerySelector($"#objekt-{idB}")!.HasAttribute("checked"));

        var liste = await hausmeister.LadenAsync("/service/objekte");
        Assert.Contains("Lindenweg 3", liste.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birkenallee", liste.Text, StringComparison.Ordinal);

        var erlaubt = await hausmeister.LadenAsync($"/service/objekte/{idA}");
        Assert.Contains("EG links", erlaubt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Leerstand", erlaubt.Text, StringComparison.Ordinal); // keine Belegung/Mieterdaten

        var verboten = await hausmeister.LadenAsync($"/service/objekte/{idB}");
        Assert.Contains("Objekt nicht gefunden", verboten.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birkenallee", verboten.Text, StringComparison.Ordinal);

        Assert.Equal("/konto/zugriff-verweigert", (await hausmeister.LadenAsync($"/verwaltung/objekte/{idA}", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);

        // Mieter haben keine Objektzuordnung
        var (_, mieterId) = await AngemeldetAlsAsync(Rollen.Mieter);
        var ergebnis = await App.MitScopeAsync(sp => sp.GetRequiredService<IObjektZuordnungen>().SetzenAsync(mieterId, [idA], TestContext.Current.CancellationToken));
        Assert.False(ergebnis.Erfolgreich);
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var bild = SKImage.FromBitmap(bitmap);
        return bild.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }
}
