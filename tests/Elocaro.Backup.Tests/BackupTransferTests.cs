using System.Formats.Tar;
using Elocaro.Backup;

namespace Elocaro.Backup.Tests;

/// <summary>A backup as a file: packed for download, handed in again, read and restored.</summary>
public sealed class BackupTransferTests : IDisposable
{
    private readonly BackupTestEnvironment environment = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => environment.Dispose();

    private BackupTransfers Transfers() => new(environment.Settings, TimeProvider.System);

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
    public async Task Export_copies_one_snapshot_into_a_new_repository_with_the_same_password_and_packs_it()
    {
        var transfers = Transfers();

        await environment.Engine.ExportAsync(BackupTestEnvironment.Target, "bbbbbbbb22222222", transfers.ExportFile, transfers.ExportWorkDir, Ct);

        Assert.Equal(["restic cat", "restic init", "restic copy"], environment.Runner.Commands);
        var init = environment.Runner.Calls.Single(c => c.Arguments[0] == "init");
        Assert.Contains("--copy-chunker-params", init.Arguments);
        // New repository in the work directory, the old one as the source; one password for both
        Assert.StartsWith(transfers.ExportWorkDir, init.Environment!["RESTIC_REPOSITORY"], StringComparison.Ordinal);
        Assert.Equal("/repo", init.Environment["RESTIC_FROM_REPOSITORY"]);
        Assert.Equal("pw", init.Environment["RESTIC_PASSWORD"]);
        Assert.Equal("pw", init.Environment["RESTIC_FROM_PASSWORD"]);
        Assert.Equal(["copy", "bbbbbbbb22222222"], environment.Runner.Calls.Single(c => c.Arguments[0] == "copy").Arguments);
        Assert.True(File.Exists(transfers.ExportFile));
        Assert.False(Directory.Exists(transfers.ExportWorkDir));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("--from-repo")]
    [InlineData("xyz; rm -rf /")]
    public async Task Export_refuses_anything_but_a_hexadecimal_id(string snapshot)
    {
        var transfers = Transfers();

        await Assert.ThrowsAsync<BackupException>(() => environment.Engine.ExportAsync(BackupTestEnvironment.Target, snapshot, transfers.ExportFile, transfers.ExportWorkDir, Ct));

        Assert.Empty(environment.Runner.Calls);
    }

    [Fact]
    public async Task Export_of_a_missing_repository_is_an_error()
    {
        environment.Runner.Script["restic cat"] = _ => new ProcessResult(10, string.Empty);
        var transfers = Transfers();

        await Assert.ThrowsAsync<BackupException>(() => environment.Engine.ExportAsync(BackupTestEnvironment.Target, "bbbbbbbb22222222", transfers.ExportFile, transfers.ExportWorkDir, Ct));

        Assert.False(File.Exists(transfers.ExportFile));
    }

    [Fact]
    public async Task An_upload_is_unpacked_into_the_import_directory_and_waits_for_the_restore()
    {
        var transfers = Transfers();
        using var tar = Tar(("config", "c"), ("data/aa/blob", "d"));

        var file = await transfers.SaveUploadAsync(tar, "elocaro-sicherung-bbbbbbbb.tar", Ct);

        Assert.Equal("elocaro-sicherung-bbbbbbbb.tar", file.Name);
        Assert.Equal(2, file.Size);
        Assert.True(File.Exists(Path.Combine(transfers.ImportDir, "config")));
        Assert.True(File.Exists(Path.Combine(transfers.ImportDir, "data", "aa", "blob")));
        Assert.Equal(file.Name, transfers.Upload()!.Name);

        transfers.DiscardUpload();
        Assert.Null(transfers.Upload());
        Assert.False(Directory.Exists(transfers.ImportDir));
    }

    [Fact]
    public async Task A_second_upload_replaces_the_first()
    {
        var transfers = Transfers();
        using (var first = Tar(("old", "1")))
        {
            await transfers.SaveUploadAsync(first, "a.tar", Ct);
        }

        using var second = Tar(("new", "2"));
        await transfers.SaveUploadAsync(second, "b.tar", Ct);

        Assert.False(File.Exists(Path.Combine(transfers.ImportDir, "old")));
        Assert.True(File.Exists(Path.Combine(transfers.ImportDir, "new")));
        Assert.Equal("b.tar", transfers.Upload()!.Name);
    }

    [Fact]
    public async Task A_file_that_is_no_backup_file_is_refused_and_leaves_nothing_behind()
    {
        var transfers = Transfers();
        using var rubbish = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x', 4096)));

        var error = await Assert.ThrowsAsync<BackupException>(() => transfers.SaveUploadAsync(rubbish, "x.tar", Ct));

        Assert.Contains("keine Sicherungsdatei", error.Message, StringComparison.Ordinal);
        Assert.Null(transfers.Upload());
        Assert.False(Directory.Exists(transfers.ImportDir));
    }

    [Fact]
    public async Task An_entry_that_leaves_the_directory_is_refused()
    {
        var transfers = Transfers();
        using var tar = Tar(("../evil.txt", "boom"));

        await Assert.ThrowsAsync<BackupException>(() => transfers.SaveUploadAsync(tar, "evil.tar", Ct));

        Assert.False(File.Exists(Path.Combine(environment.Root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(transfers.ImportDir)!, "evil.txt")));
        Assert.Null(transfers.Upload());
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\x\\sicherung.tar", "sicherung.tar")]
    [InlineData("", "sicherung.tar")]
    public async Task The_name_of_an_upload_is_only_a_file_name(string given, string expected)
    {
        var transfers = Transfers();
        using var tar = Tar(("config", "c"));

        var file = await transfers.SaveUploadAsync(tar, given, Ct);

        Assert.Equal(expected, file.Name);
    }

    [Fact]
    public async Task Inspect_reports_a_wrong_password_and_lists_the_backup_otherwise()
    {
        environment.Runner.Script["restic cat"] = args => new ProcessResult(0, string.Empty);
        environment.Runner.Script["restic snapshots"] = _ => new ProcessResult(0,
            "[{\"id\":\"bbbbbbbb22222222\",\"short_id\":\"bbbbbbbb\",\"time\":\"2026-03-02T01:30:00Z\"}]");

        var (check, snapshots) = await environment.Engine.InspectAsync("/import", "pw", Ct);

        Assert.Equal(RepositoryState.Ready, check.State);
        Assert.Equal(["bbbbbbbb"], snapshots.Select(s => s.ShortId));

        environment.Runner.Script["restic cat"] = _ => new ProcessResult(12, "wrong password");
        var (wrong, none) = await environment.Engine.InspectAsync("/import", "falsch", Ct);

        Assert.Equal(RepositoryState.WrongPassword, wrong.State);
        Assert.Empty(none);
    }
}
