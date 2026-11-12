using System.Net.Http.Headers;
using Elocaro.Infrastructure.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Backup.Tests;

/// <summary>The target of the container environment (<c>RESTIC_REPOSITORY</c>): the app shows it, lists its backups and restores from it.</summary>
public sealed class EnvironmentTargetContractTests : IDisposable
{
    private readonly BackupTestEnvironment environment = new();
    private readonly List<WebApplicationFactory<Program>> factories = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var factory in factories)
        {
            factory.Dispose();
        }

        environment.Dispose();
    }

    private (BackupSidecar Client, WebApplicationFactory<Program> Factory) Start(BackupTarget? target)
    {
        var settings = environment.Settings with { EnvironmentTarget = target };
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(settings);
            services.AddSingleton<IProcessRunner>(environment.Runner);
        }));
        factories.Add(factory);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret-token");
        return (new BackupSidecar(http, Options.Create(new BackupOptions { Url = "http://localhost/", ApiToken = "secret-token" })), factory);
    }

    private static BackupTarget Azure() => new("azure:elocaro-backup:/", "pw", null, new RetentionPolicy(7, 4, 12));

    [Fact]
    public async Task Without_a_repository_in_the_environment_there_is_no_target()
    {
        var (client, _) = Start(null);

        Assert.Null(await client.EnvironmentAsync(Ct));
        Assert.False((await client.StartEnvironmentBackupAsync(Ct)).Succeeded);
    }

    [Fact]
    public async Task The_environment_target_shows_where_the_backups_lie_and_when_they_are_made()
    {
        var (client, _) = Start(Azure());

        var info = await client.EnvironmentAsync(Ct);

        Assert.NotNull(info);
        Assert.Equal("azure:elocaro-backup:/", info.Repository);
        Assert.Equal("02:30", info.Time);
        Assert.Equal((7, 4, 12), (info.KeepDaily, info.KeepWeekly, info.KeepMonthly));
        Assert.False(info.Paused);
    }

    [Fact]
    public async Task A_password_in_the_repository_address_is_not_shown()
    {
        var (client, _) = Start(new BackupTarget("rest:https://benutzer:geheim@host.example/repo", "pw", null, new RetentionPolicy()));

        var info = await client.EnvironmentAsync(Ct);

        Assert.Equal("rest:https://host.example/repo", info!.Repository);
        Assert.DoesNotContain("geheim", info.Repository, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_environment_target_lists_its_backups()
    {
        environment.Runner.Script["restic snapshots"] = _ => new ProcessResult(0,
            "[{\"id\":\"bbbbbbbb22222222\",\"short_id\":\"bbbbbbbb\",\"time\":\"2026-03-02T01:30:00Z\"},{\"id\":\"aaaaaaaa11111111\",\"short_id\":\"aaaaaaaa\",\"time\":\"2026-03-01T01:30:00Z\"}]");
        var (client, _) = Start(Azure());

        var (rows, error) = await client.EnvironmentSnapshotsAsync(Ct);

        Assert.Null(error);
        Assert.Equal(["bbbbbbbb", "aaaaaaaa"], rows.Select(r => r.ShortId));
    }

    [Fact]
    public async Task The_environment_target_can_be_backed_up_and_restored_from_the_app()
    {
        environment.Runner.Script["restic backup"] = _ => new ProcessResult(0, "{\"message_type\":\"summary\",\"snapshot_id\":\"49ccbcd74357a447\"}");
        var (client, factory) = Start(Azure());

        var backup = await client.StartEnvironmentBackupAsync(Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;
        Assert.True(backup.Succeeded, string.Join(' ', backup.Errors));
        Assert.True((await client.StatusAsync(Ct))!.Last!.Succeeded);

        var restore = await client.StartEnvironmentRestoreAsync("49ccbcd74357a447", Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;
        Assert.True(restore.Succeeded, string.Join(' ', restore.Errors));
    }

    [Fact]
    public async Task A_backup_of_the_environment_target_can_be_deleted_from_the_app()
    {
        var (client, factory) = Start(Azure());

        var deleted = await client.StartEnvironmentDeleteAsync("49ccbcd74357a447", Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;

        Assert.True(deleted.Succeeded, string.Join(' ', deleted.Errors));
        Assert.Contains("restic forget", environment.Runner.Commands);
        Assert.Equal("Delete", (await client.StatusAsync(Ct))!.Last!.Kind);
    }

    [Fact]
    public async Task Deleting_a_backup_without_a_target_in_the_environment_is_refused()
    {
        var (client, _) = Start(null);

        Assert.False((await client.StartEnvironmentDeleteAsync("49ccbcd74357a447", Ct)).Succeeded);
    }
}
