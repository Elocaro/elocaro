using System.Net.Http.Headers;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Backup.Tests;

/// <summary>
/// The real backup API against the client of the app (<see cref="BackupSidecar"/>): the JSON that the server writes must be what the
/// client reads, and every target that <see cref="BackupProviders"/> builds must pass the validation of the server.
/// </summary>
public sealed class SidecarContractTests : IDisposable
{
    private readonly BackupTestEnvironment environment = new();
    private readonly WebApplicationFactory<Program> factory;

    public SidecarContractTests() =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(environment.Settings);
            services.AddSingleton<IProcessRunner>(environment.Runner);
        }));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        factory.Dispose();
        environment.Dispose();
    }

    private BackupSidecar Client(string token = "secret-token")
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new BackupSidecar(http, Options.Create(new BackupOptions { Url = "http://localhost/", ApiToken = token }));
    }

    private static BackupRepositoryTarget Target(BackupProvider provider)
    {
        var credentials = BackupProviders.Fields(provider).ToDictionary(f => f.Key, _ => "wert", StringComparer.Ordinal);
        var location = provider == BackupProvider.Local ? string.Empty : "ziel";
        return BackupProviders.Build(provider, location, credentials, "pw", new BackupRetention(7, 4, 12));
    }

    [Theory]
    [InlineData(BackupProvider.Local)]
    [InlineData(BackupProvider.S3)]
    [InlineData(BackupProvider.Azure)]
    [InlineData(BackupProvider.Sftp)]
    [InlineData(BackupProvider.GoogleDrive)]
    public async Task Every_target_that_the_providers_build_is_accepted_by_the_server(BackupProvider provider)
    {
        var result = await Client().StartBackupAsync(Target(provider), Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;

        Assert.True(result.Succeeded, string.Join(' ', result.Errors));
    }

    [Fact]
    public async Task Status_reports_an_idle_service_and_then_the_result_of_the_backup()
    {
        var client = Client();
        environment.Runner.Script["restic backup"] = _ => new ProcessResult(0, "{\"message_type\":\"summary\",\"snapshot_id\":\"49ccbcd74357a447\"}");

        var idle = await client.StatusAsync(Ct);
        Assert.False(idle!.Running);
        Assert.Null(idle.Last);

        await client.StartBackupAsync(Target(BackupProvider.Local), Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;

        var done = await client.StatusAsync(Ct);
        Assert.Equal("Backup", done!.Last!.Kind);
        Assert.True(done.Last.Succeeded);
        Assert.Equal("49ccbcd74357a447", done.Last.Snapshot);
    }

    [Theory]
    [InlineData(0, BackupTargetState.Ready)]
    [InlineData(10, BackupTargetState.New)]
    [InlineData(12, BackupTargetState.WrongPassword)]
    [InlineData(1, BackupTargetState.Unreachable)]
    public async Task Test_states_travel_as_the_client_expects(int exitCode, BackupTargetState expected)
    {
        environment.Runner.Script["restic cat"] = _ => new ProcessResult(exitCode, "nicht erreichbar");

        var check = await Client().TestAsync(Target(BackupProvider.S3), Ct);

        Assert.Equal(expected, check.State);
    }

    [Fact]
    public async Task Snapshots_travel_with_id_short_id_and_time()
    {
        environment.Runner.Script["restic snapshots"] = _ => new ProcessResult(0,
            "[{\"id\":\"bbbbbbbb22222222\",\"short_id\":\"bbbbbbbb\",\"time\":\"2026-03-02T01:30:00Z\"},{\"id\":\"aaaaaaaa11111111\",\"short_id\":\"aaaaaaaa\",\"time\":\"2026-03-01T01:30:00Z\"}]");

        var (rows, error) = await Client().SnapshotsAsync(Target(BackupProvider.S3), Ct);

        Assert.Null(error);
        Assert.Equal(["bbbbbbbb22222222", "aaaaaaaa11111111"], rows.Select(r => r.Id));
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 1, 30, 0, TimeSpan.Zero), rows[0].Time);
    }

    [Fact]
    public async Task A_restore_runs_and_a_second_request_meanwhile_is_a_conflict()
    {
        var release = new TaskCompletionSource();
        environment.Runner.Script["restic cat"] = _ =>
        {
            release.Task.GetAwaiter().GetResult();
            return new ProcessResult(0, string.Empty);
        };
        var client = Client();

        Assert.True((await client.StartRestoreAsync(Target(BackupProvider.Local), "latest", Ct)).Succeeded);
        var second = await client.StartBackupAsync(Target(BackupProvider.Local), Ct);
        release.SetResult();
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;

        Assert.False(second.Succeeded);
        Assert.Contains("läuft bereits", string.Join(' ', second.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Taking_over_the_schedule_sets_the_marker()
    {
        Assert.True(await Client().SetManagedAsync(managed: true, Ct));

        Assert.True(File.Exists(environment.Settings.ManagedMarker));
    }

    [Fact]
    public async Task A_wrong_token_is_reported_as_unavailable_service()
    {
        var client = Client("falsch");

        Assert.Null(await client.StatusAsync(Ct));
        Assert.Equal(BackupTargetState.ServiceUnavailable, (await client.TestAsync(Target(BackupProvider.Local), Ct)).State);
        Assert.False((await client.StartBackupAsync(Target(BackupProvider.Local), Ct)).Succeeded);
        Assert.False(await client.SetManagedAsync(managed: true, Ct));
        Assert.Empty(environment.Runner.Calls);
    }
}
