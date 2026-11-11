using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elocaro.Backup;

/// <summary>
/// Daily backup for installations that configure the target through the environment (<c>RESTIC_REPOSITORY</c>, <c>BACKUP_TIME</c>).
/// Without such a target nothing runs here; the app then schedules backups itself and calls the API.
/// </summary>
internal sealed class EnvironmentScheduler(BackupCoordinator coordinator, BackupSettings settings, TimeProvider clock, ILogger<EnvironmentScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.EnvironmentTarget is not { } target)
        {
            BackupLog.NoEnvironmentTarget(logger);
            return;
        }

        if (!DailySchedule.TryParse(settings.BackupTime, out var time))
        {
            BackupLog.InvalidTime(logger, settings.BackupTime);
            return;
        }

        BackupLog.Scheduled(logger, settings.BackupTime, clock.LocalTimeZone.Id);
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = DailySchedule.Until(clock.GetUtcNow(), time, clock.LocalTimeZone);
            BackupLog.NextRun(logger, (int)wait.TotalMinutes);
            await Task.Delay(wait, clock, stoppingToken);
            if (File.Exists(settings.ManagedMarker))
            {
                BackupLog.ManagedByApp(logger);
            }
            else if (coordinator.TryStartBackup(target, stoppingToken))
            {
                await coordinator.Running;
            }

            await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
        }
    }
}
