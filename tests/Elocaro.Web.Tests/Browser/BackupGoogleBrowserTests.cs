using System.Text.Json;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Adding a Google Drive target through the sign-in at Google (device code), with Google replaced by a stand-in.</summary>
public class BackupGoogleBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string Code = "TEST-CODE-1234";

    private readonly FakeGoogle google = new();

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IGoogleDriveAuthorization>(google);

    [Fact]
    public async Task A_google_drive_target_is_set_up_by_signing_in_at_google()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/system/datensicherung");
            await page.Locator("#backup-add").ClickAsync();

            // A new target starts with real default values: Google Drive is chosen, the name follows it
            await Assertions.Expect(page.Locator("#provider-googledrive")).ToHaveAttributeAsync("aria-checked", "true");
            await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Google Drive");
            await Assertions.Expect(page.GetByLabel("Passwort der Sicherung")).ToHaveValueAsync("");
            await Assertions.Expect(page.GetByLabel("Uhrzeit")).ToHaveValueAsync("02:30");

            await page.Locator("#provider-s3").ClickAsync();
            await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("S3-Speicher");
            await page.Locator("#provider-googledrive").ClickAsync();
            await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Google Drive");

            // The folder has a default, nothing but the sign-in is left to do
            await Assertions.Expect(page.GetByLabel("Ordner in Google Drive")).ToHaveValueAsync("Elocaro-Sicherung");
            await Assertions.Expect(page.Locator("#drive-connect-button")).ToHaveTextAsync("Mit Google anmelden");

            await page.Locator("#drive-connect-button").ClickAsync();
            await Assertions.Expect(page.Locator("#drive-code")).ToHaveTextAsync(Code);
            await Assertions.Expect(page.Locator("#drive-device a")).ToHaveAttributeAsync("href", "https://www.google.com/device");
            await Assertions.Expect(page.Locator("#drive-device")).ToContainTextAsync("Warte auf die Bestätigung");

            // The person confirms at Google; the page notices by itself
            await Assertions.Expect(page.Locator("#drive-connected")).ToContainTextAsync("Bei Google angemeldet", new() { Timeout = 30_000 });
            await Assertions.Expect(page.Locator("#drive-connect-button")).ToHaveTextAsync("Neu anmelden");

            await page.GetByLabel("Passwort der Sicherung").FillAsync("drive-test-passwort");
            await page.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");
            var row = page.Locator("tr[data-target='Google Drive']");
            await Assertions.Expect(row).ToContainTextAsync("Google Drive");

            // Editing shows what was saved: values in the fields, secrets only as "stored"
            await row.GetByRole(AriaRole.Button, new() { Name = "Bearbeiten" }).ClickAsync();
            await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Google Drive");
            await Assertions.Expect(page.GetByLabel("Ordner in Google Drive")).ToHaveValueAsync("Elocaro-Sicherung");
            await Assertions.Expect(page.GetByLabel("Uhrzeit")).ToHaveValueAsync("02:30");
            await Assertions.Expect(page.GetByLabel("Passwort der Sicherung")).ToHaveValueAsync("");
            await Assertions.Expect(page.Locator("#backup-edit")).ToContainTextAsync("Gespeichert. Leer lassen, um es zu behalten.");
            await Assertions.Expect(page.Locator("#drive-connected")).ToContainTextAsync("Bei Google angemeldet");
            await Assertions.Expect(page.Locator("#drive-connect-button")).ToHaveTextAsync("Neu anmelden");
        });

        // Token and client are stored (encrypted), nothing of it is in the page
        var view = Assert.Single(await App.WithScopeAsync(async sp => await sp.GetRequiredService<IBackupSettings>().ListAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(BackupProvider.GoogleDrive, view.Provider);
        Assert.Equal("Elocaro-Sicherung", view.Location);
        Assert.Contains(BackupProviders.DriveTokenKey, view.StoredCredentials);
        Assert.Contains(BackupProviders.DriveClientIdKey, view.StoredCredentials);
        Assert.DoesNotContain("at-secret", await page.ContentAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("rt-secret", await page.ContentAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_at_google_is_shown_and_the_sign_in_can_be_repeated()
    {
        google.Refuse = true;
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/system/datensicherung");
            await page.Locator("#backup-add").ClickAsync();
            await page.Locator("#provider-googledrive").ClickAsync();
            await page.Locator("#drive-connect-button").ClickAsync();

            await Assertions.Expect(page.Locator("#drive-error")).ToContainTextAsync("abgelehnt", new() { Timeout = 30_000 });
            await Assertions.Expect(page.Locator("#drive-connect-button")).ToHaveTextAsync("Mit Google anmelden");
            await Assertions.Expect(page.Locator("#drive-device")).ToHaveCountAsync(0);
        });
    }

    private sealed class FakeGoogle : IGoogleDriveAuthorization
    {
        private int polls;

        public bool Refuse { get; set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<DeviceAuthorizationStart> StartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceAuthorizationStart(new DeviceAuthorization("device-1", Code, "https://www.google.com/device", 600, 1), null));

        public Task<DeviceAuthorizationResult> PollAsync(string deviceCode, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref polls) < 2)
            {
                return Task.FromResult(new DeviceAuthorizationResult(DeviceAuthorizationState.Pending));
            }

            if (Refuse)
            {
                return Task.FromResult(new DeviceAuthorizationResult(DeviceAuthorizationState.Denied, Message: "Der Zugriff wurde bei Google abgelehnt."));
            }

            var token = JsonSerializer.Serialize(new { access_token = "at-secret", refresh_token = "rt-secret", token_type = "Bearer", expiry = "2030-01-01T00:00:00Z" });
            return Task.FromResult(new DeviceAuthorizationResult(DeviceAuthorizationState.Authorized, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BackupProviders.DriveTokenKey] = token,
                [BackupProviders.DriveClientIdKey] = "client-id",
                [BackupProviders.DriveClientSecretKey] = "client-secret",
            }));
        }
    }
}
