using System.Formats.Tar;
using System.Net.Http.Headers;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Backup.Tests;

/// <summary>A backup as a file, through the client of the app (<see cref="BackupSidecar"/>) against the real API.</summary>
public sealed class BackupFileContractTests : IDisposable
{
    private readonly BackupTestEnvironment environment = new();
    private readonly WebApplicationFactory<Program> factory;

    public BackupFileContractTests() =>
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

    private BackupSidecar Client()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret-token");
        return new BackupSidecar(http, Options.Create(new BackupOptions { Url = "http://localhost/", ApiToken = "secret-token" }));
    }

    private static BackupRepositoryTarget Target() =>
        BackupProviders.Build(BackupProvider.Local, string.Empty, new Dictionary<string, string>(), "pw", new BackupRetention(7, 4, 12));

    private static MemoryStream Tar(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)) });
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task A_backup_is_packed_for_download_fetched_and_discarded()
    {
        // What restic would have copied into the new repository
        environment.Runner.Script["restic copy"] = _ =>
        {
            var repository = Path.Combine(environment.Settings.ExportDir, "export-work", "repo");
            File.WriteAllText(Path.Combine(repository, "config"), "c");
            return new ProcessResult(0, string.Empty);
        };
        var client = Client();

        var started = await client.StartExportAsync(Target(), "bbbbbbbb22222222", "ziel1", Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;

        Assert.True(started.Succeeded, string.Join(' ', started.Errors));
        var status = await client.StatusAsync(Ct);
        Assert.Equal("Export", status!.Last!.Kind);
        Assert.True(status.Last.Succeeded);
        Assert.Equal("elocaro-sicherung-bbbbbbbb.tar", status.Export!.Name);
        Assert.Equal("bbbbbbbb22222222", status.Export.Snapshot);

        await using (var download = await client.OpenExportAsync(Ct))
        {
            Assert.NotNull(download);
            Assert.Equal("elocaro-sicherung-bbbbbbbb.tar", download.FileName);
            using var copy = new MemoryStream();
            await download.Content.CopyToAsync(copy, Ct);
            Assert.True(copy.Length > 0);
        }

        // The file is removed once it was sent: nothing stays behind, a second fetch finds nothing
        Assert.Null((await client.StatusAsync(Ct))!.Export);
        Assert.Null(await client.OpenExportAsync(Ct));
        await client.DiscardExportAsync(Ct);
    }

    [Fact]
    public async Task A_backup_file_is_handed_in_opened_with_the_password_and_restored()
    {
        environment.Runner.Script["restic snapshots"] = _ => new ProcessResult(0,
            "[{\"id\":\"bbbbbbbb22222222\",\"short_id\":\"bbbbbbbb\",\"time\":\"2026-03-02T01:30:00Z\",\"summary\":{\"total_bytes_processed\":2048}}]");
        var client = Client();

        using (var tar = Tar(("config", "c"), ("data/aa/blob", "d")))
        {
            var uploaded = await client.UploadAsync(tar, "elocaro-sicherung-bbbbbbbb.tar", Ct);
            Assert.True(uploaded.Succeeded, string.Join(' ', uploaded.Errors));
        }

        Assert.Equal("elocaro-sicherung-bbbbbbbb.tar", (await client.StatusAsync(Ct))!.Upload!.Name);

        var check = await client.InspectUploadAsync("pw", Ct);
        Assert.True(check.Succeeded, check.Message);
        Assert.Equal(["bbbbbbbb"], check.Snapshots.Select(s => s.ShortId));
        Assert.Equal(2048, check.Snapshots[0].Size);

        environment.Runner.Script["restic cat"] = _ => new ProcessResult(12, "wrong password");
        Assert.False((await client.InspectUploadAsync("falsch", Ct)).Succeeded);
        environment.Runner.Script.Remove("restic cat");

        var restore = await client.StartRestoreUploadAsync("pw", Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;

        Assert.True(restore.Succeeded, string.Join(' ', restore.Errors));
        var status = await client.StatusAsync(Ct);
        Assert.Equal("Restore", status!.Last!.Kind);
        // The restic repository of the restore is the unpacked file (the restore itself is covered by the test with the real container)
        var import = factory.Services.GetRequiredService<BackupTransfers>().ImportDir;
        Assert.Contains(environment.Runner.Calls, c => c.Environment is not null && c.Environment.GetValueOrDefault("RESTIC_REPOSITORY") == import);
    }

    [Fact]
    public async Task A_file_that_is_no_backup_file_is_refused_with_a_message()
    {
        using var rubbish = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x', 4096)));

        var result = await Client().UploadAsync(rubbish, "x.tar", Ct);

        Assert.False(result.Succeeded);
        Assert.Contains("keine Sicherungsdatei", string.Join(' ', result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_upload_there_is_nothing_to_open_or_restore()
    {
        var client = Client();

        Assert.False((await client.InspectUploadAsync("pw", Ct)).Succeeded);
        Assert.False((await client.StartRestoreUploadAsync("pw", Ct)).Succeeded);
    }

    [Fact]
    public async Task An_upload_is_not_accepted_while_a_backup_runs()
    {
        var release = new TaskCompletionSource();
        environment.Runner.Script["restic cat"] = _ =>
        {
            release.Task.GetAwaiter().GetResult();
            return new ProcessResult(0, string.Empty);
        };
        var client = Client();
        Assert.True((await client.StartBackupAsync(Target(), "ziel1", Ct)).Succeeded);

        using var tar = Tar(("config", "c"));
        var result = await client.UploadAsync(tar, "a.tar", Ct);

        release.SetResult();
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;
        Assert.False(result.Succeeded);
        Assert.Contains("läuft bereits", string.Join(' ', result.Errors), StringComparison.Ordinal);
    }
}
