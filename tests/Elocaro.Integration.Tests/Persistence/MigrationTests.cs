using Elocaro.Infrastructure;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
}
