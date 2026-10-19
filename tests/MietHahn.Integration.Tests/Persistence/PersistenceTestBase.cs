using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Persistence;

/// <summary>Stellt je Test eine eigene Datenbank mit dem <see cref="TestDbContext"/>-Schema bereit.</summary>
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

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    internal TestDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<MietHahnDbContext>();
        DependencyInjection.ConfigureNpgsql(options, connection);
        options.AddInterceptors(new EntitySaveInterceptor(Clock, User));
        return new TestDbContext(options.Options);
    }
}
