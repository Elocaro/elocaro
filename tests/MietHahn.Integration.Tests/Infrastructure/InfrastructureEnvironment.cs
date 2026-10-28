using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Integration.Tests.Infrastructure;

/// <summary>
/// Fully registered infrastructure against a dedicated, migrated database, with temporary file storage
/// and a controllable clock. Jobs (Quartz) are off by default.
/// </summary>
public sealed class InfrastructureEnvironment : IAsyncDisposable
{
    public static readonly DateTimeOffset StartTime = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

    private readonly PostgresFixture postgres;

    private InfrastructureEnvironment(PostgresFixture postgres, ServiceProvider services, FakeTimeProvider clock, string filePath, string connection)
    {
        this.postgres = postgres;
        Services = services;
        Clock = clock;
        FilePath = filePath;
        Connection = connection;
    }

    public ServiceProvider Services { get; }

    public FakeTimeProvider Clock { get; }

    public string FilePath { get; }

    public string Connection { get; }

    public static async Task<InfrastructureEnvironment> CreateAsync(
        PostgresFixture postgres,
        IDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configure = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateMigratedDatabaseAsync(ct);
        var filePath = Path.Combine(Path.GetTempPath(), "miethahn-tests", Guid.NewGuid().ToString("N"));

        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MietHahn"] = connection,
            ["Jobs:Active"] = "false",
            ["FileStore:Path"] = filePath,
        };
        foreach (var (key, value) in configuration ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var clock = new FakeTimeProvider(StartTime);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(clock);
        services.AddMietHahnInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.MigrateDatabaseAsync(ct);
        return new InfrastructureEnvironment(postgres, provider, clock, filePath, connection);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await postgres.DropDatabaseAsync(Connection);
        if (Directory.Exists(FilePath))
        {
            Directory.Delete(FilePath, recursive: true);
        }
    }
}
