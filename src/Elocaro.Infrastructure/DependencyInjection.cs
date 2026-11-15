using Elocaro.Application.Common;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Admin;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Bank;
using Elocaro.Infrastructure.Bank.FinTs;
using Elocaro.Infrastructure.Correspondence;
using Elocaro.Infrastructure.Datev;
using Elocaro.Infrastructure.Deadlines;
using Elocaro.Infrastructure.Documents;
using Elocaro.Infrastructure.Emergency;
using Elocaro.Infrastructure.Files;
using Elocaro.Infrastructure.Finance;
using Elocaro.Infrastructure.Handover;
using Elocaro.Infrastructure.Heating;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Jobs;
using Elocaro.Infrastructure.Leases;
using Elocaro.Infrastructure.Legal;
using Elocaro.Infrastructure.Mail;
using Elocaro.Infrastructure.Metering;
using Elocaro.Infrastructure.Notices;
using Elocaro.Infrastructure.Notifications;
using Elocaro.Infrastructure.OperatingCosts;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Pdf;
using Elocaro.Infrastructure.People;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Portal;
using Elocaro.Infrastructure.Portfolio;
using Elocaro.Infrastructure.Privacy;
using Elocaro.Infrastructure.Properties;
using Elocaro.Infrastructure.Receipts;
using Elocaro.Infrastructure.Setup;
using Elocaro.Infrastructure.Statements;
using Elocaro.Infrastructure.Tax;
using Elocaro.Infrastructure.Templates;
using Elocaro.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elocaro.Infrastructure;

/// <summary>
/// Entry point of the registrations. Each module registers its own services in its folder (<c>&lt;Module&gt;/&lt;Module&gt;Registration.cs</c>);
/// this class only calls them and holds what belongs to no single module (database, health checks).
/// </summary>
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
        services.Configure<DegreeDayOptions>(configuration.GetSection(DegreeDayOptions.Section));
        services.Configure<UpdateCheckOptions>(configuration.GetSection(UpdateCheckOptions.Section));

        AddPersistence(services, configuration);

        // Extension points (IMeterUsage, ITodoSource, ...) are resolved as lists in the order of registration, which is the order of these calls:
        // a consumer that shows them to the user (blocked deletion of a meter) lists the modules in this order.
        services.AddIdentityModule(configuration);
        services.AddAuditModule(configuration);
        services.AddPdfModule(configuration);
        services.AddReceiptsModule(configuration);
        services.AddOperationsModule(configuration);
        services.AddPrivacyModule(configuration);
        services.AddJobsModule(configuration);
        services.AddMailModule(configuration);
        services.AddTemplatesModule();
        services.AddNotificationsModule(configuration);
        services.AddSetupModule();
        services.AddLeasesModule();
        services.AddPropertiesModule();
        services.AddFinanceModule();
        services.AddTaxModule();
        services.AddDatevModule();
        services.AddLegalModule();
        services.AddPortfolioModule();
        services.AddEmergencyModule();
        services.AddPeopleModule();
        services.AddMeteringModule();
        services.AddDocumentsModule();
        services.AddCorrespondenceModule();
        services.AddHandoverModule();
        services.AddAdminModule();
        services.AddBankModule();
        services.AddHeatingModule();
        services.AddOperatingCostsModule();
        services.AddStatementsModule();
        services.AddDeadlinesModule();
        services.AddPortalModule();
        services.AddTicketsModule();
        services.AddNoticesModule();
        services.AddFilesModule(configuration);
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

    private static void AddHealthChecks(IServiceCollection services) =>
        services.AddHealthChecks()
            .AddDbContextCheck<ElocaroDbContext>("database", tags: [HealthTags.Ready])
            .AddCheck<GotenbergHealthCheck>("gotenberg", tags: [HealthTags.Ready])
            .AddCheck<FileStoreHealthCheck>("file-store", tags: [HealthTags.Ready])
            .AddCheck<BackupHealthCheck>("backup", tags: [HealthTags.Ready]);

    internal static string Connection(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException($"Verbindungszeichenfolge 'ConnectionStrings:{ConnectionName}' fehlt.");
        }

        return connection;
    }
}
