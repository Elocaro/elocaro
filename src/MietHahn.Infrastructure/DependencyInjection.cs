using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Betrieb;
using MietHahn.Application.Dateien;
using MietHahn.Application.Dokumente;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Kommunikation;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Dateien;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Jobs;
using MietHahn.Infrastructure.Mail;
using MietHahn.Infrastructure.Pdf;
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

        services.Configure<AnwendungOptionen>(konfiguration.GetSection(AnwendungOptionen.Abschnitt));

        PersistenzRegistrieren(services, konfiguration);
        IdentitaetRegistrieren(services);
        AuditRegistrieren(services, konfiguration);
        MailRegistrieren(services, konfiguration);
        DateiablageRegistrieren(services, konfiguration);
        PdfRegistrieren(services, konfiguration);
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

    private static void IdentitaetRegistrieren(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddDataProtection()
            .SetApplicationName("MietHahn")
            .PersistKeysToDbContext<MietHahnDbContext>();

        services.AddIdentityCore<Benutzer>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;

                // Länge statt Zeichenklassen (NIST SP 800-63B).
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<Rolle>()
            .AddEntityFrameworkStores<MietHahnDbContext>()
            .AddSignInManager<MietHahnSignInManager>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EinladungsTokenProvider>(TokenAnbieter.Einladung)
            .AddClaimsPrincipalFactory<MietHahnClaimsFactory>()
            .AddErrorDescriber<DeutscheIdentityFehler>();

        // Passwort-Reset-Links: 2 Stunden; Einladungen nutzen den eigenen Anbieter (7 Tage).
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.AddScoped<IKontoverwaltung, Kontoverwaltung>();
        services.AddScoped<IBenutzerverwaltung, Benutzerverwaltung>();
    }

    private static void AuditRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        services.Configure<AuditOptionen>(konfiguration.GetSection(AuditOptionen.Abschnitt));
        services.AddScoped<IZugriffsprotokoll, Zugriffsprotokoll>();
        services.AddScoped<IAenderungsHistorie, AenderungsHistorie>();
        services.AddScoped<AuditBereinigung>();
        services.AddScoped<AuditSchreiber>();
    }

    private static void MailRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        services.Configure<MailOptionen>(konfiguration.GetSection(MailOptionen.Abschnitt));
        services.TryAddSingleton<ScribanRenderer>();
        services.AddScoped<IMailVersand, MailVersand>();
        services.TryAddScoped<ISmtpVersender, MailKitSmtpVersender>();
        services.AddScoped<MailOutbox>();
        services.AddScoped<IMailVerwaltung, MailVerwaltung>();
        services.AddScoped<IAufgabenQuelle, MailAufgaben>();
    }

    private static void DateiablageRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        services.Configure<DateiablageOptionen>(konfiguration.GetSection(DateiablageOptionen.Abschnitt));
        services.AddScoped<IDateiablage, LokaleDateiablage>();
    }

    private static void PdfRegistrieren(IServiceCollection services, IConfiguration konfiguration)
    {
        services.Configure<PdfOptionen>(konfiguration.GetSection(PdfOptionen.Abschnitt));
        services.AddHttpClient<GotenbergKonverter>((sp, http) =>
        {
            var optionen = sp.GetRequiredService<IOptions<PdfOptionen>>().Value;
            http.BaseAddress = optionen.GotenbergUrl;
            http.Timeout = TimeSpan.FromSeconds(optionen.TimeoutSekunden);
        });
        services.AddScoped<IPdfErzeugung, PdfErzeugung>();
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
            q.PlaneTaeglich<AuditBereinigungJob>(AuditBereinigungJob.Schluessel, "Löscht Audit-Einträge nach Ablauf der Aufbewahrungsfrist", 3, 30);
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
