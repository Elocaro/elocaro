using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Application.Bank;
using Elocaro.Application.Common;
using Elocaro.Application.Communication;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Documents;
using Elocaro.Application.Emergency;
using Elocaro.Application.Files;
using Elocaro.Application.Finance;
using Elocaro.Application.Heating;
using Elocaro.Application.Leases;
using Elocaro.Application.Legal;
using Elocaro.Application.Metering;
using Elocaro.Application.Notices;
using Elocaro.Application.Notifications;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.Operations;
using Elocaro.Application.People;
using Elocaro.Application.Portal;
using Elocaro.Application.Portfolio;
using Elocaro.Application.Privacy;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Setup;
using Elocaro.Application.Statements;
using Elocaro.Application.Tax;
using Elocaro.Application.Tickets;
using Elocaro.Application.Todos;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Admin;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Bank;
using Elocaro.Infrastructure.Bank.FinTs;
using Elocaro.Infrastructure.Correspondence;
using Elocaro.Infrastructure.Deadlines;
using Elocaro.Infrastructure.Documents;
using Elocaro.Infrastructure.Files;
using Elocaro.Infrastructure.Finance;
using Elocaro.Infrastructure.Handover;
using Elocaro.Infrastructure.Heating;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Jobs;
using Elocaro.Infrastructure.Leases;
using Elocaro.Infrastructure.Mail;
using Elocaro.Infrastructure.Metering;
using Elocaro.Infrastructure.Notices;
using Elocaro.Infrastructure.OperatingCosts;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Pdf;
using Elocaro.Infrastructure.People;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Portal;
using Elocaro.Infrastructure.Privacy;
using Elocaro.Infrastructure.Properties;
using Elocaro.Infrastructure.Receipts;
using Elocaro.Infrastructure.Receipts.Extraction;
using Elocaro.Infrastructure.Statements;
using Elocaro.Infrastructure.Tax;
using Elocaro.Infrastructure.Templates;
using Elocaro.Infrastructure.Tickets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quartz;

