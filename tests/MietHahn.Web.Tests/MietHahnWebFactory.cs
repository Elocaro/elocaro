using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MietHahn.Application.Admin;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Web.Tests;

/// <summary>
/// Starts the real web app against a dedicated, fresh database. Jobs are off (mails stay in the outbox
/// and are verified there), the clock is controllable.
/// </summary>
public sealed class MietHahnWebFactory(
    string connection,
    IDictionary<string, string?>? supplement = null,
    Action<IServiceCollection>? configureServices = null,
    PostgresFixture? postgres = null)
    : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string FilePath { get; } = Path.Combine(Path.GetTempPath(), "miethahn-webtests", Guid.NewGuid().ToString("N"));

    public static async Task<MietHahnWebFactory> WithDatabaseAsync(
        PostgresFixture postgres, IDictionary<string, string?>? supplement = null, Action<IServiceCollection>? configureServices = null, bool setupCompleted = true)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        var factory = new MietHahnWebFactory(await postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken), supplement, configureServices, postgres);
        _ = factory.Server; // start the host (migrations run in the process)
        if (setupCompleted)
        {
            await factory.CompleteSetupAsync();
        }

        return factory;
    }

    /// <summary>Marks the setup wizard as done (all modules on), so that the dashboard does not lead to it.</summary>
    public async Task CompleteSetupAsync() =>
        await WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true);
            return true;
        });

    /// <summary>HTTP client like a browser: cookies on, redirects handled manually (for inspection).</summary>
    public TestBrowser Browser() => new(CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost/"),
    }));

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task WithDbAsync(Func<MietHahnDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<MietHahnDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseStaticWebAssets(); // serve blazor.web.js and app.css as in development
        builder.UseSetting("ConnectionStrings:MietHahn", connection);
        builder.UseSetting("Database:AutoMigrate", "true");
        builder.UseSetting("Jobs:Active", "false");
        builder.UseSetting("Application:BaseUrl", "https://localhost/");
        builder.UseSetting("SignIn:RequestsPerMinute", "1000");
        builder.UseSetting("FileStore:Path", FilePath);
        foreach (var (key, value) in supplement ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            configureServices?.Invoke(services);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (postgres is not null)
        {
            await postgres.DropDatabaseAsync(connection);
        }

        if (Directory.Exists(FilePath))
        {
            Directory.Delete(FilePath, recursive: true);
        }
    }
}
