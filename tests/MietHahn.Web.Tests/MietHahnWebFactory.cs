using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Web.Tests;

/// <summary>
/// Startet die echte Web-App gegen eine eigene, frische Datenbank. Jobs sind aus (Mails bleiben in der Outbox
/// und werden dort geprüft), die Uhr ist steuerbar.
/// </summary>
public sealed class MietHahnWebFactory(string connection, IDictionary<string, string?>? supplement = null, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string FilePath { get; } = Path.Combine(Path.GetTempPath(), "miethahn-webtests", Guid.NewGuid().ToString("N"));

    public static async Task<MietHahnWebFactory> WithDatabaseAsync(
        PostgresFixture postgres, IDictionary<string, string?>? supplement = null, Action<IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        var factory = new MietHahnWebFactory(await postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken), supplement, configureServices);
        _ = factory.Server; // Host starten (Migrationen laufen dabei)
        return factory;
    }

    /// <summary>HTTP-Client wie ein Browser: Cookies an, Weiterleitungen manuell (zum Prüfen).</summary>
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
        builder.UseStaticWebAssets(); // blazor.web.js und app.css wie in der Entwicklung ausliefern
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
        if (Directory.Exists(FilePath))
        {
            Directory.Delete(FilePath, recursive: true);
        }
    }
}
