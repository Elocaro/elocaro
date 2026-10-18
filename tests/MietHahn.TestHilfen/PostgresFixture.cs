using Npgsql;
using Testcontainers.PostgreSql;

namespace MietHahn.TestHilfen;

/// <summary>
/// Ein PostgreSQL-Container für den gesamten Testlauf. Jeder Test legt sich mit
/// <see cref="NeueDatenbankAsync"/> eine eigene, leere Datenbank an und ist damit isoliert.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18-alpine";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    /// <summary>Erzeugt eine neue Datenbank und liefert deren Verbindungszeichenfolge.</summary>
    public async Task<string> NeueDatenbankAsync(CancellationToken cancellationToken = default)
    {
        var name = "test_" + Guid.NewGuid().ToString("N");

        await using (var verbindung = new NpgsqlConnection(container.GetConnectionString()))
        {
            await verbindung.OpenAsync(cancellationToken);
            await using var befehl = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", verbindung);
            await befehl.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
