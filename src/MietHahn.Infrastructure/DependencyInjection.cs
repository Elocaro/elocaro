using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Audit;
using MietHahn.Application.Bank;
using MietHahn.Application.Common;
using MietHahn.Application.Communication;
using MietHahn.Application.Deadlines;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Finance;
using MietHahn.Application.Heating;
using MietHahn.Application.Leases;
using MietHahn.Application.Metering;
using MietHahn.Application.Notices;
using MietHahn.Application.OperatingCosts;
using MietHahn.Application.Operations;
using MietHahn.Application.People;
using MietHahn.Application.Portal;
using MietHahn.Application.Privacy;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Application.Statements;
using MietHahn.Application.Tax;
using MietHahn.Application.Tickets;
using MietHahn.Application.Todos;
using MietHahn.Infrastructure.Admin;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Bank;
using MietHahn.Infrastructure.Bank.FinTs;
using MietHahn.Infrastructure.Deadlines;
using MietHahn.Infrastructure.Documents;
using MietHahn.Infrastructure.Files;
using MietHahn.Infrastructure.Finance;
using MietHahn.Infrastructure.Heating;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Jobs;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Mail;
using MietHahn.Infrastructure.Metering;
using MietHahn.Infrastructure.Notices;
using MietHahn.Infrastructure.OperatingCosts;
using MietHahn.Infrastructure.Operations;
using MietHahn.Infrastructure.Pdf;
using MietHahn.Infrastructure.People;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Portal;
using MietHahn.Infrastructure.Privacy;
using MietHahn.Infrastructure.Properties;
using MietHahn.Infrastructure.Receipts;
using MietHahn.Infrastructure.Receipts.Extraction;
using MietHahn.Infrastructure.Statements;
using MietHahn.Infrastructure.Tax;
using MietHahn.Infrastructure.Templates;
using MietHahn.Infrastructure.Tickets;
using Quartz;

