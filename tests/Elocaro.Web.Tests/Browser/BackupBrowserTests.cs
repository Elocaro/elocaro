using System.Net.Http.Headers;
using System.Net.Http.Json;
using Elocaro.Application.Admin;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Npgsql;

namespace Elocaro.Web.Tests.Browser;

/// <summary>
/// Setting up, running and restoring a backup through the real user interface, against the real backup container (restic, pg_dump)
/// and a real database. Needs Docker and the image from <c>deploy/backup/Dockerfile</c>: set <c>ELOCARO_BACKUP_IMAGE</c>
/// (e.g. <c>docker build -f deploy/backup/Dockerfile -t elocaro-backup:lokal .</c>); without it the tests are skipped.
/// </summary>
public class BackupBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string Token = "browser-test-token";
    private const string RepositoryPassword = "ui-test-passwort";
    private const string Page = "/verwaltung/system/datensicherung";
    private const string Original = "Original Vermietung GmbH";

    /// <summary>Steps that wait for restic in the container: generous, because the whole test run keeps the machine busy.</summary>
    private const float Restic = 60_000;

    private readonly int port = BackupSidecarContainer.FreePort();
    private readonly string work = Path.Combine(Path.GetTempPath(), "elocaro-backup-browser", Guid.NewGuid().ToString("N"));
    private readonly RecordingRestart restart = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?>
    {
        ["Backup:Url"] = $"http://127.0.0.1:{port}/",
        ["Backup:ApiToken"] = Token,
        ["Backup:StatusFile"] = Path.Combine(work, "status", "backup.json"),
    };

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IAppRestart>(restart);

    private string StoredFile => Path.Combine(App.FilePath, "ab", "mietvertrag.txt");

    private Task<BackupSidecarContainer> StartSidecarAsync() =>
        BackupSidecarContainer.StartAsync(BackupSidecarContainer.Image!, port, Token, App.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Elocaro"]!,
            App.FilePath, Path.Combine(work, "repo"), Path.Combine(work, "status"), work);

    private async Task WriteStateAsync(string landlord)
    {
        await App.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<ILandlordProfileService>().SaveAsync(new LandlordProfileInput { Name = landlord, Street = "Musterweg 1", PostalCode = "12345", City = "Musterstadt" }, Ct);
            return true;
        });
    }

    private async Task<string?> LandlordAsync() =>
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<ILandlordProfileService>().GetAsync(Ct))?.Name);

    /// <summary>The app restarts after a restore; the test stays in the same process, so only its connections are renewed.</summary>
    private static void SimulateRestart() => NpgsqlConnection.ClearAllPools();

    [Fact]
    public async Task Admin_sets_up_the_backup_runs_it_and_restores_an_earlier_state()
    {
        if (BackupSidecarContainer.Image is null)
        {
            Assert.Skip($"Nur mit dem Backup-Image: {BackupSidecarContainer.ImageVariable}=elocaro-backup:lokal (docker build -f deploy/backup/Dockerfile -t elocaro-backup:lokal .)");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(StoredFile)!);
        await File.WriteAllTextAsync(StoredFile, "Mietvertrag Original", Ct);
        await WriteStateAsync(Original);
        await using var sidecar = await StartSidecarAsync();
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, Page);
            var main = page.Locator("main");

            // Not set up yet: the page explains that the environment decides and reaches the backup service
            await Assertions.Expect(main).ToContainTextAsync("verwaltet die App Ziel und Zeitplan");
            await Assertions.Expect(page.Locator("#backup-now")).ToHaveCountAsync(0);

            // Set up: the local repository (default), password, time
            await page.GetByLabel("Passwort der Sicherung").FillAsync(RepositoryPassword);
            await page.GetByLabel("Uhrzeit").FillAsync("03:15");
            await page.GetByRole(AriaRole.Button, new() { Name = "Verbindung testen" }).ClickAsync();
            await Assertions.Expect(page.Locator("#backup-check")).ToContainTextAsync("noch kein Repository");

            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Einstellungen der Datensicherung wurden gespeichert.");
            await Assertions.Expect(page.Locator("#backup-now")).ToBeVisibleAsync();
            await Assertions.Expect(main).ToContainTextAsync("Gespeichert. Leer lassen, um es zu behalten.");
            await Assertions.Expect(main).Not.ToContainTextAsync("verwaltet die App Ziel und Zeitplan");

            // The confirmation covers the main action of the page at the top right and does not go away in the headless browser:
            // the click goes straight to the button.
            // Back up now; the page follows the progress by itself
            await page.Locator("#backup-now").DispatchEventAsync("click");
            await Assertions.Expect(main).ToContainTextAsync("Eine Sicherung läuft", new() { Timeout = 30_000 });
            await Assertions.Expect(main).ToContainTextAsync("erfolgreich.", new() { Timeout = 120_000 });
            await Assertions.Expect(main).Not.ToContainTextAsync("Eine Sicherung läuft");

            await page.Locator("#backup-load").ClickAsync();
            await Assertions.Expect(page.Locator("table.data-table tbody tr")).ToHaveCountAsync(1, new() { Timeout = Restic });
        });

        // What was saved: encrypted password, the schedule taken over from the container environment
        var setting = await App.WithScopeAsync(async sp => await sp.GetRequiredService<ElocaroDbContext>().Set<BackupSetting>().AsNoTracking().SingleAsync(Ct));
        Assert.Equal(BackupProvider.Local, setting.Provider);
        Assert.Equal(new TimeOnly(3, 15), setting.Time);
        Assert.DoesNotContain(RepositoryPassword, setting.ProtectedPassword, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(work, "status", "managed-by-app")));
        Assert.Contains("\"succeeded\":true", await File.ReadAllTextAsync(Path.Combine(work, "status", "backup.json"), Ct), StringComparison.Ordinal);

        // Change data and files after the backup
        await WriteStateAsync("Geänderte Vermietung");
        await sidecar.ExecAsync("rm", "/data/ab/mietvertrag.txt");
        Assert.Equal("Geänderte Vermietung", await LandlordAsync());

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var main = page.Locator("main");
            await page.GetByRole(AriaRole.Button, new() { Name = "Wiederherstellen …" }).ClickAsync();
            await Assertions.Expect(page.Locator("#restore-confirm")).ToBeVisibleAsync();

            // Only enabled after the confirmation word was typed
            await Assertions.Expect(page.Locator("#restore-start")).ToBeDisabledAsync();
            await page.Locator("#restore-confirm input").FillAsync("wiederherstellen");
            await Assertions.Expect(page.Locator("#restore-start")).ToBeDisabledAsync();
            await page.Locator("#restore-confirm input").FillAsync("WIEDERHERSTELLEN");
            await Assertions.Expect(page.Locator("#restore-start")).ToBeEnabledAsync();
            await page.Locator("#restore-start").ClickAsync();

            await Assertions.Expect(page.Locator("#restore-result")).ToContainTextAsync("Die Wiederherstellung ist abgeschlossen", new() { Timeout = 120_000 });
            await Assertions.Expect(page.Locator("#restore-result")).ToContainTextAsync("startet in wenigen Sekunden neu");
        });

        // The app was told to restart (the test host must not stop, so it only records it)
        Assert.Equal(1, restart.Count);
        SimulateRestart();

        Assert.Equal(Original, await LandlordAsync());
        Assert.Equal("Mietvertrag Original", await File.ReadAllTextAsync(StoredFile, Ct));
        // The settings themselves came back with the database
        Assert.Equal(1, await App.WithScopeAsync(async sp => await sp.GetRequiredService<ElocaroDbContext>().Set<BackupSetting>().CountAsync(Ct)));
    }

    [Fact]
    public async Task A_new_installation_restores_the_backup_in_the_first_setup()
    {
        if (BackupSidecarContainer.Image is null)
        {
            Assert.Skip($"Nur mit dem Backup-Image: {BackupSidecarContainer.ImageVariable}=elocaro-backup:lokal (docker build -f deploy/backup/Dockerfile -t elocaro-backup:lokal .)");
            return;
        }

        const string email = "bestand@example.de";
        Directory.CreateDirectory(Path.GetDirectoryName(StoredFile)!);
        await File.WriteAllTextAsync(StoredFile, "Mietvertrag Original", Ct);
        await WriteStateAsync(Original);
        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await manager.CreateAsync(new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Bestand" }, Password)).Succeeded);
            return true;
        });
        await using var sidecar = await StartSidecarAsync();
        await BackUpThroughTheApiAsync();

        // "New server": no accounts, files gone
        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var user in manager.Users.ToList())
            {
                Assert.True((await manager.DeleteAsync(user)).Succeeded);
            }

            return true;
        });
        await sidecar.ExecAsync("rm", "-rf", "/data/ab");
        Assert.False(File.Exists(StoredFile));

        var page = await NewPageAsync(mobile: false);
        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // The first setup offers the way
            await page.GotoAsync(BaseUrl + "/konto/einrichtung");
            await page.GetByRole(AriaRole.Link, new() { Name = "Aus einer Datensicherung wiederherstellen" }).ClickAsync();
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            // Wrong setup code: nothing happens
            await page.GetByLabel("Einrichtungscode").FillAsync("falscher-code");
            await page.GetByLabel("Passwort der Sicherung").FillAsync(RepositoryPassword);
            await page.Locator("#restore-find").ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Der Einrichtungscode stimmt nicht.");

            // Wrong repository password: reported, no list
            await page.GetByLabel("Einrichtungscode").FillAsync(ElocaroWebFactory.SetupCode);
            await page.GetByLabel("Passwort der Sicherung").FillAsync("falsches-passwort");
            await page.Locator("#restore-find").ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Passwort", new() { Timeout = Restic });
            await Assertions.Expect(page.Locator("table.data-table")).ToHaveCountAsync(0);

            // Right password: the backups are listed, restore the newest
            await page.GetByLabel("Passwort der Sicherung").FillAsync(RepositoryPassword);
            await page.Locator("#restore-find").ClickAsync();
            await Assertions.Expect(page.Locator("table.data-table tbody tr")).ToHaveCountAsync(1, new() { Timeout = Restic });
            await page.GetByRole(AriaRole.Button, new() { Name = "Diesen Stand wiederherstellen" }).ClickAsync();

            await Assertions.Expect(page.Locator("#restore-done")).ToContainTextAsync("Die Wiederherstellung ist abgeschlossen", new() { Timeout = 120_000 });
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Zur Anmeldung" })).ToBeVisibleAsync();
        });

        Assert.Equal(1, restart.Count);
        SimulateRestart();

        // The accounts and data of the old server are back
        Assert.NotNull(await App.WithScopeAsync(async sp => await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email)));
        Assert.Equal(Original, await LandlordAsync());
        Assert.Equal("Mietvertrag Original", await File.ReadAllTextAsync(StoredFile, Ct));
    }

    /// <summary>A backup through the API of the container, as the old server made it.</summary>
    private async Task BackUpThroughTheApiAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var started = await http.PostAsJsonAsync(new Uri("backup", UriKind.Relative), new { target = new { repository = "/repo", password = RepositoryPassword } }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, started.StatusCode);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var status = await http.GetStringAsync(new Uri("status", UriKind.Relative), Ct);
            if (status.Contains("\"running\":false", StringComparison.Ordinal) && status.Contains("\"succeeded\":true", StringComparison.Ordinal))
            {
                return;
            }

            Assert.DoesNotContain("\"succeeded\":false", status, StringComparison.Ordinal);
            await Task.Delay(1000, Ct);
        }

        Assert.Fail("Die Sicherung wurde nicht fertig.");
    }

    private sealed class RecordingRestart : IAppRestart
    {
        private int count;

        public int Count => count;

        public void Schedule() => Interlocked.Increment(ref count);
    }
}
