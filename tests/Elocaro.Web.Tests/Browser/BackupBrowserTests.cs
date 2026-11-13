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

    /// <summary>
    /// The app restarts after a restore; the test stays in the same process, so only the connections to its own database are renewed.
    /// (ClearAllPools would cut the connections of every test running at the same time.)
    /// </summary>
    private void SimulateRestart()
    {
        using var connection = new NpgsqlConnection(App.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Elocaro"]);
        NpgsqlConnection.ClearPool(connection);
    }

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

            // Not set up yet: the page says so and offers to add a target
            await Assertions.Expect(main).ToContainTextAsync("Noch keine Sicherung eingerichtet");
            // The empty state offers the first target right there: the same dialog as the button at the top
            await page.Locator("#backup-empty-add").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Dialog).Locator("#backup-cancel").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#backup-targets")).ToHaveCountAsync(0);

            // Add a target: the local repository (default), name, password, time
            await page.Locator("#backup-add").ClickAsync();
            await page.Locator("#provider-local").ClickAsync();
            await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Dieser Server");
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Lokal");
            await FillKeptAsync(page.GetByLabel("Passwort der Sicherung"), RepositoryPassword);
            await page.GetByLabel("Uhrzeit").FillAsync("03:15");
            await page.GetByRole(AriaRole.Button, new() { Name = "Verbindung testen" }).ClickAsync();
            await Assertions.Expect(page.Locator("#backup-check")).ToContainTextAsync("noch kein Repository", new() { Timeout = Restic });

            await page.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");
            var row = page.Locator("tr[data-target='Lokal']");
            await Assertions.Expect(row).ToContainTextAsync("täglich um 03:15");
            await Assertions.Expect(row).ToContainTextAsync("noch keine Sicherung");
            await Assertions.Expect(main).Not.ToContainTextAsync("Noch keine Sicherung eingerichtet");

            // The confirmation covers the buttons at the top right and does not go away in the headless browser:
            // the click goes straight to the button. Back up now; the page follows the progress by itself.
            await row.GetByRole(AriaRole.Button, new() { Name = "Jetzt sichern" }).DispatchEventAsync("click");
            await Assertions.Expect(main).ToContainTextAsync("Eine Sicherung läuft: Lokal", new() { Timeout = 30_000 });
            await Assertions.Expect(page.Locator("#backup-running [role=progressbar]")).ToBeVisibleAsync();
            await Assertions.Expect(row).ToContainTextAsync("aktuell", new() { Timeout = 300_000 });
            await Assertions.Expect(main).Not.ToContainTextAsync("Eine Sicherung läuft");

            // The list of all backups needs no click: it shows up once the backup is done.
            await page.Locator("#snapshots-refresh").ClickAsync();
            await Assertions.Expect(page.Locator("#backup-snapshots table.data-table tbody tr")).ToHaveCountAsync(1, new() { Timeout = Restic });
        });

        // What was saved: encrypted password, the schedule taken over from the container environment
        var setting = await App.WithScopeAsync(async sp => await sp.GetRequiredService<ElocaroDbContext>().Set<BackupSetting>().AsNoTracking().SingleAsync(Ct));
        Assert.Equal("Lokal", setting.Name);
        Assert.Equal(BackupProvider.Local, setting.Provider);
        Assert.Equal(new TimeOnly(3, 15), setting.Time);
        Assert.DoesNotContain(RepositoryPassword, setting.ProtectedPassword, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(work, "status", "managed-by-app")));
        // One status file per target: the app reads the schedule and the monitoring from it
        Assert.Contains("\"succeeded\":true", await File.ReadAllTextAsync(Path.Combine(work, "status", $"backup-{setting.Id:N}.json"), Ct), StringComparison.Ordinal);

        // Change data and files after the backup
        await WriteStateAsync("Geänderte Vermietung");
        await sidecar.ExecAsync("rm", "/data/ab/mietvertrag.txt");
        Assert.Equal("Geänderte Vermietung", await LandlordAsync());

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var main = page.Locator("main");
            await page.GetByRole(AriaRole.Button, new() { Name = "Wiederherstellen", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#restore-confirm")).ToBeVisibleAsync();

            // Only enabled after the confirmation word was typed
            await Assertions.Expect(page.Locator("#restore-start")).ToBeDisabledAsync();
            await page.Locator("#restore-confirm input").FillAsync("wiederherstellen");
            await Assertions.Expect(page.Locator("#restore-start")).ToBeDisabledAsync();
            await page.Locator("#restore-confirm input").FillAsync("WIEDERHERSTELLEN");
            await Assertions.Expect(page.Locator("#restore-start")).ToBeEnabledAsync();
            await page.Locator("#restore-start").ClickAsync();

            await Assertions.Expect(page.Locator("#restore-result")).ToContainTextAsync("Die Wiederherstellung ist abgeschlossen", new() { Timeout = 300_000 });
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
    public async Task Admin_sees_the_size_of_each_backup_and_deletes_one_of_two()
    {
        if (BackupSidecarContainer.Image is null)
        {
            Assert.Skip($"Nur mit dem Backup-Image: {BackupSidecarContainer.ImageVariable}=elocaro-backup:lokal (docker build -f deploy/backup/Dockerfile -t elocaro-backup:lokal .)");
            return;
        }

        await WriteStateAsync(Original);
        await using var sidecar = await StartSidecarAsync();
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, Page);
            await page.Locator("#backup-add").ClickAsync();
            await page.Locator("#provider-local").ClickAsync();
            await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Dieser Server");
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Lokal");
            await page.GetByLabel("Passwort der Sicherung").FillAsync(RepositoryPassword);
            await page.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");

            // Two backups, one after the other (the confirmation covers the buttons at the top right: click straight on the button)
            var row = page.Locator("tr[data-target='Lokal']");
            var snapshots = page.Locator("#backup-snapshots tbody tr");
            for (var count = 1; count <= 2; count++)
            {
                await row.GetByRole(AriaRole.Button, new() { Name = "Jetzt sichern" }).DispatchEventAsync("click");
                await Assertions.Expect(page.Locator("#backup-snapshots tbody tr")).ToHaveCountAsync(count, new() { Timeout = 300_000 });
                await Assertions.Expect(page.Locator("#backup-running")).ToHaveCountAsync(0, new() { Timeout = 300_000 });
            }

            // The size of the backed-up data is shown for each backup (restic records it)
            await Assertions.Expect(snapshots.Nth(0)).ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"\d+(,\d)? (KB|MB|GB)"));
            await Assertions.Expect(snapshots.Nth(1)).ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"\d+(,\d)? (KB|MB|GB)"));

            // Delete the older one: asks first, "No" keeps it
            await snapshots.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Löschen" }).DispatchEventAsync("click");
            var confirmation = page.GetByRole(AriaRole.Dialog, new() { Name = "Sicherung löschen" });
            await Assertions.Expect(confirmation).ToContainTextAsync("endgültig gelöscht");
            await confirmation.GetByRole(AriaRole.Button, new() { Name = "Abbrechen" }).ClickAsync();
            await Assertions.Expect(snapshots).ToHaveCountAsync(2);

            await snapshots.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Löschen" }).DispatchEventAsync("click");
            await page.GetByRole(AriaRole.Dialog, new() { Name = "Sicherung löschen" }).GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Sicherung wird gelöscht.");
            await Assertions.Expect(snapshots).ToHaveCountAsync(1, new() { Timeout = 300_000 });
            await Assertions.Expect(page.Locator("#backup-running")).ToHaveCountAsync(0, new() { Timeout = 300_000 });
        });
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
            await FillKeptAsync(page.GetByLabel("Einrichtungscode"), "falscher-code");
            await FillKeptAsync(page.GetByLabel("Passwort der Sicherung"), RepositoryPassword);
            await page.Locator("#restore-find").ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Der Einrichtungscode stimmt nicht.");

            // Wrong repository password: reported, no list
            await FillKeptAsync(page.GetByLabel("Einrichtungscode"), ElocaroWebFactory.SetupCode);
            await FillKeptAsync(page.GetByLabel("Passwort der Sicherung"), "falsches-passwort");
            await page.Locator("#restore-find").ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Passwort", new() { Timeout = Restic });
            await Assertions.Expect(page.Locator("table.data-table")).ToHaveCountAsync(0);

            // Right password: the backups are listed, restore the newest
            await FillKeptAsync(page.GetByLabel("Passwort der Sicherung"), RepositoryPassword);
            await page.Locator("#restore-find").ClickAsync();
            await Assertions.Expect(page.Locator("table.data-table tbody tr")).ToHaveCountAsync(1, new() { Timeout = Restic });
            await page.GetByRole(AriaRole.Button, new() { Name = "Diesen Stand wiederherstellen" }).ClickAsync();

            await Assertions.Expect(page.Locator("#restore-done")).ToContainTextAsync("Die Wiederherstellung ist abgeschlossen", new() { Timeout = 300_000 });
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Zur Anmeldung" })).ToBeVisibleAsync();
        });

        Assert.Equal(1, restart.Count);
        SimulateRestart();

        // The accounts and data of the old server are back
        Assert.NotNull(await App.WithScopeAsync(async sp => await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email)));
        Assert.Equal(Original, await LandlordAsync());
        Assert.Equal("Mietvertrag Original", await File.ReadAllTextAsync(StoredFile, Ct));
    }

    /// <summary>
    /// Fills a field of an interactive page until the value is still there a moment later. Under load the page may reconnect after
    /// the first input and start again from its initial state; the entry is then offered again (like the file upload of the other tests).
    /// </summary>
    private static async Task FillKeptAsync(ILocator field, string value)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await field.FillAsync(value);
            await field.Page.WaitForTimeoutAsync(400);
            if (await field.InputValueAsync() == value)
            {
                return;
            }
        }

        await Assertions.Expect(field).ToHaveValueAsync(value);
    }

    /// <summary>A backup through the API of the container, as the old server made it.</summary>
    [Fact]
    public async Task A_backup_is_downloaded_as_a_file_and_restored_from_the_upload()
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
        var downloaded = Path.Combine(work, "heruntergeladen.tar");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, Page);
            await page.Locator("#backup-add").ClickAsync();
            await page.Locator("#provider-local").ClickAsync();
            await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToHaveValueAsync("Dieser Server");
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Lokal");
            await page.GetByLabel("Passwort der Sicherung").FillAsync(RepositoryPassword);
            await page.Locator("#backup-save").ClickAsync();
            await ExpectSnackbarAsync(page, "Das Ziel wurde gespeichert.");

            await page.Locator("tr[data-target='Lokal']").GetByRole(AriaRole.Button, new() { Name = "Jetzt sichern" }).DispatchEventAsync("click");
            var snapshots = page.Locator("#backup-snapshots tbody tr");
            await Assertions.Expect(snapshots).ToHaveCountAsync(1, new() { Timeout = 300_000 });
            await Assertions.Expect(page.Locator("#backup-running")).ToHaveCountAsync(0, new() { Timeout = 300_000 });

            // Download: the backup is packed and the page fetches the file by itself, without a message that stays
            var download = await page.RunAndWaitForDownloadAsync(
                async () => await snapshots.First.GetByRole(AriaRole.Button, new() { Name = "Herunterladen", Exact = true }).DispatchEventAsync("click"),
                new() { Timeout = 300_000 });
            Assert.StartsWith("elocaro-sicherung-", download.SuggestedFilename, StringComparison.Ordinal);
            Assert.EndsWith(".tar", download.SuggestedFilename, StringComparison.Ordinal);
            await download.SaveAsAsync(downloaded);
            Assert.True(new FileInfo(downloaded).Length > 1000);
            // Nothing stays behind: the file is removed once it was sent
            await Assertions.Expect(page.Locator("#backup-download")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
            await Assertions.Expect(page.Locator("#backup-running")).ToHaveCountAsync(0);
        });

        // Later the state changed (and the files are gone) ...
        await WriteStateAsync("Geänderte Vermietung");
        await sidecar.ExecAsync("rm", "/data/ab/mietvertrag.txt");
        Assert.Equal("Geänderte Vermietung", await LandlordAsync());

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // ... so restore from the file that was saved earlier
            await GotoInteractiveAsync(page, Page);
            // Nothing to send before a file is chosen; then the drop area shows its name
            await Assertions.Expect(page.Locator("#upload-send")).ToBeDisabledAsync();
            await page.Locator("#backup-upload-form input[type=file]").SetInputFilesAsync(downloaded);
            await Assertions.Expect(page.Locator("#backup-upload-form .dropzone-title")).ToHaveTextAsync("heruntergeladen.tar");
            await Assertions.Expect(page.Locator("#upload-send")).ToBeEnabledAsync();
            await page.Locator("#upload-send").DispatchEventAsync("click");
            await Assertions.Expect(page.Locator("#backup-uploaded")).ToContainTextAsync("Hochgeladen: heruntergeladen.tar", new() { Timeout = 60_000 });
            await Assertions.Expect(page.Locator("#backup-upload-result")).ToContainTextAsync("hochgeladen");
            // Typed values would be lost while the page connects after the redirect
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            // Wrong password: the file stays closed
            var uploaded = page.Locator("#backup-uploaded");
            await uploaded.GetByLabel("Passwort der Sicherung").FillAsync("falsches-passwort");
            await Assertions.Expect(page.Locator("#upload-inspect")).ToBeEnabledAsync();
            await page.Locator("#upload-inspect").DispatchEventAsync("click");
            await Assertions.Expect(page.Locator("#upload-check")).ToContainTextAsync("Passwort", new() { Timeout = Restic });
            await Assertions.Expect(page.Locator("#upload-confirm")).ToHaveCountAsync(0);

            await uploaded.GetByLabel("Passwort der Sicherung").FillAsync(RepositoryPassword);
            await Assertions.Expect(page.Locator("#upload-check")).ToHaveCountAsync(0);
            await page.Locator("#upload-inspect").DispatchEventAsync("click");
            await Assertions.Expect(page.Locator("#upload-confirm")).ToContainTextAsync("Die Datei enthält die Sicherung vom", new() { Timeout = Restic });

            // Only enabled after the confirmation word was typed
            await Assertions.Expect(page.Locator("#upload-restore")).ToBeDisabledAsync();
            await page.Locator("#upload-confirm input").FillAsync("WIEDERHERSTELLEN");
            await Assertions.Expect(page.Locator("#upload-restore")).ToBeEnabledAsync();
            await page.Locator("#upload-restore").DispatchEventAsync("click");

            await Assertions.Expect(page.Locator("#restore-result")).ToContainTextAsync("Die Wiederherstellung ist abgeschlossen", new() { Timeout = 300_000 });
        });

        Assert.Equal(1, restart.Count);
        SimulateRestart();

        Assert.Equal(Original, await LandlordAsync());
        Assert.Equal("Mietvertrag Original", await File.ReadAllTextAsync(StoredFile, Ct));
    }

    [Fact]
    public async Task A_new_installation_restores_an_uploaded_backup_file_in_the_first_setup()
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
        var file = await ExportThroughTheApiAsync();

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

        var page = await NewPageAsync(mobile: false);
        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GotoAsync(BaseUrl + "/konto/einrichtung/wiederherstellen");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await page.Locator("#source-file").ClickAsync();

            // Wrong setup code: the file is not taken
            await FillKeptAsync(page.Locator("#restore-upload-code"), "falscher-code");
            await page.Locator("#restore-upload-file").SetInputFilesAsync(file);
            await page.Locator("#restore-upload-send").ClickAsync();
            await Assertions.Expect(page.Locator("#restore-upload-result")).ToContainTextAsync("Der Einrichtungscode stimmt nicht.", new() { Timeout = 60_000 });
            await Assertions.Expect(page.Locator("#restore-uploaded")).ToHaveCountAsync(0);

            // Right code: uploaded (the page is interactive again after the redirect; typed values would be lost before that)
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await FillKeptAsync(page.Locator("#restore-upload-code"), ElocaroWebFactory.SetupCode);
            await page.Locator("#restore-upload-file").SetInputFilesAsync(file);
            await page.Locator("#restore-upload-send").ClickAsync();
            await Assertions.Expect(page.Locator("#restore-uploaded")).ToContainTextAsync("Hochgeladen:", new() { Timeout = 60_000 });
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            // Wrong password, then the right one
            await FillKeptAsync(page.GetByLabel("Einrichtungscode"), ElocaroWebFactory.SetupCode);
            await FillKeptAsync(page.GetByLabel("Passwort der Sicherung"), "falsches-passwort");
            await page.Locator("#restore-open").ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Passwort", new() { Timeout = Restic });
            await Assertions.Expect(page.Locator("#restore-file-start")).ToHaveCountAsync(0);

            await FillKeptAsync(page.GetByLabel("Passwort der Sicherung"), RepositoryPassword);
            await page.Locator("#restore-open").ClickAsync();
            await Assertions.Expect(page.Locator("#restore-file-ok")).ToContainTextAsync("Die Datei enthält die Sicherung vom", new() { Timeout = Restic });
            await page.Locator("#restore-file-start").ClickAsync();

            await Assertions.Expect(page.Locator("#restore-done")).ToContainTextAsync("Die Wiederherstellung ist abgeschlossen", new() { Timeout = 300_000 });
        });

        Assert.Equal(1, restart.Count);
        SimulateRestart();

        Assert.NotNull(await App.WithScopeAsync(async sp => await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email)));
        Assert.Equal(Original, await LandlordAsync());
        Assert.Equal("Mietvertrag Original", await File.ReadAllTextAsync(StoredFile, Ct));
    }

    /// <summary>Packs the newest backup as a file through the API of the backup service (what the download button does) and returns its path.</summary>
    private async Task<string> ExportThroughTheApiAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var target = new { repository = "/repo", password = RepositoryPassword };
        var snapshots = await http.PostAsJsonAsync(new Uri("snapshots", UriKind.Relative), new { target }, Ct);
        var id = System.Text.Json.JsonElement.Parse(await snapshots.Content.ReadAsStringAsync(Ct))[0].GetProperty("id").GetString();
        using var started = await http.PostAsJsonAsync(new Uri("export", UriKind.Relative), new { target, snapshot = id }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, started.StatusCode);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var status = await http.GetStringAsync(new Uri("status", UriKind.Relative), Ct);
            if (status.Contains("\"running\":false", StringComparison.Ordinal))
            {
                Assert.Contains("\"kind\":\"Export\"", status, StringComparison.Ordinal);
                var path = Path.Combine(work, "export-aus-dienst.tar");
                await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(new Uri("export/file", UriKind.Relative), Ct), Ct);
                return path;
            }

            await Task.Delay(1000, Ct);
        }

        Assert.Fail("Der Export wurde nicht fertig.");
        return string.Empty;
    }

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
