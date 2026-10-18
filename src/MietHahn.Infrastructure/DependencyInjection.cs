using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MietHahn.Application.Betrieb;
using MietHahn.Application.Dateien;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Kommunikation;
using MietHahn.Infrastructure.Dateien;
using MietHahn.Infrastructure.Jobs;
using MietHahn.Infrastructure.Mail;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Vorlagen;
using Quartz;

namespace MietHahn.Infrastructure;

public static class DependencyInjection
{
    public const string VerbindungsName = "MietHahn";

    /// <summary>Registriert Persistenz und Infrastrukturdienste (Mail, Dateiablage, Hintergrundjobs).</summary>
    public static IServiceCollection AddMietHahnInfrastruktur(this IServiceCollection services, IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAktuellerBenutzer, SystemBenutzer>();

        PersistenzRegistrieren(services, konfiguration);
        MailRegistrieren(services, konfiguration);
        DateiablageRegistrieren(services, konfiguration);
        JobsRegistrieren(services, konfiguration);

        return services;
    }

    internal static void KonfiguriereNpgsql(DbContextOptionsBuilder optionen, string verbindung) =>
        optionen
            .UseNpgsql(verbindung, npgsql => npgsql.MigrationsHistoryTable("__ef_migrationen"))
            .UseSnakeCaseNamingConvention();

    private static void PersistenzRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        services.Configure<DatenbankOptionen>(konfiguration.GetSection(DatenbankOptionen.Abschnitt));
        services.AddScoped<EntitaetSpeichernInterceptor>();

        services.AddDbContext<MietHahnDbContext>((sp, optionen) =>
        {
            KonfiguriereNpgsql(optionen, Verbindung(konfiguration));
            optionen.AddInterceptors(sp.GetRequiredService<EntitaetSpeichernInterceptor>());
        });
    }

    private static void MailRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        services.Configure<MailOptionen>(konfiguration.GetSection(MailOptionen.Abschnitt));
        services.TryAddSingleton<ScribanRenderer>();
        services.AddScoped<IMailVersand, MailVersand>();
        services.TryAddScoped<ISmtpVersender, MailKitSmtpVersender>();
        services.AddScoped<MailOutbox>();
        services.AddScoped<IMailVerwaltung, MailVerwaltung>();
    }

    private static void DateiablageRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        services.Configure<DateiablageOptionen>(konfiguration.GetSection(DateiablageOptionen.Abschnitt));
        services.AddScoped<IDateiablage, LokaleDateiablage>();
    }

    private static void JobsRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        var abschnitt = konfiguration.GetSection(JobsOptionen.Abschnitt);
        services.Configure<JobsOptionen>(abschnitt);
        var jobs = abschnitt.Get<JobsOptionen>() ?? new JobsOptionen();

        if (!jobs.Aktiv)
        {
            services.AddScoped<IJobUebersicht, LeereJobUebersicht>();
            return;
        }

        services.AddSingleton<JobLaufProtokoll>();
        services.AddQuartz(q =>
        {
            q.SchedulerName = "MietHahn";
            q.SchedulerId = "AUTO";
            q.UsePersistentStore(s =>
            {
                s.UsePostgres(Verbindung(konfiguration));
                s.UseSystemTextJsonSerializer();
                s.PerformSchemaValidation = true;
            });
            q.AddJobListener<JobLaufProtokoll>();

            q.PlaneIntervall<MailOutboxJob>(MailOutboxJob.Schluessel, "Versendet fällige E-Mails aus der Outbox", TimeSpan.FromSeconds(30));
            q.PlaneTaeglich<JobProtokollBereinigungJob>(JobProtokollBereinigungJob.Schluessel, "Löscht alte Einträge des Job-Laufprotokolls", 3, 15);
        });
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        services.AddScoped<IJobUebersicht, QuartzJobUebersicht>();
    }

    private static string Verbindung(IConfiguration konfiguration)
    {
        var verbindung = konfiguration.GetConnectionString(VerbindungsName);
        if (string.IsNullOrWhiteSpace(verbindung))
        {
            throw new InvalidOperationException($"Verbindungszeichenfolge 'ConnectionStrings:{VerbindungsName}' fehlt.");
        }

        return verbindung;
    }
}
