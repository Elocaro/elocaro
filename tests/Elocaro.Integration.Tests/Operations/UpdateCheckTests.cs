using System.Net;
using System.Security.Claims;
using System.Text;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Operations;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Elocaro.Integration.Tests.Operations;

/// <summary>The hint on new versions: comparison, reading the GitHub releases, display data and the task on the dashboard.</summary>
public class UpdateCheckTests
{
    private const string Releases = """
        [
          { "tag_name": "v0.3.0-rc.1", "prerelease": true, "draft": false, "published_at": "2026-12-01T10:00:00Z", "html_url": "https://example.test/rc", "body": "### Hinzugefügt\n- Vorschau" },
          { "tag_name": "v0.2.0", "prerelease": false, "draft": false, "published_at": "2026-11-12T08:30:00Z", "html_url": "https://example.test/0.2.0",
            "body": "### Hinzugefügt\n- Datensicherung in der App: **mehrere Ziele**\n- Hinweis auf `neue Versionen`\n\n### Geändert\n- Umgestellt auf .NET 11" },
          { "tag_name": "v0.1.0", "prerelease": false, "draft": false, "published_at": "2026-10-29T22:00:00Z", "html_url": "https://example.test/0.1.0", "body": "Erste veröffentlichte Version." },
          { "tag_name": "v9.9.9", "prerelease": false, "draft": true, "published_at": null, "html_url": "", "body": "" },
          { "tag_name": "nightly", "prerelease": false, "draft": false, "published_at": "2026-11-01T00:00:00Z", "html_url": "", "body": "" }
        ]
        """;

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("v0.10.0", "0.9.9", true)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("0.3.0-rc.1", "0.2.0", true)]
    [InlineData("nightly", "0.2.0", false)]
    [InlineData("0.2.0", "Entwicklung", false)]
    [InlineData("0.2", "0.2.0", false)]
    public void Versions_are_compared_numerically(string candidate, string current, bool newer) =>
        Assert.Equal(newer, ReleaseVersion.IsNewer(candidate, current));

    [Fact]
    public void Releases_skip_drafts_prereleases_and_foreign_tags_and_are_sorted_newest_first()
    {
        var releases = ReleaseList.Parse(Releases);

        Assert.Equal(["0.2.0", "0.1.0"], releases.Select(r => r.Version));
        Assert.Equal(new DateOnly(2026, 11, 12), releases[0].Date);
        Assert.Equal("https://example.test/0.2.0", releases[0].Url);
    }

    [Fact]
    public void Release_notes_become_sections_with_entries()
    {
        var notes = ReleaseList.Parse(Releases)[0];

        Assert.Equal(["Hinzugefügt", "Geändert"], notes.Sections.Select(s => s.Title));
        Assert.Equal(["Datensicherung in der App: **mehrere Ziele**", "Hinweis auf `neue Versionen`"], notes.Sections[0].Items);
        Assert.Equal(["Umgestellt auf .NET 11"], notes.Sections[1].Items);
        Assert.Empty(ReleaseList.Parse(Releases)[1].Sections);
    }

    [Fact]
    public async Task Check_stores_the_releases_and_the_display_shows_what_is_newer()
    {
        var (checker, state, options) = Setup(new Stub(HttpStatusCode.OK, Releases));
        await checker.CheckAsync(TestContext.Current.CancellationToken);

        var info = await new UpdateInfoService(state, options).GetAsync(TestContext.Current.CancellationToken);

        Assert.True(info.UpdateAvailable);
        Assert.Equal("0.2.0", info.Latest!.Version);
        Assert.Equal("deploy/update.sh 0.2.0", UpdateInfo.UpdateCommand(info.Latest.Version));
        Assert.Equal("0.1.0", info.Installed!.Version);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero), info.CheckedAt);
        Assert.False(info.Failed);
    }

    [Fact]
    public async Task No_hint_when_the_running_version_is_current_or_a_development_build()
    {
        var (checker, state, options) = Setup(new Stub(HttpStatusCode.OK, Releases));
        await checker.CheckAsync(TestContext.Current.CancellationToken);

        options.Value.CurrentVersion = "0.2.0";
        Assert.False((await new UpdateInfoService(state, options).GetAsync(TestContext.Current.CancellationToken)).UpdateAvailable);

        options.Value.CurrentVersion = "Entwicklung";
        var development = await new UpdateInfoService(state, options).GetAsync(TestContext.Current.CancellationToken);
        Assert.Null(development.CurrentVersion);
        Assert.False(development.UpdateAvailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "[]")]
    [InlineData(HttpStatusCode.OK, "kein json")]
    public async Task A_failed_check_keeps_the_last_known_releases_and_reports_the_failure(HttpStatusCode status, string body)
    {
        var stub = new Stub(HttpStatusCode.OK, Releases);
        var (checker, state, options) = Setup(stub);
        await checker.CheckAsync(TestContext.Current.CancellationToken);

        stub.Next = (status, body);
        await checker.CheckAsync(TestContext.Current.CancellationToken);

        var info = await new UpdateInfoService(state, options).GetAsync(TestContext.Current.CancellationToken);
        Assert.True(info.Failed);
        Assert.Equal("0.2.0", info.Latest!.Version);
    }

    [Fact]
    public async Task A_disabled_check_asks_nobody()
    {
        var stub = new Stub(HttpStatusCode.OK, Releases);
        var (checker, state, options) = Setup(stub);
        options.Value.Enabled = false;

        await checker.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, stub.Calls);
        var info = await new UpdateInfoService(state, options).GetAsync(TestContext.Current.CancellationToken);
        Assert.False(info.Enabled);
        Assert.Empty(info.Releases);
    }

    [Fact]
    public async Task The_request_goes_to_the_releases_url_with_a_generic_client_name()
    {
        var stub = new Stub(HttpStatusCode.OK, Releases);
        var (checker, _, _) = Setup(stub);

        await checker.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://api.github.com/repos/elocaro/elocaro/releases?per_page=15", stub.LastUrl);
    }

    [Fact]
    public async Task The_dashboard_points_admins_to_a_newer_version_and_nobody_else()
    {
        var (checker, state, options) = Setup(new Stub(HttpStatusCode.OK, Releases));
        await checker.CheckAsync(TestContext.Current.CancellationToken);
        var updates = new UpdateInfoService(state, options);

        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));
        var system = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, nameof(AccessRight.System))], "test"));
        var manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, nameof(AccessRight.Finance))], "test"));

        var todos = await new UpdateTodos(updates, Options.Create(new DemoOptions())).TodosAsync(admin, TestContext.Current.CancellationToken);
        var item = Assert.Single(todos);
        Assert.Equal("Neue Version 0.2.0 verfügbar", item.Title);
        Assert.Equal("/verwaltung/system/neuigkeiten", item.Link);
        Assert.Equal(Elocaro.Application.Todos.Urgency.Info, item.Urgency);

        Assert.Single(await new UpdateTodos(updates, Options.Create(new DemoOptions())).TodosAsync(system, TestContext.Current.CancellationToken));
        Assert.Empty(await new UpdateTodos(updates, Options.Create(new DemoOptions())).TodosAsync(manager, TestContext.Current.CancellationToken));
        Assert.Empty(await new UpdateTodos(updates, Options.Create(new DemoOptions { Enabled = true })).TodosAsync(admin, TestContext.Current.CancellationToken));
    }

    private static (UpdateChecker Checker, UpdateState State, IOptions<UpdateCheckOptions> Options) Setup(Stub stub)
    {
        var options = Options.Create(new UpdateCheckOptions { CurrentVersion = "0.1.0" });
        var state = new UpdateState();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        return (new UpdateChecker(new HttpClient(stub), state, clock, options, NullLogger<UpdateChecker>.Instance), state, options);
    }

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Next { get; set; } = (status, body);

        public int Calls { get; private set; }

        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(Next.Status) { Content = new StringContent(Next.Body, Encoding.UTF8, "application/json") });
        }
    }
}