namespace Elocaro.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionName = "Elocaro";

    /// <summary>Registers persistence and infrastructure services (mail, file store, background jobs).</summary>
    public static IServiceCollection AddElocaroInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // The one place every host passes (web app, jobs, tests): texts are German whatever the system language is.
        AppCulture.Apply();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, SystemUser>();

        services.Configure<ApplicationOptions>(configuration.GetSection(ApplicationOptions.Section));
        services.Configure<SetupOptions>(configuration.GetSection(SetupOptions.Section));
        services.Configure<FinTsOptions>(configuration.GetSection(FinTsOptions.Section));
        services.Configure<Application.Admin.DemoOptions>(configuration.GetSection(Application.Admin.DemoOptions.Section));

        AddPersistence(services, configuration);
        AddIdentity(services, configuration);
        AddAudit(services, configuration);
        AddMail(services, configuration);
        AddFileStore(services, configuration);
        AddPdf(services, configuration);
        AddReceiptExtraction(services, configuration);
        AddOperations(services, configuration);
        AddMasterData(services);
        AddPrivacy(services, configuration);
        services.Configure<DegreeDayOptions>(configuration.GetSection(DegreeDayOptions.Section));
        AddJobs(services, configuration);
        AddHealthChecks(services);

        return services;
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, string connection) =>
        options
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations").UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery))
            .UseSnakeCaseNamingConvention();

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.AddScoped<EntitySaveInterceptor>();

        services.AddDbContext<ElocaroDbContext>((sp, options) =>
        {
            ConfigureNpgsql(options, Connection(configuration));
            options.AddInterceptors(sp.GetRequiredService<EntitySaveInterceptor>());
        });
    }

    /// <summary>The optional certificate protecting the data protection keys; a configured but unusable one stops the start.</summary>
    private static X509Certificate2? KeyCertificate(IConfiguration configuration)
    {
        var path = configuration["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Zertifikat für den Schlüsselschutz fehlt: Die Datei '{path}' (DataProtection:CertificatePath) existiert nicht.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, configuration["DataProtection:CertificatePassword"]);
        }
        catch (CryptographicException e)
        {
            throw new InvalidOperationException($"Zertifikat für den Schlüsselschutz lässt sich nicht öffnen: '{path}' (Dateirechte und Passwort in DataProtection:CertificatePassword prüfen).", e);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"Zertifikat für den Schlüsselschutz enthält keinen privaten Schlüssel: '{path}'.");
        }

        return certificate;
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        var dataProtection = services.AddDataProtection()
            // The name takes part in deriving the keys: changing it makes everything protected so far unreadable.
            .SetApplicationName("Elocaro")
            .PersistKeysToDbContext<ElocaroDbContext>();

        // Encrypts new keys only; keys already stored without encryption stay as they are and stay readable.
        if (KeyCertificate(configuration) is { } certificate)
        {
            dataProtection.ProtectKeysWithCertificate(certificate);
        }

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;

                // Length instead of character classes (NIST SP 800-63B).
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<ElocaroDbContext>()
            .AddSignInManager<ElocaroSignInManager>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InvitationTokenProvider>(TokenProviders.Invitation)
            .AddClaimsPrincipalFactory<ElocaroClaimsFactory>()
            .AddErrorDescriber<GermanIdentityErrorDescriber>();

        // Password reset links: 2 hours; invitations use their own provider (7 days).
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.AddScoped<IAccountService, AccountService>();
        services.AddSingleton<ISetupCode, SetupCode>();
        services.AddScoped<IUserManagementService, UserManagement>();
    }

    private static void AddAudit(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.Section));
        services.AddScoped<IAccessLog, AccessLog>();
        services.AddScoped<IChangeHistory, ChangeHistory>();
        services.AddScoped<AuditCleanup>();
        services.AddScoped<AuditWriter>();
    }

    private static void AddMail(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.Section));
        services.AddScoped<IPersonalDataSource, MailPersonalData>();
        services.AddScoped<MailRetention>();
        services.TryAddSingleton<ScribanRenderer>();
        services.AddScoped<MailSender>();
        services.AddScoped<IMailSender>(sp => new Notifications.PushingMailSender(sp.GetRequiredService<MailSender>(), sp.GetRequiredService<IPushNotifications>(), sp.GetRequiredService<UserManager<ApplicationUser>>()));
        services.Configure<Notifications.PushOptions>(configuration.GetSection(Notifications.PushOptions.Section));
        services.AddHttpClient(Notifications.PushNotifications.ClientName, http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IPushNotifications, Notifications.PushNotifications>();
        services.TryAddScoped<ISmtpSender, MailKitSmtpSender>();
        services.AddScoped<MailOutbox>();
        services.AddScoped<IMailService, MailService>();
        services.AddScoped<ITodoSource, MailTodos>();
        services.AddScoped<ITodoSource, Setup.SetupChecklistTodos>();
        services.AddScoped<IOptionUsageSource, Leases.LeaseOptionUsage>();
        services.AddScoped<IOptionUsageSource, Properties.PropertyOptionUsage>();
        services.AddScoped<IOptionUsageSource, Finance.FinanceOptionUsage>();
        services.AddScoped<IOptionUsageSource, Tax.TaxOptionUsage>();
        services.AddScoped<IOptionUsageSource, Identity.OwnerOptionUsage>();
        services.AddScoped<ILegalReferences, Legal.LegalReferences>();
        services.AddScoped<IDataImport, Setup.DataImport>();
        services.AddScoped<IPortfolioFigures, Portfolio.PortfolioFiguresService>();
        services.AddScoped<IDemoData, Setup.DemoData>();
        services.AddScoped<IDeposits, Leases.Deposits>();
        services.AddScoped<ITodoSource, Leases.DepositTodos>();
        services.AddScoped<ITodoSource, Legal.LegalReferenceTodos>();
    }

    private static void AddFileStore(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStoreOptions>(configuration.GetSection(FileStoreOptions.Section));
        services.AddScoped<IFileStore, LocalFileStore>();
    }

    private static void AddPdf(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PdfOptions>(configuration.GetSection(PdfOptions.Section));
        services.AddHttpClient<GotenbergConverter>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<PdfOptions>>().Value;
            http.BaseAddress = options.GotenbergUrl;
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddScoped<IPdfGenerator, PdfGenerator>();
        services.AddScoped<ILandlordProfileService, LandlordProfileService>();
    }

    /// <summary>
    /// Receipt extraction: the provider (None, Claude, Ollama) is chosen by <c>ReceiptExtraction:Provider</c>, no code change needed.
    /// Default is None: no document leaves the server unless it is switched on explicitly.
    /// </summary>
    private static void AddReceiptExtraction(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReceiptExtractionOptions>(configuration.GetSection(ReceiptExtractionOptions.Section));
        services.Configure<ReceiptInboxOptions>(configuration.GetSection(ReceiptInboxOptions.Section));
        services.AddHttpClient<ClaudeReceiptExtractor>().ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.AddHttpClient<OllamaReceiptExtractor>().ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<NoReceiptExtractor>();
        services.AddScoped<IReceiptExtractor>(sp => sp.GetRequiredService<IOptions<ReceiptExtractionOptions>>().Value.ProviderKind switch
        {
            ExtractionProvider.Claude => sp.GetRequiredService<ClaudeReceiptExtractor>(),
            ExtractionProvider.Ollama => sp.GetRequiredService<OllamaReceiptExtractor>(),
            _ => sp.GetRequiredService<NoReceiptExtractor>(),
        });
        services.AddScoped<IReceiptExtraction, ReceiptExtraction>();
    }

    private static void AddOperations(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StatementOptions>(configuration.GetSection(StatementOptions.Section));
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.Section));
        services.AddScoped<IBackupStatus, BackupStatus>();
        services.AddScoped<ITodoSource, BackupTodos>();
        services.AddScoped<BackupWarning>();
        services.AddScoped<IDataExport, DataExport>();
        services.AddScoped<Emergency.EmergencyFolderContent>();
        services.AddScoped<IEmergencyFolder, Emergency.EmergencyFolderService>();
        services.AddScoped<ITodoSource, Emergency.EmergencyFolderTodos>();
    }

    private static void AddMasterData(IServiceCollection services)
    {
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<INavigationContext, Portfolio.NavigationContextResolver>();
        services.AddScoped<IPropertyAssignments, PropertyAssignments>();
        services.AddScoped<IOccupancyLookup, LeaseOccupancy>();
        services.AddScoped<IPersonService, PersonService>();
        services.AddScoped<ILeaseService, LeaseService>();
        services.AddScoped<IPersonReference, LeasePersonalReferences>();
        services.AddScoped<IPersonalDataSource, LeasePersonalData>();
        services.AddScoped<ICpiService, CpiService>();
        services.AddScoped<RentStepApplication>();
        services.AddScoped<ITodoSource, RentModelTodos>();
        services.AddScoped<IMeterService, MeterService>();
        services.AddScoped<IReadingReports, ReadingReports>();
        services.AddScoped<ReadingReminders>();
        services.AddScoped<IMeterImport, MeterImport>();
        services.AddScoped<IReceiptImport, ReceiptImport>();
        services.AddScoped<IConsumptionInformation, ConsumptionInformation>();
        services.AddScoped<ConsumptionNotifications>();
        services.AddScoped<IDepreciationService, DepreciationService>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IAnlageVService, AnlageVService>();
        services.AddScoped<ITodoSource, CalibrationDueTodos>();
        services.AddScoped<IDocumentLibrary, DocumentLibrary>();
        services.AddScoped<IDocumentTemplates, DocumentTemplates>();
        services.AddScoped<IDocumentReadiness, DocumentReadiness>();
        services.AddScoped<ILetters, Letters>();
        services.AddScoped<IContractSignatures, ContractSignatures>();
        services.AddScoped<ILeaseContracts, LeaseContracts>();
        services.AddScoped<IRentIncreases, RentIncreases>();
        services.AddScoped<IHandovers, Handovers>();
        services.AddScoped<IDocumentLeaseSource, DocumentLeaseSource>();
        services.AddScoped<IFileShare, DocumentFileShare>();
        services.AddScoped<ITodoSource, DocumentExpiryTodos>();
        services.AddScoped<IPersonalDataSource, DocumentPersonalData>();
        services.AddScoped<ITenantAccounts, TenantAccounts>();
        services.AddSingleton<IModuleState, ModuleStateService>();
        services.AddScoped<IFinanceSettings, FinanceSettingsService>();
        services.AddScoped<ITodoSource, FinanceTodos>();
        services.AddScoped<IDunning, DunningService>();
        services.AddScoped<ITodoSource, DunningTodos>();
        services.AddScoped<IPersonalDataSource, DunningPersonalData>();
        services.AddScoped<IPersonalDataSource, TenantAccountPersonalData>();
        services.AddScoped<IBankService, BankService>();
        services.AddSingleton<FinTsSessions>();
        services.AddScoped<IFinTsService, FinTsService>();
        services.AddScoped<IPaymentAssignment, PaymentAssignment>();
        services.AddScoped<IMatchRules, MatchRules>();
        services.AddScoped<ITodoSource, BankTodos>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<ReceiptInbox>();
        services.AddScoped<ICostAllocation, CostAllocationService>();
        services.AddScoped<ISupplyContracts, SupplyContractService>();
        services.AddScoped<IOccupancyPlan, OccupancyPlanService>();
        services.AddScoped<ICostSubtypes, CostSubtypeService>();
        services.AddScoped<IOperatingCosts, OperatingCostService>();
        services.AddScoped<IHeatingSettings, HeatingSettingsService>();
        services.AddHttpClient<DegreeDayImporter>(http => http.Timeout = TimeSpan.FromSeconds(90));
        services.AddScoped<IStatements, StatementService>();
        services.AddScoped<IStatementDispatch, StatementDispatchService>();
        services.AddScoped<IStatementReceiptShares, StatementReceiptShareService>();
        services.AddScoped<ITenantStatements, TenantStatementService>();
        services.AddScoped<ITodoSource, StatementDeadlineTodos>();
        services.AddScoped<ITodoSource, ReceiptTodos>();
        services.AddScoped<IDeadlineSource, StatementDeadlineSource>();
        services.AddScoped<IDeadlineSource, LeaseDeadlineSource>();
        services.AddScoped<IDeadlineSource, LoanDeadlineSource>();
        services.AddScoped<IDeadlineSource, CalibrationDeadlineSource>();
        services.AddScoped<IDeadlineSource, DocumentDeadlineSource>();
        services.AddScoped<IDeadlineSource, TaskDeadlineSource>();
        services.AddScoped<DeadlineService>();
        services.AddScoped<IDeadlines>(sp => sp.GetRequiredService<DeadlineService>());
        services.AddScoped<DeadlineReminders>();
        services.AddScoped<ITenantPortal, TenantPortal>();
        services.AddScoped<IContactChangeRequests, ContactChangeRequests>();
        services.AddScoped<ITodoSource, ContactChangeTodos>();
        services.AddScoped<DocumentNotifications>();
        services.AddScoped<ITickets, TicketService>();
        services.AddScoped<IFileShare, TicketFileShare>();
        services.AddScoped<IPersonReference, TicketPersonReferences>();
        services.AddScoped<ITodoSource, TicketTodos>();
        services.AddScoped<IDatevExport, DatevExportService>();
        services.AddScoped<ITicketWork, TicketWorkService>();
        services.AddScoped<IContractorLinks, ContractorLinks>();
        services.AddScoped<ICareRoutines, CareRoutines>();
        services.AddScoped<IPropertyOwners, PropertyOwners>();
        services.AddScoped<IPropertyLandlords, PropertyLandlords>();
        services.AddScoped<IOwnerPortal, OwnerPortal>();
        services.AddScoped<IPersonReference, PropertyOwnerReferences>();
        services.AddScoped<ITodoSource, CareRoutineTodos>();
        services.AddScoped<INotices, NoticeService>();
        services.AddScoped<IMessages, MessageService>();
        services.AddScoped<IFileShare, NoticeFileShare>();
        services.AddScoped<ITodoSource, MessageTodos>();
        services.AddScoped<ITodoSource, NoticeAcknowledgementTodos>();
        services.AddScoped<NoticeNotifications>();
        services.AddScoped<ITodoSource, DeadlineTodos>();
        services.AddScoped<IPersonalDataSource, ReceiptPersonalData>();
    }

    private static void AddPrivacy(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PrivacyOptions>(configuration.GetSection(PrivacyOptions.Section));
        services.AddScoped<IPrivacyService, Privacy.Privacy>();
    }

    private static void AddHealthChecks(IServiceCollection services) =>
        services.AddHealthChecks()
            .AddDbContextCheck<ElocaroDbContext>("database", tags: [HealthTags.Ready])
            .AddCheck<GotenbergHealthCheck>("gotenberg", tags: [HealthTags.Ready])
            .AddCheck<FileStoreHealthCheck>("file-store", tags: [HealthTags.Ready])
            .AddCheck<BackupHealthCheck>("backup", tags: [HealthTags.Ready]);

    private static void AddJobs(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(JobsOptions.Section);
        services.Configure<JobsOptions>(section);
        var jobs = section.Get<JobsOptions>() ?? new JobsOptions();

        if (!jobs.Active)
        {
            services.AddScoped<IJobOverview, EmptyJobOverview>();
            return;
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
                s.UsePostgres(Connection(configuration));
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
            q.ScheduleDaily<BackupWarningJob>(BackupWarningJob.Key, "Warnt Admins per Mail, wenn die Datensicherung fehlgeschlagen oder veraltet ist", 8, 5);
        });
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        services.AddScoped<IJobOverview, QuartzJobOverview>();
    }

    private static string Connection(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException($"Verbindungszeichenfolge 'ConnectionStrings:{ConnectionName}' fehlt.");
        }

        return connection;
    }
}
