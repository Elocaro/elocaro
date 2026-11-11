using Elocaro.Application.Operations;
using Elocaro.Domain.Common;
using Quartz;

namespace Elocaro.Infrastructure.Operations;

internal static class BackupSchedule
{
    /// <summary>
    /// Due once the time of day has passed and nothing was tried since then (a failed attempt counts: no retry storm,
    /// the warning mail reports it). Catches up a backup if the app was not running at the time.
    /// </summary>
    public static bool IsDue(DateTimeOffset now, TimeOnly time, DateTimeOffset? lastAttempt)
    {
        var zone = GermanTime.Germany;
        var local = TimeZoneInfo.ConvertTime(now, zone).Date + time.ToTimeSpan();
        // The hour skipped by the switch to daylight saving time does not exist.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var scheduled = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return now >= scheduled && (lastAttempt is null || lastAttempt < scheduled);
    }
}

/// <summary>Starts the daily backup at the time set in the app (only if the app manages the backup).</summary>
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
        if (await settings.GetAsync(cancellationToken) is not { } current)
        {
            return;
        }

        // Keeps the daily backup of the container environment paused, even if its volume was recreated.
        await sidecar.SetManagedAsync(true, cancellationToken);

        var progress = await control.ProgressAsync(cancellationToken);
        if (!current.Enabled || !progress.Available || progress.Running)
        {
            return;
        }

        var last = await status.QueryAsync(cancellationToken);
        if (BackupSchedule.IsDue(clock.GetUtcNow(), current.Time, last.Timestamp))
        {
            await control.StartBackupAsync(cancellationToken);
        }
    }
}
