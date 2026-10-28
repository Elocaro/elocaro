using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MietHahn.TestHelpers;

/// <summary>
/// A PostgreSQL container for the entire test run. Each test creates its own empty database with
/// <see cref="CreateDatabaseAsync"/> and is thereby isolated.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18-alpine";

    // Many parallel tests, each with its own database and connection pool: more connections than the default (100).
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).WithCommand("-c", "max_connections=300").Build();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    /// <summary>Creates a new database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var name = "test_" + Guid.NewGuid().ToString("N");

        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return ConnectionString(name);
    }

    private readonly SemaphoreSlim templateLock = new(1, 1);
    private string? template;

    /// <summary>
    /// Creates a new database with all migrations and the roles already applied. The (about a hundred) migrations run once per
    /// test run into a template database; every further database is a file copy of it, which is far quicker than migrating each time.
    /// </summary>
    public async Task<string> CreateMigratedDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await templateLock.WaitAsync(cancellationToken);
        try
        {
            template ??= await BuildTemplateAsync(cancellationToken);
            var name = "test_" + Guid.NewGuid().ToString("N");
            await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\"", cancellationToken);
            return ConnectionString(name);
        }
        finally
        {
            templateLock.Release();
        }
    }

    private async Task<string> BuildTemplateAsync(CancellationToken cancellationToken)
    {
        var name = "template_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync($"CREATE DATABASE \"{name}\"", cancellationToken);
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MietHahn"] = ConnectionString(name),
            ["Jobs:Active"] = "false",
            ["FileStore:Path"] = Path.Combine(Path.GetTempPath(), "miethahn-template", Guid.NewGuid().ToString("N")),
        };
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddMietHahnInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        await using (var provider = services.BuildServiceProvider())
        {
            await provider.MigrateDatabaseAsync(cancellationToken);
        }

        // A template must have no open connections.
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}'", cancellationToken);
        return name;
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = database,
            ConnectionIdleLifetime = 2,
            ConnectionPruningInterval = 1,
        }.ConnectionString;

    /// <summary>
    /// Drops a database created by <see cref="CreateDatabaseAsync"/>. Without this the schemas of thousands of tests
    /// add up and fill the disk of the CI runner. Best effort: cleanup must never fail a test.
    /// </summary>
    public async Task DropDatabaseAsync(string connectionString)
    {
        try
        {
            var name = new NpgsqlConnectionStringBuilder(connectionString).Database;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            await using var connection = new NpgsqlConnection(container.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            // The database is gone with the container at the latest.
        }
    }
}
