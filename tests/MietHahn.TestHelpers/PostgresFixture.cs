using Npgsql;
using Testcontainers.PostgreSql;

namespace MietHahn.TestHelpers;

/// <summary>
/// Ein PostgreSQL-Container für den gesamten Testlauf. Jeder Test legt sich mit
/// <see cref="CreateDatabaseAsync"/> eine eigene, leere Datenbank an und ist damit isoliert.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18-alpine";

    // Viele parallele Tests mit je eigener Datenbank und eigenem Verbindungspool: mehr Verbindungen als der Standard (100).
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).WithCommand("-c", "max_connections=300").Build();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    /// <summary>Erzeugt eine neue Datenbank und liefert deren Verbindungszeichenfolge.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var name = "test_" + Guid.NewGuid().ToString("N");

        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Ungenutzte Verbindungen schnell schließen, damit beendete Tests keine Verbindungen blockieren.
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = name,
            ConnectionIdleLifetime = 2,
            ConnectionPruningInterval = 1,
        }.ConnectionString;
    }
}