namespace MietHahn.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionName = "MietHahn";

    /// <summary>Registers persistence and infrastructure services (mail, file store, background jobs).</summary>
    public static IServiceCollection AddMietHahnInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, SystemUser>();

        services.Configure<ApplicationOptions>(configuration.GetSection(ApplicationOptions.Section));
        services.Configure<FinTsOptions>(configuration.GetSection(FinTsOptions.Section));

        AddPersistence(services, configuration);
        AddIdentity(services);
        AddAudit(services, configuration);
        AddMail(services, configuration);
        AddFileStore(services, configuration);
        AddPdf(services, configuration);
        AddReceiptExtraction(services, configuration);
        AddOperations(services, configuration);
        AddMasterData(services);
        AddPrivacy(services, configuration);
        AddJobs(services, configuration);
        AddHealthChecks(services);

        return services;
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, string connection) =>
        options
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations"))
            .UseSnakeCaseNamingConvention();

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.AddScoped<EntitySaveInterceptor>();

        services.AddDbContext<MietHahnDbContext>((sp, options) =>
        {
            ConfigureNpgsql(options, Connection(configuration));
            options.AddInterceptors(sp.GetRequiredService<EntitySaveInterceptor>());
        });
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddDataProtection()
            .SetApplicationName("MietHahn")
            .PersistKeysToDbContext<MietHahnDbContext>();

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
            .AddEntityFrameworkStores<MietHahnDbContext>()
            .AddSignInManager<MietHahnSignInManager>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InvitationTokenProvider>(TokenProviders.Invitation)
            .AddClaimsPrincipalFactory<MietHahnClaimsFactory>()
            .AddErrorDescriber<GermanIdentityErrorDescriber>();

        // Password reset links: 2 hours; invitations use their own provider (7 days).
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.AddScoped<IAccountService, AccountService>();
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
        services.TryAddSingleton<ScribanRenderer>();
        services.AddScoped<IMailSender, MailSender>();
        services.TryAddScoped<ISmtpSender, MailKitSmtpSender>();
        services.AddScoped<MailOutbox>();
        services.AddScoped<IMailService, MailService>();
        services.AddScoped<ITodoSource, MailTodos>();
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
    }

    private static void AddMasterData(IServiceCollection services)
    {
        services.AddScoped<IPropertyService, PropertyService>();
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
        services.AddScoped<IDepreciationService, DepreciationService>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IAnlageVService, AnlageVService>();
        services.AddScoped<ITodoSource, CalibrationDueTodos>();
        services.AddScoped<IDocumentLibrary, DocumentLibrary>();
        services.AddScoped<IDocumentTemplates, DocumentTemplates>();
        services.AddScoped<ILetters, Letters>();
        services.AddScoped<ILeaseContracts, LeaseContracts>();
        services.AddScoped<IRentIncreases, RentIncreases>();
        services.AddScoped<IHandovers, Handovers>();
        services.AddScoped<IFileShare, DocumentFileShare>();
        services.AddScoped<ITodoSource, DocumentExpiryTodos>();
        services.AddScoped<IPersonalDataSource, DocumentPersonalData>();
        services.AddScoped<ITenantAccounts, TenantAccounts>();
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
        services.AddScoped<ICostAllocation, CostAllocationService>();
        services.AddScoped<ICostSubtypes, CostSubtypeService>();
        services.AddScoped<IOperatingCosts, OperatingCostService>();
        services.AddScoped<IHeatingSettings, HeatingSettingsService>();
        services.AddScoped<IStatements, StatementService>();
        services.AddScoped<IStatementDispatch, StatementDispatchService>();
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
        services.AddScoped<ITicketWork, TicketWorkService>();
        services.AddScoped<IContractorLinks, ContractorLinks>();
        services.AddScoped<ICareRoutines, CareRoutines>();
        services.AddScoped<INotices, NoticeService>();
        services.AddScoped<IMessages, MessageService>();
        services.AddScoped<IFileShare, NoticeFileShare>();
        services.AddScoped<ITodoSource, MessageTodos>();
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
            .AddDbContextCheck<MietHahnDbContext>("database", tags: [HealthTags.Ready])
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
        services.AddQuartz(q =>
        {
            q.SchedulerName = "MietHahn";
            q.SchedulerId = "AUTO";
            q.UsePersistentStore(s =>
            {
                s.UsePostgres(Connection(configuration));
                s.UseSystemTextJsonSerializer();
                s.PerformSchemaValidation = true;
            });
            q.AddJobListener<JobRunLog>();

            q.ScheduleInterval<MailOutboxJob>(MailOutboxJob.Key, "Versendet fällige E-Mails aus der Outbox", TimeSpan.FromSeconds(30));
            q.ScheduleDaily<JobLogCleanupJob>(JobLogCleanupJob.Key, "Löscht alte Einträge des Job-Laufprotokolls", 3, 15);
            q.ScheduleDaily<AuditCleanupJob>(AuditCleanupJob.Key, "Löscht Audit-Einträge nach Ablauf der Aufbewahrungsfrist", 3, 30);
            q.ScheduleDaily<RentStepJob>(RentStepJob.Key, "Übernimmt fällige Staffeln als Kaltmiete und informiert die Admins", 0, 30);
            q.ScheduleDaily<ChargeJob>(ChargeJob.Key, "Erzeugt die Monatsmieten (Sollstellungen) und gleicht sie an Vertragsänderungen an", 0, 40);
            q.ScheduleDaily<DunningJob>(DunningJob.Key, "Erkennt Rückstände und schlägt Mahnungen vor (versendet nichts, Freigabe nötig)", 6, 15);
            q.ScheduleDaily<DeadlineReminderJob>(DeadlineReminderJob.Key, "Schickt Erinnerungen zu Fristen und Wartungen und montags die Wochenübersicht", 7, 30);
            q.ScheduleInterval<DocumentNotificationJob>(DocumentNotificationJob.Key, "Informiert Mieter per Mail über neue Dokumente im Portal", TimeSpan.FromMinutes(10));
            q.ScheduleInterval<NoticeNotificationJob>(NoticeNotificationJob.Key, "Informiert Mieter per Mail über neue Aushänge im Portal", TimeSpan.FromMinutes(10));
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
