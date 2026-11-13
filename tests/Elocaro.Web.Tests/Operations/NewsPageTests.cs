using System.Net;
using AngleSharp.Html.Dom;
using Elocaro.Application.Accounts;
using Elocaro.Application.Operations;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Operations;

/// <summary>"Neuigkeiten und Updates": hint on a newer version with the changes and the update command.</summary>
public class NewsPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Page = "/verwaltung/system/neuigkeiten";

    private static readonly ReleaseNotes V090 = new("0.9.0", new DateOnly(2026, 12, 1), "https://example.test/0.9.0",
        "### Hinzugefügt\n- Neue Auswertung: **Mieteingang** je *Jahr*\n  - Unterpunkt\n- Einstellung `X__Y` <script>alert(1)</script>");

    private static readonly ReleaseNotes V020 = new("0.2.0", new DateOnly(2026, 11, 12), "https://example.test/0.2.0",
        "### Geändert\n- Umgestellt auf .NET 11");

    private UpdateInfo info = new("0.2.0", true, null, false, []);

    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?>
    {
        ["UpdateCheck:Enabled"] = "false", // no call to GitHub from the test host
    };

    private int refreshes;

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IUpdateInfo>(new Stub(() => info, () => refreshes++));

    [Fact]
    public async Task A_newer_version_is_announced_with_its_changes_and_the_update_command()
    {
        info = new UpdateInfo("0.2.0", true, new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), false, [V090, V020]);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("Version 0.9.0 ist verfügbar", page.Document.QuerySelector("#news-status")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("deploy/update.sh 0.9.0", page.Document.QuerySelector("#news-command")!.TextContent.Trim());
        var newer = page.Document.QuerySelector("[data-version='0.9.0']")!;
        Assert.Contains("Mieteingang", newer.InnerHtml, StringComparison.Ordinal);
        Assert.Contains("<strong>Mieteingang</strong>", newer.InnerHtml, StringComparison.Ordinal);
        Assert.Contains("<code>X__Y</code>", newer.InnerHtml, StringComparison.Ordinal);
        Assert.Contains("<em>Jahr</em>", newer.InnerHtml, StringComparison.Ordinal);
        Assert.NotNull(newer.QuerySelector(".release-body li > ul > li"));
        Assert.DoesNotContain("<script>", newer.InnerHtml, StringComparison.Ordinal);
        Assert.NotNull(page.Document.QuerySelector("[data-version='0.2.0']")); // what is new in the running version
        Assert.Equal("0.2.0", page.Document.QuerySelector("#news-current")!.TextContent);
        Assert.NotNull(page.Document.QuerySelector("#news-link .nav-dot")); // the user menu points to the news
    }

    [Fact]
    public async Task The_current_version_is_reported_as_up_to_date_with_its_own_changes()
    {
        info = new UpdateInfo("0.2.0", true, new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), false, [V020]);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("auf dem neuesten Stand", page.Document.QuerySelector("#news-status")!.TextContent, StringComparison.Ordinal);
        Assert.Null(page.Document.QuerySelector("#news-command"));
        Assert.NotNull(page.Document.QuerySelector("#news-link"));
        Assert.Null(page.Document.QuerySelector("#news-link .nav-dot"));
        Assert.Contains("Umgestellt auf .NET 11", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_check_says_so_and_a_development_build_has_nothing_to_compare()
    {
        info = new UpdateInfo("0.2.0", false, null, false, []);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("UpdateCheck__Enabled=false", (await admin.GetAsync(Page)).Text, StringComparison.Ordinal);

        info = new UpdateInfo(null, true, null, false, []);
        var page = await admin.GetAsync(Page);
        Assert.Contains("Entwicklungsstand", page.Text, StringComparison.Ordinal);
        Assert.Equal("Entwicklung", page.Document.QuerySelector("#news-current")!.TextContent);
    }

    [Fact]
    public async Task A_failed_first_check_is_reported_without_a_hint()
    {
        info = new UpdateInfo("0.2.0", true, null, true, []);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("fehlgeschlagen", page.Document.QuerySelector("#news-status")!.TextContent, StringComparison.Ordinal);
        Assert.Null(page.Document.QuerySelector("#news-command"));
    }

    [Fact]
    public async Task Manager_without_system_permission_cannot_open_the_page()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager);
        using var _ = manager;

        Assert.Equal(HttpStatusCode.Redirect, (await manager.GetAsync(Page, followRedirects: false)).Status);
    }

    [Fact]
    public async Task The_check_button_asks_for_the_releases_right_away_and_returns_to_the_page()
    {
        info = new UpdateInfo("0.2.0", true, null, false, [V020]);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync(Page);
        var form = page.Document.QuerySelector("#news-check-form") as IHtmlFormElement;
        Assert.NotNull(form);
        Assert.NotNull(page.Document.QuerySelector("#news-check"));

        using var response = await PostAsync(admin, form.Elements.OfType<IHtmlInputElement>().Select(i => new KeyValuePair<string, string>(i.Name!, i.Value)).ToList());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Page, response.Headers.Location!.OriginalString);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task The_check_button_is_hidden_when_nothing_can_be_checked()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        info = new UpdateInfo("0.2.0", false, null, false, []);
        Assert.Null((await admin.GetAsync(Page)).Document.QuerySelector("#news-check"));

        info = new UpdateInfo(null, true, null, false, []);
        Assert.Null((await admin.GetAsync(Page)).Document.QuerySelector("#news-check"));
    }

    [Fact]
    public async Task The_check_without_antiforgery_token_or_system_permission_is_refused()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        using var withoutToken = await PostAsync(admin, []);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        var (manager, _) = await SignedInAsAsync(Roles.Manager);
        using var __ = manager;
        var start = await manager.GetAsync("/verwaltung");
        var token = start.Document.QuerySelectorAll("input[name=__RequestVerificationToken]").OfType<IHtmlInputElement>().First().Value;
        using var denied = await PostAsync(manager, [new("__RequestVerificationToken", token)]);

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/konto/zugriff-verweigert", denied.Headers.Location!.AbsolutePath);
        Assert.Equal(0, refreshes);
    }

    private static async Task<HttpResponseMessage> PostAsync(TestBrowser browser, List<KeyValuePair<string, string>> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await browser.Http.PostAsync(new Uri(Page + "/pruefen", UriKind.Relative), content, TestContext.Current.CancellationToken);
    }

    private sealed class Stub(Func<UpdateInfo> info, Action refreshed) : IUpdateInfo
    {
        public Task<UpdateInfo> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(info());

        public Task<UpdateInfo> RefreshAsync(CancellationToken cancellationToken = default)
        {
            refreshed();
            return GetAsync(cancellationToken);
        }
    }
}
