using System.Net;
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
        [new("Hinzugefügt", ["Neue Auswertung: **Mieteingang** je Jahr", "Einstellung `X__Y` <script>alert(1)</script>"])]);

    private static readonly ReleaseNotes V020 = new("0.2.0", new DateOnly(2026, 11, 12), "https://example.test/0.2.0",
        [new("Geändert", ["Umgestellt auf .NET 11"])]);

    private UpdateInfo info = new("0.2.0", true, null, false, []);

    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?>
    {
        ["UpdateCheck:Enabled"] = "false", // no call to GitHub from the test host
    };

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IUpdateInfo>(new Stub(() => info));

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
        Assert.DoesNotContain("<script>", newer.InnerHtml, StringComparison.Ordinal);
        Assert.NotNull(page.Document.QuerySelector("[data-version='0.2.0']")); // what is new in the running version
        Assert.Equal("0.2.0", page.Document.QuerySelector("#news-current")!.TextContent);
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

    private sealed class Stub(Func<UpdateInfo> info) : IUpdateInfo
    {
        public Task<UpdateInfo> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(info());
    }
}
