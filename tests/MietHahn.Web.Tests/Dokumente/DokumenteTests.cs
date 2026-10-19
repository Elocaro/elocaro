using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Web.Tests.Konto;
using SkiaSharp;

namespace MietHahn.Web.Tests.Dokumente;

public class DokumenteTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    [Fact]
    public async Task Dokument_zum_Objekt_hochladen_suchen_bearbeiten_und_loeschen()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var objektId = await ObjektAsync(admin);

        var objekt = await admin.LadenAsync($"/verwaltung/objekte/{objektId}");
        Assert.Contains("Noch keine Dokumente.", objekt.Text, StringComparison.Ordinal);
        var neu = await admin.LadenAsync(objekt.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Dokument hochladen").GetAttribute("href")!);
        Assert.True(neu.Dokument.QuerySelector($"#dokument-objekt option[value='{objektId}']")!.HasAttribute("selected"));

        // Ohne Titel gilt der Dateiname.
        var details = await admin.AbsendenMitDateiAsync(neu, "dokument", "Datei", Pdf, "Energieausweis_2024.pdf", "application/pdf", new Dictionary<string, string>
        {
            ["Daten.Kategorie"] = "Energieausweis",
            ["Daten.AblaufAm"] = "2034-05-31",
            ["Daten.Tags"] = "Energie, Pflicht",
        });
        Assert.Contains("Das Dokument wurde abgelegt.", details.Text, StringComparison.Ordinal);
        Assert.Equal("Energieausweis_2024", details.Dokument.QuerySelector("h1")!.TextContent.Trim());
        Assert.NotNull(details.Dokument.QuerySelector("iframe.dokument-vorschau"));
        var dokumentPfad = details.Pfad;

        var liste = await admin.LadenAsync("/verwaltung/dokumente?q=pflicht");
        Assert.Contains("Energieausweis_2024", liste.Text, StringComparison.Ordinal);
        Assert.Contains("gültig bis 31.05.2034", liste.Text, StringComparison.Ordinal);
        Assert.Contains("Keine passenden Dokumente.", (await admin.LadenAsync("/verwaltung/dokumente?kategorie=Rechnung")).Text, StringComparison.Ordinal);
        Assert.Contains("Energieausweis_2024", (await admin.LadenAsync($"/verwaltung/objekte/{objektId}")).Text, StringComparison.Ordinal);

        details = await admin.AbsendenAsync(details, "dokument", new Dictionary<string, string> { ["Daten.Titel"] = "Energieausweis (Bedarf)", ["Daten.FuerHausmeisterFreigegeben"] = "true" });
        Assert.Contains("Die Angaben wurden gespeichert.", details.Text, StringComparison.Ordinal);
        Assert.Contains("Hausmeister", details.Dokument.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);

        var geloescht = await admin.AbsendenAsync(await admin.LadenAsync(dokumentPfad), "loeschen");
        Assert.Contains("Das Dokument wurde gelöscht.", geloescht.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Dokumente.", geloescht.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Bezug_oder_Datei_wird_nichts_abgelegt()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        await ObjektAsync(admin);

        var ohneBezug = await admin.AbsendenMitDateiAsync(await admin.LadenAsync("/verwaltung/dokumente/neu"), "dokument", "Datei", Pdf, "lose.pdf", "application/pdf");
        Assert.Contains("mindestens einen Bezug", ohneBezug.Text, StringComparison.Ordinal);

        var ausfuehrbar = await admin.AbsendenMitDateiAsync(await admin.LadenAsync("/verwaltung/dokumente/neu"), "dokument", "Datei", "MZ"u8.ToArray(), "tool.exe", "application/octet-stream");
        Assert.NotNull(ausfuehrbar.Dokument.QuerySelector(".meldung-fehler"));
        Assert.Contains("Noch keine Dokumente.", (await admin.LadenAsync("/verwaltung/dokumente")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hausmeister_sieht_freigegebene_Dokumente_im_Servicebereich()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var objektId = await ObjektAsync(admin);
        var neu = await admin.LadenAsync($"/verwaltung/dokumente/neu?objekt={objektId}");
        var freigegeben = await admin.AbsendenMitDateiAsync(neu, "dokument", "Datei", Pdf, "wartung.pdf", "application/pdf",
            new Dictionary<string, string> { ["Daten.Titel"] = "Wartungsplan Heizung", ["Daten.FuerHausmeisterFreigegeben"] = "true" });
        await admin.AbsendenMitDateiAsync(neu, "dokument", "Datei", Pdf, "kalkulation.pdf", "application/pdf",
            new Dictionary<string, string> { ["Daten.Titel"] = "Interne Kalkulation" });
        var dateiLink = freigegeben.Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Herunterladen").GetAttribute("href")!;
        var interneDatei = (await admin.LadenAsync("/verwaltung/dokumente?q=Kalkulation")).Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Interne Kalkulation").GetAttribute("href")!;
        interneDatei = (await admin.LadenAsync(interneDatei)).Dokument.QuerySelectorAll("a").Single(a => a.TextContent == "Herunterladen").GetAttribute("href")!;

        var (hausmeister, hausmeisterId) = await AngemeldetAlsAsync(Rollen.Hausmeister);
        using var __ = hausmeister;
        Assert.True((await App.MitScopeAsync(sp => sp.GetRequiredService<IObjektZuordnungen>().SetzenAsync(hausmeisterId, [objektId], TestContext.Current.CancellationToken))).Erfolgreich);

        var service = await hausmeister.LadenAsync($"/service/objekte/{objektId}");
        Assert.Contains("Wartungsplan Heizung", service.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Interne Kalkulation", service.Text, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await hausmeister.LadenAsync(dateiLink)).Status);
        Assert.Equal("/konto/zugriff-verweigert", (await hausmeister.LadenAsync(interneDatei, weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
    }

    [Fact]
    public async Task PDF_Vorschau_darf_eingebettet_werden_aber_nicht_fremd()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var objektId = await ObjektAsync(admin);
        var details = await admin.AbsendenMitDateiAsync(await admin.LadenAsync($"/verwaltung/dokumente/neu?objekt={objektId}"), "dokument", "Datei", Pdf, "plan.pdf", "application/pdf");
        var dateiPfad = details.Dokument.QuerySelector("iframe.dokument-vorschau")!.GetAttribute("src")!;

        using var antwort = await admin.Http.GetAsync(new Uri(dateiPfad, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("SAMEORIGIN", antwort.Headers.GetValues("X-Frame-Options").Single());
        var csp = antwort.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox", csp, StringComparison.Ordinal);

        // Bilder (und alles andere) bleiben in der Sandbox.
        var foto = await admin.AbsendenMitDateiAsync(await admin.LadenAsync($"/verwaltung/dokumente/neu?objekt={objektId}"), "dokument", "Datei", Jpeg(), "keller.jpg");
        var bildPfad = foto.Dokument.QuerySelector("img.dokument-vorschau")!.GetAttribute("src")!;
        using var bild = await admin.Http.GetAsync(new Uri(bildPfad, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.EndsWith("; sandbox", bild.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var bild = SKImage.FromBitmap(bitmap);
        return bild.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    [Fact]
    public async Task Ohne_Recht_Stammdaten_keine_Dokumentablage()
    {
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Finanzen]);
        using var _ = verwalter;

        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung/dokumente", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await verwalter.LadenAsync("/verwaltung/dokumente/neu", weiterleitungenFolgen: false)).Weiterleitung!.AbsolutePath);
    }

    private static async Task<Guid> ObjektAsync(TestBrowser admin)
    {
        var objekt = await admin.AbsendenAsync(await admin.LadenAsync("/verwaltung/objekte/neu"), "objekt", new Dictionary<string, string>
        {
            ["Daten.Bezeichnung"] = "Lindenweg 3",
            ["Daten.Strasse"] = "Lindenweg 3",
            ["Daten.Plz"] = "50667",
            ["Daten.Ort"] = "Köln",
        });
        return Guid.Parse(objekt.Pfad.Split('/')[^1]);
    }
}
