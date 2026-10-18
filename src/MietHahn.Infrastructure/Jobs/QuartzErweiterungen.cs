using MietHahn.Domain.Gemeinsam;
using Quartz;

namespace MietHahn.Infrastructure.Jobs;

internal static class QuartzErweiterungen
{
    /// <summary>Plant einen Job, der in festem Abstand läuft.</summary>
    public static void PlaneIntervall<TJob>(this IServiceCollectionQuartzConfigurator q, JobKey schluessel, string beschreibung, TimeSpan intervall)
        where TJob : IJob
    {
        q.AddJob<TJob>(schluessel, j => j.StoreDurably().WithDescription(beschreibung));
        q.AddTrigger(t => t
            .ForJob(schluessel)
            .WithIdentity(schluessel.Name + "-trigger", schluessel.Group)
            .StartNow()
            .WithSimpleSchedule(s => s.WithInterval(intervall).RepeatForever().WithMisfireHandlingInstructionNextWithRemainingCount()));
    }

    /// <summary>Plant einen Job täglich zur angegebenen deutschen Uhrzeit.</summary>
    public static void PlaneTaeglich<TJob>(this IServiceCollectionQuartzConfigurator q, JobKey schluessel, string beschreibung, int stunde, int minute)
        where TJob : IJob
    {
        q.AddJob<TJob>(schluessel, j => j.StoreDurably().WithDescription(beschreibung));
        q.AddTrigger(t => t
            .ForJob(schluessel)
            .WithIdentity(schluessel.Name + "-trigger", schluessel.Group)
            .WithCronSchedule(
                CronScheduleBuilder.DailyAtHourAndMinute(stunde, minute)
                    .InTimeZone(Zeitzone.Deutschland)
                    .WithMisfireHandlingInstructionFireAndProceed()));
    }
}
