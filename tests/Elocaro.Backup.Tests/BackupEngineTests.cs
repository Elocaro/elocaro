using Elocaro.Backup;

namespace Elocaro.Backup.Tests;

public sealed class BackupEngineTests : IDisposable
{
    private readonly BackupTestEnvironment environment = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => environment.Dispose();

    private void ScriptSummary() =>
        environment.Runner.Script["restic backup"] = _ => new ProcessResult(0, "{\"message_type\":\"status\"}\n{\"message_type\":\"summary\",\"snapshot_id\":\"49ccbcd74357a447\"}\n");

    [Fact]
    public async Task Backup_dumps_saves_prunes_and_checks_in_that_order()
    {
        ScriptSummary();

        var snapshot = await environment.Engine.BackupAsync(BackupTestEnvironment.Target, Ct);

        Assert.Equal("49ccbcd74357a447", snapshot);
        Assert.Equal(["restic cat", "pg_dump --format=custom", "restic backup", "restic forget", "restic check"], environment.Runner.Commands);
        Assert.False(Directory.Exists(environment.Settings.WorkDir));
    }

    [Fact]
    public async Task Backup_passes_the_target_to_restic_and_the_retention_to_forget()
    {
        ScriptSummary();

        await environment.Engine.BackupAsync(BackupTestEnvironment.Target with { Retention = new RetentionPolicy(3, 2, 1) }, Ct);

        var forget = environment.Runner.Calls.Single(c => c.Arguments[0] == "forget");
        Assert.Equal("/repo", forget.Environment!["RESTIC_REPOSITORY"]);
        Assert.Equal("pw", forget.Environment["RESTIC_PASSWORD"]);
        Assert.Equal("key", forget.Environment["AWS_ACCESS_KEY_ID"]);
        Assert.Equal(["--keep-daily", "3", "--keep-weekly", "2", "--keep-monthly", "1"], forget.Arguments.SkipWhile(a => a != "--keep-daily").ToArray());
    }

    [Fact]
    public async Task Backup_creates_a_missing_repository()
    {
        ScriptSummary();
        environment.Runner.Script["restic cat"] = _ => new ProcessResult(10, "repository does not exist");

        await environment.Engine.BackupAsync(BackupTestEnvironment.Target, Ct);

        Assert.Equal("restic init", environment.Runner.Commands.ElementAt(1));
    }

    [Fact]
    public async Task Backup_stops_with_the_end_of_the_output_when_a_step_fails()
    {
        environment.Runner.Script["pg_dump --format=custom"] = _ => new ProcessResult(1, "a\nb\nc\npg_dump: connection refused\n");

        var error = await Assert.ThrowsAsync<BackupException>(() => environment.Engine.BackupAsync(BackupTestEnvironment.Target, Ct));

        Assert.Equal("b c pg_dump: connection refused", error.Message);
        Assert.DoesNotContain("restic backup", environment.Runner.Commands);
        Assert.False(Directory.Exists(environment.Settings.WorkDir));
    }

    [Theory]
    [InlineData(0, RepositoryState.Ready)]
    [InlineData(10, RepositoryState.New)]
    [InlineData(12, RepositoryState.WrongPassword)]
    [InlineData(1, RepositoryState.Unreachable)]
    public async Task Test_maps_the_restic_exit_code(int exitCode, RepositoryState expected)
    {
        environment.Runner.Script["restic cat"] = _ => new ProcessResult(exitCode, "unable to open config");

        var check = await environment.Engine.TestAsync(BackupTestEnvironment.Target, Ct);

        Assert.Equal(expected, check.State);
        Assert.Equal(["restic cat"], environment.Runner.Commands);
    }

    [Fact]
    public async Task Snapshots_are_listed_newest_first()
    {
        environment.Runner.Script["restic snapshots"] = _ => new ProcessResult(0,
            "[{\"id\":\"aaaaaaaa11111111\",\"short_id\":\"aaaaaaaa\",\"time\":\"2026-03-01T01:30:00Z\"},{\"id\":\"bbbbbbbb22222222\",\"short_id\":\"bbbbbbbb\",\"time\":\"2026-03-02T01:30:00Z\"}]");

        var snapshots = await environment.Engine.SnapshotsAsync(BackupTestEnvironment.Target, Ct);

        Assert.Equal(["bbbbbbbb", "aaaaaaaa"], snapshots.Select(s => s.ShortId));
    }

    [Fact]
    public async Task Snapshots_of_a_missing_repository_are_an_error()
    {
        environment.Runner.Script["restic cat"] = _ => new ProcessResult(10, string.Empty);

        await Assert.ThrowsAsync<BackupException>(() => environment.Engine.SnapshotsAsync(BackupTestEnvironment.Target, Ct));
    }

    [Theory]
    [InlineData("latest; rm -rf /")]
    [InlineData("--target")]
    [InlineData("xyz")]
    public async Task Restore_refuses_anything_but_latest_or_a_hexadecimal_id(string snapshot)
    {
        await Assert.ThrowsAsync<BackupException>(() => environment.Engine.RestoreAsync(BackupTestEnvironment.Target, snapshot, force: true, Ct));

        Assert.Empty(environment.Runner.Calls);
    }

    [Fact]
    public async Task Restore_without_force_refuses_while_the_app_is_connected()
    {
        environment.Runner.Script["psql -d"] = _ => new ProcessResult(0, "2\n");

        var error = await Assert.ThrowsAsync<BackupException>(() => environment.Engine.RestoreAsync(BackupTestEnvironment.Target, "latest", force: false, Ct));

        Assert.Contains("2 Verbindung", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("restic restore", environment.Runner.Commands);
    }

    [Fact]
    public async Task Restore_replaces_database_and_files()
    {
        await File.WriteAllTextAsync(Path.Combine(environment.Settings.DataPath, "old.pdf"), "old", Ct);
        environment.Runner.Script["restic restore"] = _ =>
        {
            var dump = environment.Settings.RestoreDir + Path.Combine(environment.Settings.WorkDir, "elocaro.dump");
            Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
            File.WriteAllText(dump, "dump");
            Directory.CreateDirectory(environment.Settings.RestoreDir + environment.Settings.DataPath);
            return new ProcessResult(0, string.Empty);
        };

        await environment.Engine.RestoreAsync(BackupTestEnvironment.Target, "latest", force: true, Ct);

        Assert.Equal(["restic cat", "restic restore", "psql -d", "pg_restore --exit-on-error", "cp -a", "chown -R"], environment.Runner.Commands);
        Assert.False(File.Exists(Path.Combine(environment.Settings.DataPath, "old.pdf")));
        Assert.False(Directory.Exists(environment.Settings.RestoreDir));
        var chown = environment.Runner.Calls.Single(c => c.File == "chown");
        Assert.Equal("1654:1654", chown.Arguments[1]);
    }

    [Fact]
    public async Task Restore_of_a_backup_without_database_dump_leaves_the_database_alone()
    {
        var error = await Assert.ThrowsAsync<BackupException>(() => environment.Engine.RestoreAsync(BackupTestEnvironment.Target, "latest", force: true, Ct));

        Assert.Contains("keinen Datenbank-Dump", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("psql -d", environment.Runner.Commands);
    }
}
