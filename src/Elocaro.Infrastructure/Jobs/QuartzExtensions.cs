using Elocaro.Domain.Common;
using Quartz;

namespace Elocaro.Infrastructure.Jobs;

internal static class QuartzExtensions
{
    /// <summary>Schedules a job that runs at a fixed interval.</summary>
    public static void ScheduleInterval<TJob>(this IServiceCollectionQuartzConfigurator q, JobKey key, string description, TimeSpan interval)
        where TJob : IJob
    {
        q.AddJob<TJob>(key, j => j.StoreDurably().WithDescription(description));
        q.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity(key.Name + "-trigger", key.Group)
            .StartNow()
            .WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever().WithMisfireHandlingInstructionNextWithRemainingCount()));
    }

    /// <summary>Schedules a job daily at the given German time of day.</summary>
    public static void ScheduleDaily<TJob>(this IServiceCollectionQuartzConfigurator q, JobKey key, string description, int hour, int minute)
        where TJob : IJob
    {
        q.AddJob<TJob>(key, j => j.StoreDurably().WithDescription(description));
        q.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity(key.Name + "-trigger", key.Group)
            .WithCronSchedule(
                CronScheduleBuilder.DailyAtHourAndMinute(hour, minute)
                    .InTimeZone(GermanTime.Germany)
                    .WithMisfireHandlingInstructionFireAndProceed()));
    }
}
