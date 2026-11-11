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
    public async Task Without_settings_the_page_says_that_the_environment_decides_and_offers_no_backup_button()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("deploy/.env", page.Text, StringComparison.Ordinal);
        Assert.Contains("verwaltet die App Ziele und Zeitplan", page.Text, StringComparison.Ordinal);
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
        Assert.DoesNotContain("verwaltet die App Ziele und Zeitplan", page.Text, StringComparison.Ordinal);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("connection refused");
            }

            return Task.FromResult(request.RequestUri!.AbsolutePath == "/status"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StatusJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
