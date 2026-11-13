using System.Net;
using System.Text;
using Elocaro.Application.Accounts;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Operations;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Operations;

public class BackupSettingsPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Page = "/verwaltung/system/datensicherung";

    private readonly Sidecar sidecar = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?>
    {
        ["Backup:Url"] = "http://backup:8080/",
        ["Backup:ApiToken"] = "test-token",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient<BackupSidecar>().ConfigurePrimaryHttpMessageHandler(() => sidecar);

    [Fact]
    public async Task Without_any_target_the_page_says_that_nothing_is_set_up_and_offers_to_add_one()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("Noch keine Sicherung eingerichtet", page.Text, StringComparison.Ordinal);
        Assert.Contains("Ziel hinzufügen", page.Text, StringComparison.Ordinal);
        // Not the old claim that the server configuration decides (there is no such target), nor the monitoring message
        Assert.DoesNotContain("Die Sicherung läuft derzeit nach der Server-Konfiguration", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Backup-Überwachung ist nicht eingerichtet", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Jetzt sichern", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("von hier aus nicht erreichbar", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_backup_service_is_explained()
    {
        sidecar.Offline = true;
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("von hier aus nicht erreichbar", page.Text, StringComparison.Ordinal);
        Assert.Contains("BACKUP_API_TOKEN", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_a_saved_target_the_page_lists_it_with_its_actions_and_never_shows_secrets()
    {
        var input = new BackupSettingsInput { Name = "Hetzner", Provider = BackupProvider.S3, Location = "https://s3.example.com/bucket/elocaro", Password = "super-geheimes-passwort" };
        input.Credentials["AWS_ACCESS_KEY_ID"] = "AKIAZUGANG";
        input.Credentials["AWS_SECRET_ACCESS_KEY"] = "super-geheimer-schluessel";
        var saved = await App.WithScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().SaveAsync(null, input, Ct));
        Assert.True(saved.Succeeded);
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("Hetzner", page.Text, StringComparison.Ordinal);
        Assert.Contains("täglich um 02:30", page.Text, StringComparison.Ordinal);
        Assert.Contains("Jetzt sichern", page.Text, StringComparison.Ordinal);
        Assert.Contains("Bearbeiten", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("super-geheimes-passwort", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("super-geheimer-schluessel", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Noch keine Sicherung eingerichtet", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_target_from_the_server_configuration_shows_where_what_is_backed_up_and_lists_the_backups()
    {
        sidecar.EnvironmentJson = """{"repository":"azure:elocaro-backup:/","time":"02:30","keepDaily":7,"keepWeekly":4,"keepMonthly":12,"paused":false}""";
        sidecar.SnapshotsJson = """[{"id":"bbbbbbbb22222222","shortId":"bbbbbbbb","time":"2026-03-02T01:30:00Z","size":3865470566},{"id":"aaaaaaaa11111111","shortId":"aaaaaaaa","time":"2026-03-01T01:30:00Z"}]""";
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("azure:elocaro-backup:/", page.Text, StringComparison.Ordinal);
        Assert.Contains("täglich um 02:30", page.Text, StringComparison.Ordinal);
        Assert.Contains("7 täglich, 4 wöchentlich, 12 monatlich", page.Text, StringComparison.Ordinal);
        // The list of the backups is read after the page appeared (it can be slow); the page itself says so. The list is checked in the browser
        // (BackupEnvironmentBrowserTests).
        Assert.Contains("Vorhandene Sicherungen", page.Text, StringComparison.Ordinal);
        Assert.Contains("Sicherungen werden geladen", page.Text, StringComparison.Ordinal);
        Assert.Contains("Jetzt sichern", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_running_restore_is_shown()
    {
        sidecar.StatusJson = """{"running":true,"kind":"Restore"}""";
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Die Wiederherstellung läuft", (await admin.GetAsync(Page)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_without_system_permission_cannot_open_the_page()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager);
        using var _ = manager;

        Assert.Equal(HttpStatusCode.Redirect, (await manager.GetAsync(Page, followRedirects: false)).Status);
    }

    private sealed class Sidecar : HttpMessageHandler
    {
        public bool Offline { get; set; }

        public string StatusJson { get; set; } = """{"running":false}""";

        public string? EnvironmentJson { get; set; }

        public string SnapshotsJson { get; set; } = "[]";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("connection refused");
            }

            var path = request.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/status" => StatusJson,
                "/environment" => EnvironmentJson,
                "/environment/snapshots" => SnapshotsJson,
                _ => null,
            };
            return Task.FromResult(path == "/environment" && body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : body is null
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
