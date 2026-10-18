using System.IO.Compression;
using System.Net;
using AngleSharp.Html.Dom;
using MietHahn.Application.Konten;
using MietHahn.Web.Tests.Konto;

namespace MietHahn.Web.Tests.Betrieb;

public class DatensicherungTests(PostgresFixture postgres) : KontoTestBasis(postgres)
{
    private const string Seite = "/verwaltung/system/datensicherung";
    private const string Export = "/verwaltung/system/datensicherung/export";

    private readonly string statusDatei = Path.Combine(Path.GetTempPath(), "miethahn-tests", Guid.NewGuid().ToString("N"), "backup.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override IDictionary<string, string?> Konfiguration => new Dictionary<string, string?> { ["Backup:StatusDatei"] = statusDatei };

    [Fact]
    public async Task Admin_sieht_fehlgeschlagene_Sicherung_auf_Seite_und_Dashboard()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statusDatei)!);
        await File.WriteAllTextAsync(statusDatei,
            """{"zeitpunkt":"2026-03-01T01:30:05Z","erfolgreich":false,"meldung":"Fatal: unable to open repository","snapshot":"","dauerSekunden":2}""", Ct);
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var seite = await admin.LadenAsync(Seite);
        Assert.Contains("Die Sicherung am 01.03.2026 02:30 ist fehlgeschlagen.", seite.Text, StringComparison.Ordinal);
        Assert.Contains("Fatal: unable to open repository", seite.Text, StringComparison.Ordinal);
        Assert.Contains("run --rm backup sichern", seite.Text, StringComparison.Ordinal);

        var dashboard = await admin.LadenAsync("/verwaltung");
        Assert.Contains("Datensicherung prüfen", dashboard.TextMitStreaming, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Statusdatei_fordert_die_Seite_zur_ersten_Sicherung_auf()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        var seite = await admin.LadenAsync(Seite);

        Assert.Contains("Es liegt noch keine Sicherung vor.", seite.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_laedt_den_Vollexport_herunter()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;
        var seite = await admin.LadenAsync(Seite);

        using var antwort = await PostenAsync(admin, ExportFormularFelder(seite));

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("application/zip", antwort.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", antwort.Content.Headers.ContentDisposition!.DispositionType);
        Assert.StartsWith("miethahn-export-", antwort.Content.Headers.ContentDisposition.FileNameStar, StringComparison.Ordinal);
        Assert.True(antwort.Headers.CacheControl!.NoStore);
        using var zip = new ZipArchive(await antwort.Content.ReadAsStreamAsync(Ct), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("daten/benutzer.json"));
    }

    [Fact]
    public async Task Export_ohne_Antiforgery_Token_wird_abgelehnt()
    {
        var (admin, _) = await AngemeldetAlsAsync(Rollen.Admin);
        using var _ = admin;

        using var antwort = await PostenAsync(admin, []);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    [Fact]
    public async Task Ohne_Systemrecht_kein_Zugriff_auf_Seite_und_Export()
    {
        var (verwalter, _) = await AngemeldetAlsAsync(Rollen.Verwalter, [Recht.Stammdaten, Recht.Finanzen]);
        using var _ = verwalter;

        var seite = await verwalter.LadenAsync(Seite, weiterleitungenFolgen: false);
        Assert.Equal("/konto/zugriff-verweigert", seite.Weiterleitung!.AbsolutePath);

        // Token von einer erlaubten Seite, damit nur die Autorisierung greift
        var start = await verwalter.LadenAsync("/verwaltung");
        using var antwort = await PostenAsync(verwalter, [new("__RequestVerificationToken", Token(start))]);
        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal("/konto/zugriff-verweigert", antwort.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Ohne_Anmeldung_fuehrt_der_Export_zur_Anmeldung()
    {
        using var browser = App.Browser();

        using var antwort = await PostenAsync(browser, []);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal("/konto/anmelden", antwort.Headers.Location!.AbsolutePath);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var ordner = Path.GetDirectoryName(statusDatei)!;
        if (Directory.Exists(ordner))
        {
            Directory.Delete(ordner, recursive: true);
        }
    }

    private static List<KeyValuePair<string, string>> ExportFormularFelder(Seite seite)
    {
        var formular = seite.Dokument.Forms.Single(f => f.GetAttribute("action") == Export);
        return formular.Elements.OfType<IHtmlInputElement>().Select(i => new KeyValuePair<string, string>(i.Name!, i.Value)).ToList();
    }

    private static string Token(Seite seite) =>
        seite.Dokument.QuerySelectorAll("input[name=__RequestVerificationToken]").OfType<IHtmlInputElement>().First().Value;

    private static async Task<HttpResponseMessage> PostenAsync(TestBrowser browser, List<KeyValuePair<string, string>> felder)
    {
        using var inhalt = new FormUrlEncodedContent(felder);
        return await browser.Http.PostAsync(new Uri(Export, UriKind.Relative), inhalt, Ct);
    }
}
