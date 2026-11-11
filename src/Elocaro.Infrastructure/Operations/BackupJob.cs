using Elocaro.Application.Operations;
using Elocaro.Domain.Common;
using Quartz;

namespace Elocaro.Infrastructure.Operations;

internal static class BackupSchedule
{
    /// <summary>
    /// Due once the time of day has passed and nothing was tried since <paramref name="intervalDays"/> days before (a failed attempt counts:
    /// no retry storm, the warning mail reports it). Catches up a backup if the app was not running at the time.
    /// </summary>
    public static bool IsDue(DateTimeOffset now, TimeOnly time, int intervalDays, DateTimeOffset? lastAttempt)
    {
        var zone = GermanTime.Germany;
        var local = TimeZoneInfo.ConvertTime(now, zone).Date + time.ToTimeSpan();
        // The hour skipped by the switch to daylight saving time does not exist.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var scheduled = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return now >= scheduled && (lastAttempt is null || lastAttempt < scheduled.AddDays(-(Math.Max(intervalDays, 1) - 1)));
    }
}

/// <summary>Starts the backups at the times set in the app, one target at a time (only if the app manages the backup).</summary>
[DisallowConcurrentExecution]
internal sealed class BackupJob(IBackupSettings settings, IBackupControl control, IBackupStatus status, BackupSidecar sidecar, TimeProvider clock) : IJob
{
    public static readonly JobKey Key = new("backup", "system");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await RunAsync(cancellationToken);
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var targets = await settings.ListAsync(cancellationToken);
        if (targets.Count == 0)
        {
            return;
        }

        // Keeps the daily backup of the container environment paused, even if its volume was recreated.
        await sidecar.SetManagedAsync(true, cancellationToken);

        var progress = await control.ProgressAsync(cancellationToken);
        if (!progress.Available || progress.Running)
        {
            return;
        }

        var states = (await status.QueryTargetsAsync(cancellationToken)).ToDictionary(s => s.Id);
        var now = clock.GetUtcNow();
        foreach (var target in targets.Where(t => t.Enabled))
        {
            var last = states.GetValueOrDefault(target.Id)?.Status.Timestamp;
            if (BackupSchedule.IsDue(now, target.Time, target.IntervalDays, last))
            {
                // One at a time: the container is busy with this one, the next target follows in one of the next runs.
                await control.StartBackupAsync(target.Id, cancellationToken);
                return;
            }
        }
    }
}
