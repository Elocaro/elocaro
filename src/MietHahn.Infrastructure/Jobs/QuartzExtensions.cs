using MietHahn.Domain.Common;
using Quartz;

namespace MietHahn.Infrastructure.Jobs;

internal static class QuartzExtensions
{
    /// <summary>Plant einen Job, der in festem Abstand läuft.</summary>
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

    /// <summary>Plant einen Job täglich zur angegebenen deutschen Uhrzeit.</summary>
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
