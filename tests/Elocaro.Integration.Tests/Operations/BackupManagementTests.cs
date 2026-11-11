using System.Net;
using System.Text.Json;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Operations;

public class BackupManagementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly FakeBackupSidecar sidecar = new();
    private InfrastructureEnvironment environment = null!;
    private string statusFile = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        statusFile = Path.Combine(Path.GetTempPath(), "elocaro-tests", Guid.NewGuid().ToString("N"), "backup.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!);
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["Backup:Url"] = "http://backup:8080/",
            ["Backup:ApiToken"] = "test-token",
            ["Backup:StatusFile"] = statusFile,
        }, services => services.AddHttpClient<BackupSidecar>().ConfigurePrimaryHttpMessageHandler(() => sidecar));
    }

    public async ValueTask DisposeAsync()
    {
        await environment.DisposeAsync();
        Directory.Delete(Path.GetDirectoryName(statusFile)!, recursive: true);
    }

    private static BackupSettingsInput S3(string? password = "geheim-pw", string? secret = "geheim-key")
    {
        var input = new BackupSettingsInput { Provider = BackupProvider.S3, Location = "https://s3.example.com/bucket/elocaro", Password = password };
        input.Credentials["AWS_ACCESS_KEY_ID"] = "AKIA123";
        if (secret is not null)
        {
            input.Credentials["AWS_SECRET_ACCESS_KEY"] = secret;
        }

        return input;
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<Application.Common.Result> SaveAsync(BackupSettingsInput input) =>
        InScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().SaveAsync(input, Ct));

    private Task<BackupSettingsView?> GetAsync() => InScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().GetAsync(Ct));

    [Fact]
    public async Task Without_settings_the_backup_runs_through_the_environment_of_the_container() =>
        Assert.Null(await GetAsync());

    [Fact]
    public async Task Saved_settings_are_shown_without_secrets()
    {
        Assert.True((await SaveAsync(S3())).Succeeded);

        var view = (await GetAsync())!;

        Assert.Equal(BackupProvider.S3, view.Provider);
        Assert.True(view.HasPassword);
        Assert.Contains("AWS_SECRET_ACCESS_KEY", view.StoredCredentials);
        Assert.Equal(new TimeOnly(2, 30), view.Time);
    }

    [Fact]
    public async Task Password_and_credentials_are_stored_encrypted()
    {
        await SaveAsync(S3());

        var row = await InScopeAsync(async sp => await sp.GetRequiredService<ElocaroDbContext>().Set<BackupSetting>().AsNoTracking().SingleAsync(Ct));

        Assert.DoesNotContain("geheim-pw", row.ProtectedPassword, StringComparison.Ordinal);
        Assert.DoesNotContain("geheim-key", row.ProtectedCredentials, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIA123", row.ProtectedCredentials, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_takes_over_the_schedule_from_the_container()
    {
        await SaveAsync(S3());

        var call = Assert.Single(sidecar.Requests);
        Assert.Equal("/managed", call.Path);
        Assert.Contains("true", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_secrets_keep_the_saved_ones()
    {
        await SaveAsync(S3());
        var change = S3(password: null, secret: null);
        change.Location = "https://s3.example.com/anderer-bucket";

        Assert.True((await SaveAsync(change)).Succeeded);
        sidecar.Script["/backup"] = () => new HttpResponseMessage(HttpStatusCode.Accepted);
        await InScopeAsync(async sp => await sp.GetRequiredService<IBackupControl>().StartBackupAsync(Ct));

        var body = JsonElement.Parse(sidecar.Requests.Last(r => r.Path == "/backup").Body).GetProperty("target");
        Assert.Equal("s3:https://s3.example.com/anderer-bucket", body.GetProperty("repository").GetString());
        Assert.Equal("geheim-pw", body.GetProperty("password").GetString());
        Assert.Equal("geheim-key", body.GetProperty("environment").GetProperty("AWS_SECRET_ACCESS_KEY").GetString());
    }

    [Fact]
    public async Task Switching_the_provider_drops_the_old_credentials()
    {
        await SaveAsync(S3());
        var drive = new BackupSettingsInput { Provider = BackupProvider.GoogleDrive, Location = "Elocaro" };

        var result = await SaveAsync(drive);

        Assert.False(result.Succeeded);
        Assert.Contains("Zugangstoken", string.Join(' ', result.Errors), StringComparison.Ordinal);
        Assert.Equal(BackupProvider.S3, (await GetAsync())!.Provider);
    }

    [Fact]
    public async Task Incomplete_entries_are_rejected_and_nothing_is_saved()
    {
        var result = await SaveAsync(S3(password: null, secret: null));

        Assert.False(result.Succeeded);
        Assert.Null(await GetAsync());
        Assert.Empty(sidecar.Requests);
    }

    [Fact]
    public async Task Test_uses_the_entry_and_reports_the_state_of_the_repository()
    {
        sidecar.Script["/test"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"state":"WrongPassword","message":"Das Repository-Passwort stimmt nicht."}""");

        var check = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().TestAsync(S3(), Ct));

        Assert.Equal(BackupTargetState.WrongPassword, check.State);
        Assert.False(check.Succeeded);
        Assert.Contains("geheim-pw", sidecar.Requests.Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_of_an_incomplete_entry_does_not_call_the_container()
    {
        var check = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().TestAsync(S3(password: null), Ct));

        Assert.Equal(BackupTargetState.Invalid, check.State);
        Assert.Empty(sidecar.Requests);
    }

    [Fact]
    public async Task An_unreachable_container_is_reported_not_thrown()
    {
        await SaveAsync(S3());
        sidecar.Offline = true;
        await using var scope = environment.Services.CreateAsyncScope();
        var control = scope.ServiceProvider.GetRequiredService<IBackupControl>();

        Assert.False((await control.ProgressAsync(Ct)).Available);
        Assert.Equal(BackupTargetState.ServiceUnavailable, (await control.TestAsync(S3(), Ct)).State);
        Assert.False((await control.StartBackupAsync(Ct)).Succeeded);
        Assert.Empty(await control.SnapshotsAsync(Ct));
    }

    [Fact]
    public async Task A_running_operation_is_reported_as_conflict()
    {
        await SaveAsync(S3());
        sidecar.Script["/backup"] = () => FakeBackupSidecar.Json(HttpStatusCode.Conflict, "{}");

        var result = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().StartBackupAsync(Ct));

        Assert.False(result.Succeeded);
        Assert.Contains("läuft bereits", string.Join(' ', result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshots_come_newest_first_from_the_container()
    {
        await SaveAsync(S3());
        sidecar.Script["/snapshots"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """[{"id":"bbbb2222cccc","shortId":"bbbb2222","time":"2026-03-02T01:30:00Z"}]""");

        var rows = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().SnapshotsAsync(Ct));

        Assert.Equal("bbbb2222", Assert.Single(rows).ShortId);
    }

    [Fact]
    public async Task Progress_shows_running_operation_and_last_result()
    {
        sidecar.Script["/status"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK,
            """{"running":true,"kind":"Backup","startedAt":"2026-03-01T01:30:00Z","last":{"kind":"Restore","succeeded":false,"message":"kaputt","snapshot":null,"finishedAt":"2026-02-28T01:30:00Z","durationSeconds":3}}""");

        var progress = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().ProgressAsync(Ct));

        Assert.True(progress.Available);
        Assert.True(progress.Running);
        Assert.Equal("Backup", progress.Kind);
        Assert.False(progress.Last!.Succeeded);
    }

    [Fact]
    public async Task Job_without_settings_does_nothing()
    {
        await RunJobAsync();

        Assert.Empty(sidecar.Requests);
    }

    [Fact]
    public async Task Job_starts_the_backup_once_the_time_has_passed()
    {
        await SaveAsync(S3());
        sidecar.Requests.Clear();
        sidecar.Script["/status"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"running":false}""");
        sidecar.Script["/backup"] = () => new HttpResponseMessage(HttpStatusCode.Accepted);

        // 2026-03-01 09:30 UTC = 10:30 local, no backup yet today
        await RunJobAsync();

        Assert.Equal(["/managed", "/status", "/backup"], sidecar.Paths);
    }

    [Fact]
    public async Task Job_waits_while_an_operation_is_running_and_after_todays_attempt()
    {
        await SaveAsync(S3());
        sidecar.Requests.Clear();
        sidecar.Script["/status"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"running":true,"kind":"Backup"}""");
        await RunJobAsync();
        Assert.DoesNotContain("/backup", sidecar.Paths);

        sidecar.Script["/status"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"running":false}""");
        await File.WriteAllTextAsync(statusFile, """{"timestamp":"2026-03-01T01:31:00Z","succeeded":false,"message":"x","snapshot":"","durationSeconds":1}""", Ct);
        await RunJobAsync();
        Assert.DoesNotContain("/backup", sidecar.Paths);
    }

    private async Task RunJobAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<BackupJob>(scope.ServiceProvider).RunAsync(Ct);
    }
}
