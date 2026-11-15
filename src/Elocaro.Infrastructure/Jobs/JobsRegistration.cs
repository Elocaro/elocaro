using Elocaro.Application.Operations;
using Elocaro.Infrastructure.Admin;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Deadlines;
using Elocaro.Infrastructure.Finance;
using Elocaro.Infrastructure.Heating;
using Elocaro.Infrastructure.Leases;
using Elocaro.Infrastructure.Mail;
using Elocaro.Infrastructure.Metering;
using Elocaro.Infrastructure.Notices;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Portal;
using Elocaro.Infrastructure.Receipts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Elocaro.Infrastructure.Jobs;

/// <summary>Registrations of the Jobs module.</summary>
internal static class JobsRegistration
{
    internal static IServiceCollection AddJobsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(JobsOptions.Section);
        services.Configure<JobsOptions>(section);
        var jobs = section.Get<JobsOptions>() ?? new JobsOptions();

        if (!jobs.Active)
        {
            services.AddScoped<IJobOverview, EmptyJobOverview>();
            return services;
        }

        services.AddSingleton<JobRunLog>();
        services.AddSingleton<ModuleJobGate>();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(o =>
            {
                o.InstanceName = "Elocaro";
                o.GenerateInstanceId = true;
            });
            q.UsePersistentStore(s =>
            {
                s.UsePostgres(DependencyInjection.Connection(configuration));
                s.UseSystemTextJsonSerializer();
            });
            q.AddJobListener<JobRunLog>();
            q.AddTriggerListener<ModuleJobGate>();

            q.ScheduleInterval<MailOutboxJob>(MailOutboxJob.Key, "Versendet fällige E-Mails aus der Outbox", TimeSpan.FromSeconds(30));
            q.ScheduleDaily<JobLogCleanupJob>(JobLogCleanupJob.Key, "Löscht alte Einträge des Job-Laufprotokolls", 3, 15);
            q.ScheduleDaily<MailRetentionJob>(MailRetentionJob.Key, "Leert geheime Mails und löscht Mails nach Ablauf der Aufbewahrungsfrist", 3, 45);
            q.ScheduleDaily<AuditCleanupJob>(AuditCleanupJob.Key, "Löscht Audit-Einträge nach Ablauf der Aufbewahrungsfrist", 3, 30);
            q.ScheduleDaily<RentStepJob>(RentStepJob.Key, "Übernimmt fällige Staffeln als Kaltmiete und informiert die Admins", 0, 30);
            q.ScheduleDaily<ChargeJob>(ChargeJob.Key, "Erzeugt die Monatsmieten (Sollstellungen) und gleicht sie an Vertragsänderungen an", 0, 40);
            q.ScheduleDaily<DunningJob>(DunningJob.Key, "Erkennt Rückstände und schlägt Mahnungen vor (versendet nichts, Freigabe nötig)", 6, 15);
            q.ScheduleDaily<DeadlineReminderJob>(DeadlineReminderJob.Key, "Schickt Erinnerungen zu Fristen und Wartungen und montags die Wochenübersicht", 7, 30);
            q.ScheduleInterval<DocumentNotificationJob>(DocumentNotificationJob.Key, "Informiert Mieter per Mail über neue Dokumente im Portal", TimeSpan.FromMinutes(10));
            q.ScheduleInterval<NoticeNotificationJob>(NoticeNotificationJob.Key, "Informiert Mieter per Mail über neue Aushänge im Portal", TimeSpan.FromMinutes(10));
            q.ScheduleDaily<ConsumptionNotificationJob>(ConsumptionNotificationJob.Key, "Schickt Mietern die monatliche Verbrauchsinformation (Heizung, Warmwasser) per Mail", 8, 15);
            q.ScheduleDaily<ReadingReminderJob>(ReadingReminderJob.Key, "Erinnert Mieter ab dem 15. Dezember, die Zählerstände zum Jahreswechsel im Portal zu melden", 8, 25);
            q.ScheduleInterval<ReceiptInboxJob>(ReceiptInboxJob.Key, "Holt Belege aus dem Beleg-Postfach ab (nur wenn eingerichtet)", TimeSpan.FromMinutes(5));
            q.ScheduleInterval<DegreeDayJob>(DegreeDayJob.Key, "Holt die Monatsgradtage der Wetterstationen vom Deutschen Wetterdienst (Aufteilung der Heizkosten beim Mieterwechsel)", TimeSpan.FromHours(12));
            q.ScheduleInterval<Notifications.PushDispatchJob>(Notifications.PushDispatchJob.Key, "Schickt Hinweise an Geräte, die Benachrichtigungen eingeschaltet haben", TimeSpan.FromMinutes(1));
            q.ScheduleInterval<UpdateCheckJob>(UpdateCheckJob.Key, "Prüft, ob eine neuere Version von Elocaro veröffentlicht wurde (nur Anzeige, es wird nichts installiert)", TimeSpan.FromHours(12));
            q.ScheduleInterval<BackupJob>(BackupJob.Key, "Startet die tägliche Datensicherung zur eingestellten Zeit (nur wenn die Sicherung in der App eingerichtet ist)", TimeSpan.FromMinutes(10));
            q.ScheduleDaily<BackupWarningJob>(BackupWarningJob.Key, "Warnt Admins per Mail, wenn die Datensicherung fehlgeschlagen oder veraltet ist", 8, 5);
        });
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        services.AddScoped<IJobOverview, QuartzJobOverview>();

        return services;
    }
}
