using System.Net;
using System.Text;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>The backup still runs through the server configuration (.env): the page shows it and lets the app take it over in a dialog.</summary>
public class BackupEnvironmentBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private readonly Sidecar sidecar = new();

    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?>
    {
        ["Backup:Url"] = "http://backup:8080/",
        ["Backup:ApiToken"] = "test-token",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient<BackupSidecar>().ConfigurePrimaryHttpMessageHandler(() => sidecar);

    [Fact]
    public async Task The_target_of_the_server_configuration_is_shown_and_taken_over_by_the_app_in_a_dialog()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/system/datensicherung");
            var row = page.Locator("tr[data-target='Server-Konfiguration']");
            await Assertions.Expect(row).ToContainTextAsync("azure:elocaro-backup:/");
            await Assertions.Expect(row).ToContainTextAsync("täglich um 02:30");
            await Assertions.Expect(page.Locator("#backup-snapshots tbody tr")).ToHaveCountAsync(2);

            // "Bearbeiten": the dialog starts with what the configuration says; credentials and password are entered again
            await row.GetByRole(AriaRole.Button, new() { Name = "Bearbeiten" }).ClickAsync();
            var dialog = page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(dialog).ToContainTextAsync("Dieses Ziel kommt aus der Server-Konfiguration");
            await Assertions.Expect(dialog.GetByLabel("Ablage")).ToHaveValueAsync("Azure Blob Storage");
            await Assertions.Expect(dialog.GetByLabel("Container")).ToHaveValueAsync("elocaro-backup");
            await Assertions.Expect(dialog.GetByLabel("Uhrzeit")).ToHaveValueAsync("02:30");
            await dialog.GetByLabel("Name", new() { Exact = true }).FillAsync("Azure");
            await dialog.GetByLabel("Name des Speicherkontos").FillAsync("konto");
            await dialog.GetByLabel("Zugriffsschlüssel").FillAsync("schluessel");
            await dialog.GetByLabel("Passwort der Sicherung").FillAsync("passwort");
            await dialog.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");

            // From now on the app manages it: the row of the configuration is gone, the target is listed
            await Assertions.Expect(page.Locator("tr[data-target='Azure']")).ToContainTextAsync("Azure Blob Storage");
            await Assertions.Expect(page.Locator("tr[data-target='Server-Konfiguration']")).ToHaveCountAsync(0);
        });

        var saved = await App.WithScopeAsync(async sp => await sp.GetRequiredService<IBackupSettings>().ListAsync());
        var target = Assert.Single(saved);
        Assert.Equal(BackupProvider.Azure, target.Provider);
        Assert.Equal("elocaro-backup", target.Location);
        Assert.True(sidecar.Managed);
    }

    private sealed class Sidecar : HttpMessageHandler
    {
        public bool Managed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/managed")
            {
                Managed = true;
            }

            var body = path switch
            {
                "/status" => """{"running":false}""",
                "/environment" => Managed
                    ? null
                    : """{"repository":"azure:elocaro-backup:/","time":"02:30","keepDaily":7,"keepWeekly":4,"keepMonthly":12,"paused":false}""",
                "/environment/snapshots" => """[{"id":"bbbbbbbb22222222","shortId":"bbbbbbbb","time":"2026-03-02T01:30:00Z"},{"id":"aaaaaaaa11111111","shortId":"aaaaaaaa","time":"2026-03-01T01:30:00Z"}]""",
                _ => null,
            };
            var response = body is not null
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(path == "/environment" ? HttpStatusCode.NotFound : HttpStatusCode.NoContent);
            return Task.FromResult(response);
        }
    }
}
