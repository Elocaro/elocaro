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

        // Close unused connections quickly so that finished tests do not hold connections.
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = name,
            ConnectionIdleLifetime = 2,
            ConnectionPruningInterval = 1,
        }.ConnectionString;
    }

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
