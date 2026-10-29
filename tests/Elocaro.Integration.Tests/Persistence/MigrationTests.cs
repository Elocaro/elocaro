using Elocaro.Infrastructure;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elocaro.Integration.Tests.Persistence;

public class MigrationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_creates_schema_in_empty_database()
    {
        await using var services = await ServicesAsync(autoMigrate: true);

        await services.MigrateDatabaseAsync(Ct);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        Assert.True(await ExtensionExistsAsync(db, "btree_gist"));
    }

    [Fact]
    public async Task Without_AutoMigrate_the_database_stays_empty()
    {
        await using var services = await ServicesAsync(autoMigrate: false);

        await services.MigrateDatabaseAsync(Ct);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync(Ct));
    }

    [Fact]
    public async Task Model_and_migrations_are_in_sync()
    {
        await using var services = await ServicesAsync(autoMigrate: true);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(), "Modelländerungen ohne Migration: 'dotnet ef migrations add <Name>' ausführen.");
    }

    [Fact]
    public void Missing_connection_string_is_reported_at_startup()
    {
        var configuration = new ConfigurationBuilder().Build();

        var errors = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddElocaroInfrastructure(configuration));
        Assert.Contains("ConnectionStrings:Elocaro", errors.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_connection_string_without_jobs_is_reported_on_first_access()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:Active"] = "false" })
            .Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddElocaroInfrastructure(configuration)
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var errors = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ElocaroDbContext>());
        Assert.Contains("ConnectionStrings:Elocaro", errors.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quartz_upgrade_keeps_existing_jobs_and_triggers_and_adds_the_version_4_schema()
    {
        // The state of an installation that still runs Quartz 3: the migration before the upgrade.
        const string before = "20261026164501_SupplyUnitMeters";
        await using var services = await ServicesAsync(autoMigrate: false);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(before, Ct);
        Assert.False(await ColumnExistsAsync(db, "qrtz_triggers", "retry_policy"));
        Assert.False(await TableExistsAsync(db, "qrtz_paused_job_grps"));

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO qrtz_job_details (sched_name, job_name, job_group, description, job_class_name, is_durable, is_nonconcurrent, is_update_data, requests_recovery) " +
            "VALUES ('Elocaro', 'charges', 'finance', 'Erzeugt die Monatsmieten', 'Elocaro.Infrastructure.Finance.ChargeJob, Elocaro.Infrastructure', true, true, false, false)",
            Ct);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO qrtz_triggers (sched_name, trigger_name, trigger_group, job_name, job_group, next_fire_time, prev_fire_time, priority, trigger_state, trigger_type, start_time, misfire_instr) " +
            "VALUES ('Elocaro', 'charges-trigger', 'finance', 'charges', 'finance', 1793000000000, 1792900000000, 5, 'WAITING', 'CRON', 1790000000000, 1)",
            Ct);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO qrtz_cron_triggers (sched_name, trigger_name, trigger_group, cron_expression, time_zone_id) VALUES ('Elocaro', 'charges-trigger', 'finance', '0 40 0 ? * *', 'Europe/Berlin')",
            Ct);

        await db.GetService<IMigrator>().MigrateAsync(null, Ct);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(1, await CountAsync(db, "qrtz_job_details"));
        Assert.Equal(1, await CountAsync(db, "qrtz_triggers"));
        Assert.Equal(1, await CountAsync(db, "qrtz_cron_triggers"));
        Assert.Equal(1793000000000L, await db.Database.SqlQuery<long>($"SELECT next_fire_time AS \"Value\" FROM qrtz_triggers WHERE trigger_name = 'charges-trigger'").SingleAsync(Ct));
        Assert.True(await ColumnExistsAsync(db, "qrtz_triggers", "retry_policy"));
        Assert.True(await ColumnExistsAsync(db, "qrtz_triggers", "misfire_threshold"));
        Assert.True(await TableExistsAsync(db, "qrtz_paused_job_grps"));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM qrtz_triggers WHERE retry_policy IS NOT NULL").SingleAsync(Ct));
    }

    private async Task<ServiceProvider> ServicesAsync(bool autoMigrate)
    {
        var connection = await postgres.CreateDatabaseAsync(Ct);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Elocaro"] = connection,
                ["Database:AutoMigrate"] = autoMigrate.ToString(),
                ["Jobs:Active"] = "false",
            })
            .Build();

        return new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddElocaroInfrastructure(configuration)
            .BuildServiceProvider();
    }

    private static async Task<bool> ExtensionExistsAsync(ElocaroDbContext db, string name) =>
        await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname = {name}").SingleAsync(Ct) == 1;

    private static async Task<bool> ColumnExistsAsync(ElocaroDbContext db, string table, string column) =>
        await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = {table} AND column_name = {column}").SingleAsync(Ct) == 1;

    private static async Task<bool> TableExistsAsync(ElocaroDbContext db, string table) =>
        await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = {table}").SingleAsync(Ct) == 1;

    private static async Task<int> CountAsync(ElocaroDbContext db, string table)
    {
        // Only fixed table names of this test are passed in; a table name cannot be a parameter.
#pragma warning disable EF1003
        return await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM " + table).SingleAsync(Ct);
#pragma warning restore EF1003
    }
}
