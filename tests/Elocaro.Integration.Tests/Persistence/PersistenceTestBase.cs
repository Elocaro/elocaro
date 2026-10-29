using Elocaro.Infrastructure;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Elocaro.Integration.Tests.Persistence;

/// <summary>Provides a dedicated database per test with the <see cref="TestDbContext"/> schema.</summary>
public abstract class PersistenceTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected static readonly DateTimeOffset StartTime = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

    private string connection = "";

    internal FakeTimeProvider Clock { get; } = new(StartTime);

    internal TestUser User { get; } = new();

    protected string Connection => connection;

    public virtual async ValueTask InitializeAsync()
    {
        connection = await postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (connection.Length > 0)
        {
            await postgres.DropDatabaseAsync(connection);
        }
    }

    internal TestDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ElocaroDbContext>();
        DependencyInjection.ConfigureNpgsql(options, connection);
        options.AddInterceptors(new EntitySaveInterceptor(Clock, User));
        return new TestDbContext(options.Options);
    }
}
