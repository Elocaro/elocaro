using Elocaro.Domain.Common;
using Quartz;

namespace Elocaro.Infrastructure.Jobs;

internal static class QuartzExtensions
{
    /// <summary>Schedules a job that runs at a fixed interval.</summary>
    public static void ScheduleInterval<TJob>(this IQuartzBuilder q, JobKey key, string description, TimeSpan interval)
        where TJob : IJob
    {
        q.AddJob<TJob>(j => j.WithIdentity(key).StoreDurably().WithDescription(description));
        q.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity(key.Name + "-trigger", key.Group)
            .StartNow()
            .WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount)));
    }

    /// <summary>Schedules a job daily at the given German time of day.</summary>
    public static void ScheduleDaily<TJob>(this IQuartzBuilder q, JobKey key, string description, int hour, int minute)
        where TJob : IJob
    {
        q.AddJob<TJob>(j => j.WithIdentity(key).StoreDurably().WithDescription(description));
        q.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity(key.Name + "-trigger", key.Group)
            .WithCronSchedule(
                CronScheduleBuilder.Create($"0 {minute} {hour} ? * *")
                    .InTimeZone(GermanTime.Germany)
                    .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed)));
    }
}
