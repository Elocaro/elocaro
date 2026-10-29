using System.IO.Compression;
using System.Net;
using AngleSharp.Html.Dom;
using Elocaro.Application.Accounts;
using Elocaro.Web.Tests.Account;

namespace Elocaro.Web.Tests.Operations;

public class BackupTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Page = "/verwaltung/system/datensicherung";
    private const string Export = "/verwaltung/system/datensicherung/export";

    private readonly string statusFile = Path.Combine(Path.GetTempPath(), "elocaro-tests", Guid.NewGuid().ToString("N"), "backup.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?> { ["Backup:StatusFile"] = statusFile };

    [Fact]
    public async Task Admin_sees_failed_backup_on_page_and_dashboard()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!);
        await File.WriteAllTextAsync(statusFile,
            """{"timestamp":"2026-03-01T01:30:05Z","succeeded":false,"message":"Fatal: unable to open repository","snapshot":"","durationSeconds":2}""", Ct);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);
        Assert.Contains("Die Sicherung am 01.03.2026 02:30 ist fehlgeschlagen.", page.Text, StringComparison.Ordinal);
        Assert.Contains("Fatal: unable to open repository", page.Text, StringComparison.Ordinal);
        Assert.Contains("run --rm backup backup", page.Text, StringComparison.Ordinal);

        var dashboard = await admin.GetAsync("/verwaltung");
        Assert.Contains("Datensicherung prüfen", dashboard.TextWithStreaming, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_status_file_the_page_prompts_for_the_first_backup()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("Es liegt noch keine Sicherung vor.", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_downloads_the_full_export()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync(Page);

        using var response = await PostAsync(admin, ExportFormFields(page));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.StartsWith("elocaro-export-", response.Content.Headers.ContentDisposition.FileNameStar, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(Ct), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("data/users.json"));
    }

    [Fact]
    public async Task Export_without_antiforgery_token_is_rejected()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        using var response = await PostAsync(admin, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_system_permission_no_access_to_page_and_export()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData, AccessRight.Finance]);
        using var _ = manager;

        var page = await manager.GetAsync(Page, followRedirects: false);
        Assert.Equal("/konto/zugriff-verweigert", page.RedirectLocation!.AbsolutePath);

        // Token from an allowed page so that only authorization applies
        var start = await manager.GetAsync("/verwaltung");
        using var response = await PostAsync(manager, [new("__RequestVerificationToken", Token(start))]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/konto/zugriff-verweigert", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Export_without_sign_in_redirects_to_sign_in()
    {
        using var browser = App.Browser();

        using var response = await PostAsync(browser, []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/konto/anmelden", response.Headers.Location!.AbsolutePath);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var folder = Path.GetDirectoryName(statusFile)!;
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static List<KeyValuePair<string, string>> ExportFormFields(Page page)
    {
        var form = page.Document.Forms.Single(f => f.GetAttribute("action") == Export);
        return form.Elements.OfType<IHtmlInputElement>().Select(i => new KeyValuePair<string, string>(i.Name!, i.Value)).ToList();
    }

    private static string Token(Page page) =>
        page.Document.QuerySelectorAll("input[name=__RequestVerificationToken]").OfType<IHtmlInputElement>().First().Value;

    private static async Task<HttpResponseMessage> PostAsync(TestBrowser browser, List<KeyValuePair<string, string>> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await browser.Http.PostAsync(new Uri(Export, UriKind.Relative), content, Ct);
    }
}
