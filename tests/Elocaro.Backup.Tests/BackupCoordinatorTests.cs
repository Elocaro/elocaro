using Elocaro.Backup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Elocaro.Backup.Tests;

public sealed class BackupCoordinatorTests : IDisposable
{
    private readonly BackupTestEnvironment environment = new();
    private readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-03-01T01:30:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => environment.Dispose();

    private BackupCoordinator Coordinator() => new(environment.Engine, environment.Settings, new BackupTransfers(environment.Settings, clock), clock, NullLogger<BackupCoordinator>.Instance);

    [Fact]
    public async Task A_successful_backup_writes_the_status_file_for_the_app()
    {
        environment.Runner.Script["restic backup"] = _ => new ProcessResult(0, "{\"message_type\":\"summary\",\"snapshot_id\":\"49ccbcd74357a447\"}");
        var coordinator = Coordinator();

        Assert.True(coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct));
        await coordinator.Running;

        // Format that BackupStatus of the app reads (see BackupMonitoringTests)
        Assert.Equal("""{"timestamp":"2026-03-01T01:30:00Z","succeeded":true,"message":"Sicherung erfolgreich","snapshot":"49ccbcd74357a447","durationSeconds":0}""",
            await File.ReadAllTextAsync(environment.Settings.StatusFile, Ct));
        var status = coordinator.Status();
        Assert.False(status.Running);
        Assert.Equal("49ccbcd74357a447", status.Last!.Snapshot);
    }

    [Fact]
    public async Task A_failed_backup_is_reported_and_written_as_failed()
    {
        environment.Runner.Script["pg_dump --format=custom"] = _ => new ProcessResult(1, "no connection");
        var coordinator = Coordinator();

        coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct);
        await coordinator.Running;

        Assert.False(coordinator.Status().Last!.Succeeded);
        Assert.Contains("\"succeeded\":false", await File.ReadAllTextAsync(environment.Settings.StatusFile, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restore_does_not_touch_the_status_file_of_the_backup()
    {
        var coordinator = Coordinator();

        coordinator.TryStartRestore(BackupTestEnvironment.Target, "latest", force: true, Ct);
        await coordinator.Running;

        Assert.False(File.Exists(environment.Settings.StatusFile));
        Assert.Equal(OperationKind.Restore, coordinator.Status().Last!.Kind);
    }

    [Fact]
    public async Task Only_one_operation_runs_at_a_time()
    {
        var release = new TaskCompletionSource();
        environment.Runner.Script["restic cat"] = _ =>
        {
            release.Task.GetAwaiter().GetResult();
            return new ProcessResult(0, string.Empty);
        };
        var coordinator = Coordinator();

        Assert.True(coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct));
        Assert.True(coordinator.Status().Running);
        Assert.False(coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct));
        Assert.False(coordinator.TryStartRestore(BackupTestEnvironment.Target, "latest", force: true, Ct));

        release.SetResult();
        await coordinator.Running;
        Assert.True(coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct));
        await coordinator.Running;
    }

    [Fact]
    public async Task Each_target_has_its_own_result_and_status_file()
    {
        environment.Runner.Script["restic backup"] = _ => new ProcessResult(0, "{\"snapshot_id\":\"aaaa1111bbbb\"}");
        var coordinator = Coordinator();

        coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct, "drive");
        await coordinator.Running;
        environment.Runner.Script["pg_dump --format=custom"] = _ => new ProcessResult(1, "kaputt");
        coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct, "nas");
        await coordinator.Running;

        var status = coordinator.Status();
        Assert.True(status.Results!["drive"].Succeeded);
        Assert.False(status.Results["nas"].Succeeded);
        Assert.Equal("nas", status.Last!.TargetId);
        var directory = Path.GetDirectoryName(environment.Settings.StatusFile)!;
        Assert.Contains("\"succeeded\":true", await File.ReadAllTextAsync(Path.Combine(directory, "backup-drive.json"), Ct), StringComparison.Ordinal);
        Assert.Contains("\"succeeded\":false", await File.ReadAllTextAsync(Path.Combine(directory, "backup-nas.json"), Ct), StringComparison.Ordinal);
        Assert.False(File.Exists(environment.Settings.StatusFile));
    }

    [Fact]
    public async Task The_running_target_is_reported()
    {
        var release = new TaskCompletionSource();
        environment.Runner.Script["restic cat"] = _ =>
        {
            release.Task.GetAwaiter().GetResult();
            return new ProcessResult(0, string.Empty);
        };
        var coordinator = Coordinator();

        coordinator.TryStartBackup(BackupTestEnvironment.Target, Ct, "drive");

        Assert.Equal("drive", coordinator.Status().TargetId);
        release.SetResult();
        await coordinator.Running;
        Assert.Null(coordinator.Status().TargetId);
    }
}
