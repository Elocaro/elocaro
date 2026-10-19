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
}
