using System.Net;
using System.Text.Json;
using Elocaro.Application.Common;
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

    private static BackupSettingsInput S3(string name = "Hetzner", string? password = "geheim-pw", string? secret = "geheim-key")
    {
        var input = new BackupSettingsInput { Name = name, Provider = BackupProvider.S3, Location = "https://s3.example.com/bucket/elocaro", Password = password };
        input.Credentials["AWS_ACCESS_KEY_ID"] = "AKIA123";
        if (secret is not null)
        {
            input.Credentials["AWS_SECRET_ACCESS_KEY"] = secret;
        }

        return input;
    }

    private static BackupSettingsInput Local(string name = "NAS") => new() { Name = name, Provider = BackupProvider.Local, Password = "lokal-pw" };

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<Result> SaveAsync(BackupSettingsInput input, Guid? id = null) =>
        InScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().SaveAsync(id, input, Ct));

    private async Task<Guid> AddAsync(BackupSettingsInput input)
    {
        var result = await SaveAsync(input);
        Assert.True(result.Succeeded, string.Join(' ', result.Errors));
        return result.Id!.Value;
    }

    private Task<IReadOnlyList<BackupSettingsView>> ListAsync() => InScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().ListAsync(Ct));

    private async Task WriteStatusAsync(Guid id, bool succeeded, DateTimeOffset time) =>
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(statusFile)!, $"backup-{id:N}.json"),
            $$"""{"timestamp":"{{time:yyyy-MM-ddTHH:mm:ssZ}}","succeeded":{{(succeeded ? "true" : "false")}},"message":"x","snapshot":"abc","durationSeconds":3}""", Ct);

    [Fact]
    public async Task Without_targets_the_backup_runs_through_the_environment_of_the_container() =>
        Assert.Empty(await ListAsync());

    [Fact]
    public async Task A_saved_target_is_shown_without_secrets()
    {
        var id = await AddAsync(S3());

        var view = Assert.Single(await ListAsync());

        Assert.Equal(id, view.Id);
        Assert.Equal("Hetzner", view.Name);
        Assert.Equal(BackupProvider.S3, view.Provider);
        Assert.True(view.HasPassword);
        Assert.Contains("AWS_SECRET_ACCESS_KEY", view.StoredCredentials);
        Assert.Equal(new TimeOnly(2, 30), view.Time);
        Assert.Equal(1, view.IntervalDays);
    }

    [Fact]
    public async Task Password_and_credentials_are_stored_encrypted()
    {
        await AddAsync(S3());

        var row = await InScopeAsync(async sp => await sp.GetRequiredService<ElocaroDbContext>().Set<BackupSetting>().AsNoTracking().SingleAsync(Ct));

        Assert.DoesNotContain("geheim-pw", row.ProtectedPassword, StringComparison.Ordinal);
        Assert.DoesNotContain("geheim-key", row.ProtectedCredentials, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIA123", row.ProtectedCredentials, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Several_targets_exist_side_by_side_each_with_its_own_schedule()
    {
        var drive = S3("Drive");
        drive.IntervalDays = 1;
        var nas = Local("NAS");
        nas.IntervalDays = 7;
        nas.Time = new TimeOnly(4, 0);
        nas.Weekday = DayOfWeek.Friday;
        drive.Weekday = DayOfWeek.Tuesday;
        await AddAsync(drive);
        await AddAsync(nas);

        var list = await ListAsync();

        Assert.Equal(["Drive", "NAS"], list.Select(t => t.Name));
        Assert.Equal([1, 7], list.Select(t => t.IntervalDays));
        Assert.Equal(new TimeOnly(4, 0), list[1].Time);
        // The weekday belongs to weekly targets only
        Assert.Null(list[0].Weekday);
        Assert.Equal(DayOfWeek.Friday, list[1].Weekday);
    }

    [Fact]
    public async Task Names_are_required_and_unique_but_a_target_may_keep_its_own()
    {
        var id = await AddAsync(S3("Drive"));

        Assert.False((await SaveAsync(Local(" "))).Succeeded);
        var duplicate = await SaveAsync(Local("drive"));
        Assert.False(duplicate.Succeeded);
        Assert.Contains("bereits ein Ziel", string.Join(' ', duplicate.Errors), StringComparison.Ordinal);

        var again = S3("Drive", password: null, secret: null);
        again.Location = "https://s3.example.com/bucket/neu";
        Assert.True((await SaveAsync(again, id)).Succeeded);
        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task Saving_takes_over_the_schedule_from_the_container()
    {
        await AddAsync(S3());

        var call = Assert.Single(sidecar.Requests);
        Assert.Equal("/managed", call.Path);
        Assert.Contains("true", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_the_last_target_gives_the_schedule_back_to_the_container()
    {
        var first = await AddAsync(S3("Drive"));
        var second = await AddAsync(Local("NAS"));
        sidecar.Requests.Clear();

        Assert.True((await InScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().DeleteAsync(first, Ct))).Succeeded);
        Assert.Empty(sidecar.Requests);
        Assert.Equal("NAS", Assert.Single(await ListAsync()).Name);

        Assert.True((await InScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().DeleteAsync(second, Ct))).Succeeded);
        var call = Assert.Single(sidecar.Requests);
        Assert.Equal("/managed", call.Path);
        Assert.Contains("false", call.Body, StringComparison.Ordinal);
        Assert.Empty(await ListAsync());
        Assert.False((await InScopeAsync(sp => sp.GetRequiredService<IBackupSettings>().DeleteAsync(second, Ct))).Succeeded);
    }

    [Fact]
    public async Task Empty_secrets_keep_the_saved_ones()
    {
        var id = await AddAsync(S3());
        var change = S3(password: null, secret: null);
        change.Location = "https://s3.example.com/anderer-bucket";

        Assert.True((await SaveAsync(change, id)).Succeeded);
        sidecar.Script["/backup"] = () => new HttpResponseMessage(HttpStatusCode.Accepted);
        await InScopeAsync(async sp => await sp.GetRequiredService<IBackupControl>().StartBackupAsync(id, Ct));

        var request = JsonElement.Parse(sidecar.Requests.Last(r => r.Path == "/backup").Body);
        var body = request.GetProperty("target");
        Assert.Equal("s3:https://s3.example.com/anderer-bucket", body.GetProperty("repository").GetString());
        Assert.Equal("geheim-pw", body.GetProperty("password").GetString());
        Assert.Equal("geheim-key", body.GetProperty("environment").GetProperty("AWS_SECRET_ACCESS_KEY").GetString());
        // The container keeps one result and status file per target
        Assert.Equal(id.ToString("N"), request.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task Switching_the_provider_drops_the_old_credentials()
    {
        var id = await AddAsync(S3());
        var drive = new BackupSettingsInput { Name = "Hetzner", Provider = BackupProvider.GoogleDrive, Location = "Elocaro" };

        var result = await SaveAsync(drive, id);

        Assert.False(result.Succeeded);
        Assert.Contains("Anmeldung bei Google", string.Join(' ', result.Errors), StringComparison.Ordinal);
        Assert.Equal(BackupProvider.S3, Assert.Single(await ListAsync()).Provider);
    }

    [Fact]
    public async Task Incomplete_entries_are_rejected_and_nothing_is_saved()
    {
        var result = await SaveAsync(S3(password: null, secret: null));

        Assert.False(result.Succeeded);
        Assert.Empty(await ListAsync());
        Assert.Empty(sidecar.Requests);
    }

    [Fact]
    public async Task Test_uses_the_entry_and_reports_the_state_of_the_repository()
    {
        sidecar.Script["/test"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"state":"WrongPassword","message":"Das Repository-Passwort stimmt nicht."}""");

        var check = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().TestAsync(null, S3(), Ct));

        Assert.Equal(BackupTargetState.WrongPassword, check.State);
        Assert.False(check.Succeeded);
        Assert.Contains("geheim-pw", sidecar.Requests.Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_of_a_saved_target_fills_empty_secrets_from_it()
    {
        var id = await AddAsync(S3());
        sidecar.Requests.Clear();
        sidecar.Script["/test"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"state":"Ready","message":"ok"}""");

        var check = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().TestAsync(id, S3(password: null, secret: null), Ct));

        Assert.Equal(BackupTargetState.Ready, check.State);
        Assert.Contains("geheim-key", sidecar.Requests.Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_of_an_incomplete_entry_does_not_call_the_container()
    {
        var check = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().TestAsync(null, S3(password: null), Ct));

        Assert.Equal(BackupTargetState.Invalid, check.State);
        Assert.Empty(sidecar.Requests);
    }

    [Fact]
    public async Task An_unreachable_container_is_reported_not_thrown()
    {
        var id = await AddAsync(S3());
        sidecar.Offline = true;
        await using var scope = environment.Services.CreateAsyncScope();
        var control = scope.ServiceProvider.GetRequiredService<IBackupControl>();

        Assert.False((await control.ProgressAsync(Ct)).Available);
        Assert.Equal(BackupTargetState.ServiceUnavailable, (await control.TestAsync(null, S3(), Ct)).State);
        Assert.False((await control.StartBackupAsync(id, Ct)).Succeeded);
        Assert.Empty(await control.SnapshotsAsync(id, Ct));
    }

    [Fact]
    public async Task A_running_operation_is_reported_as_conflict()
    {
        var id = await AddAsync(S3());
        sidecar.Script["/backup"] = () => FakeBackupSidecar.Json(HttpStatusCode.Conflict, "{}");

        var result = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().StartBackupAsync(id, Ct));

        Assert.False(result.Succeeded);
        Assert.Contains("läuft bereits", string.Join(' ', result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starting_a_backup_of_a_target_that_does_not_exist_is_an_error_without_a_call()
    {
        var result = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().StartBackupAsync(Guid.NewGuid(), Ct));

        Assert.False(result.Succeeded);
        Assert.Empty(sidecar.Requests);
    }

    [Fact]
    public async Task Snapshots_come_newest_first_from_the_container()
    {
        var id = await AddAsync(S3());
        sidecar.Script["/snapshots"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """[{"id":"bbbb2222cccc","shortId":"bbbb2222","time":"2026-03-02T01:30:00Z"}]""");

        var rows = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().SnapshotsAsync(id, Ct));

        Assert.Equal("bbbb2222", Assert.Single(rows).ShortId);
    }

    [Fact]
    public async Task Progress_shows_the_running_target_and_the_last_result_per_target()
    {
        sidecar.Script["/status"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK,
            """{"running":true,"kind":"Backup","targetId":"aaa","startedAt":"2026-03-01T01:30:00Z","last":{"kind":"Restore","succeeded":false,"message":"kaputt","snapshot":null,"finishedAt":"2026-02-28T01:30:00Z","durationSeconds":3},"results":{"aaa":{"kind":"Backup","succeeded":true,"message":"ok","snapshot":"x","finishedAt":"2026-02-28T01:00:00Z","durationSeconds":2,"targetId":"aaa"}}}""");

        var progress = await InScopeAsync(sp => sp.GetRequiredService<IBackupControl>().ProgressAsync(Ct));

        Assert.True(progress.Available);
        Assert.True(progress.Running);
        Assert.Equal("aaa", progress.RunningTargetId);
        Assert.False(progress.Last!.Succeeded);
        Assert.True(progress.Results!["aaa"].Succeeded);
    }

    // ---- The job ----

    private void Idle() => sidecar.Script["/status"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"running":false}""");

    private async Task RunJobAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<BackupJob>(scope.ServiceProvider).RunAsync(Ct);
    }

    [Fact]
    public async Task Job_without_targets_does_nothing()
    {
        await RunJobAsync();

        Assert.Empty(sidecar.Requests);
    }

    [Fact]
    public async Task Job_starts_a_due_target_one_at_a_time()
    {
        var drive = await AddAsync(S3("Drive"));
        await AddAsync(Local("NAS"));
        sidecar.Requests.Clear();
        Idle();
        sidecar.Script["/backup"] = () => new HttpResponseMessage(HttpStatusCode.Accepted);

        // 2026-03-01 09:30 UTC = 10:30 local: both are due; only the first is started, the container takes one at a time
        await RunJobAsync();

        Assert.Equal(["/managed", "/status", "/backup"], sidecar.Paths);
        Assert.Contains(drive.ToString("N"), sidecar.Requests.Last().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_skips_targets_that_are_not_due_or_switched_off()
    {
        var drive = await AddAsync(S3("Drive"));
        var nas = Local("NAS");
        nas.Enabled = false;
        await AddAsync(nas);
        sidecar.Requests.Clear();
        Idle();
        sidecar.Script["/backup"] = () => new HttpResponseMessage(HttpStatusCode.Accepted);
        // Drive was tried this morning (failed or not), the NAS is switched off
        await WriteStatusAsync(drive, succeeded: false, new DateTimeOffset(2026, 3, 1, 1, 31, 0, TimeSpan.Zero));

        await RunJobAsync();

        Assert.DoesNotContain("/backup", sidecar.Paths);
    }

    [Fact]
    public async Task Job_starts_the_second_target_when_the_first_is_done()
    {
        var drive = await AddAsync(S3("Drive"));
        var nas = await AddAsync(Local("NAS"));
        sidecar.Requests.Clear();
        Idle();
        sidecar.Script["/backup"] = () => new HttpResponseMessage(HttpStatusCode.Accepted);
        await WriteStatusAsync(drive, succeeded: true, new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));

        await RunJobAsync();

        Assert.Contains(nas.ToString("N"), sidecar.Requests.Last(r => r.Path == "/backup").Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_waits_while_an_operation_is_running()
    {
        await AddAsync(S3());
        sidecar.Requests.Clear();
        sidecar.Script["/status"] = () => FakeBackupSidecar.Json(HttpStatusCode.OK, """{"running":true,"kind":"Backup"}""");

        await RunJobAsync();

        Assert.DoesNotContain("/backup", sidecar.Paths);
    }

    // ---- Monitoring of several targets ----

    private async Task<BackupSnapshot> StateAsync() =>
        await InScopeAsync(sp => sp.GetRequiredService<IBackupStatus>().QueryAsync(Ct));

    [Fact]
    public async Task The_overall_state_names_the_worst_target()
    {
        var drive = await AddAsync(S3("Drive"));
        var nas = await AddAsync(Local("NAS"));
        await WriteStatusAsync(drive, succeeded: true, InfrastructureEnvironment.StartTime.AddHours(-3));
        await WriteStatusAsync(nas, succeeded: false, InfrastructureEnvironment.StartTime.AddHours(-1));

        var state = await StateAsync();

        Assert.Equal(BackupState.Failed, state.State);
        Assert.StartsWith("Ziel „NAS“: ", state.Description, StringComparison.Ordinal);
        Assert.True(state.NeedsAttention);
    }

    [Fact]
    public async Task The_overall_state_is_ok_when_every_enabled_target_is_current()
    {
        var drive = await AddAsync(S3("Drive"));
        var nas = await AddAsync(Local("NAS"));
        await WriteStatusAsync(drive, succeeded: true, InfrastructureEnvironment.StartTime.AddHours(-3));
        await WriteStatusAsync(nas, succeeded: true, InfrastructureEnvironment.StartTime.AddHours(-5));

        var state = await StateAsync();

        Assert.Equal(BackupState.Ok, state.State);
        Assert.Equal("Alle 2 Sicherungsziele sind aktuell.", state.Description);
    }

    [Fact]
    public async Task A_weekly_target_is_not_late_after_two_days_but_a_daily_one_is()
    {
        var daily = await AddAsync(S3("Täglich"));
        var weekly = Local("Wöchentlich");
        weekly.IntervalDays = 7;
        var weeklyId = await AddAsync(weekly);
        await WriteStatusAsync(daily, succeeded: true, InfrastructureEnvironment.StartTime.AddDays(-2));
        await WriteStatusAsync(weeklyId, succeeded: true, InfrastructureEnvironment.StartTime.AddDays(-2));

        var targets = await InScopeAsync(sp => sp.GetRequiredService<IBackupStatus>().QueryTargetsAsync(Ct));

        Assert.Equal(BackupState.Outdated, targets.Single(t => t.Name == "Täglich").Status.State);
        Assert.Equal(BackupState.Ok, targets.Single(t => t.Name == "Wöchentlich").Status.State);
    }

    [Fact]
    public async Task A_target_without_a_status_file_has_no_backup_yet_and_switched_off_targets_are_ignored()
    {
        var drive = await AddAsync(S3("Drive"));
        var nas = Local("NAS");
        nas.Enabled = false;
        await AddAsync(nas);
        await WriteStatusAsync(drive, succeeded: true, InfrastructureEnvironment.StartTime.AddHours(-1));

        Assert.Equal(BackupState.Ok, (await StateAsync()).State);

        var other = await AddAsync(Local("Neu"));
        var state = await StateAsync();
        Assert.Equal(BackupState.Unknown, state.State);
        Assert.Contains("Neu", state.Description, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, other);
    }

    [Fact]
    public async Task When_every_target_is_switched_off_nothing_is_backed_up_and_that_is_reported()
    {
        var off = Local("NAS");
        off.Enabled = false;
        await AddAsync(off);

        var state = await StateAsync();

        Assert.Equal(BackupState.Unknown, state.State);
        Assert.True(state.NeedsAttention);
    }
}
