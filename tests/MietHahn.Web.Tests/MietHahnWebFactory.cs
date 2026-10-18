using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Web.Tests;

/// <summary>
/// Startet die echte Web-App gegen eine eigene, frische Datenbank. Jobs sind aus (Mails bleiben in der Outbox
/// und werden dort geprüft), die Uhr ist steuerbar.
/// </summary>
public sealed class MietHahnWebFactory(string verbindung, IDictionary<string, string?>? zusatz = null) : WebApplicationFactory<Program>
{
    public FakeTimeProvider Uhr { get; } = new(DateTimeOffset.UtcNow);

    public string DateiPfad { get; } = Path.Combine(Path.GetTempPath(), "miethahn-webtests", Guid.NewGuid().ToString("N"));

    public static async Task<MietHahnWebFactory> MitDatenbankAsync(PostgresFixture postgres, IDictionary<string, string?>? zusatz = null)
    {
        var factory = new MietHahnWebFactory(await postgres.NeueDatenbankAsync(TestContext.Current.CancellationToken), zusatz);
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

    public async Task<T> MitScopeAsync<T>(Func<IServiceProvider, Task<T>> aktion)
    {
        ArgumentNullException.ThrowIfNull(aktion);
        await using var scope = Services.CreateAsyncScope();
        return await aktion(scope.ServiceProvider);
    }

    public async Task MitDbAsync(Func<MietHahnDbContext, Task> aktion)
    {
        ArgumentNullException.ThrowIfNull(aktion);
        await using var scope = Services.CreateAsyncScope();
        await aktion(scope.ServiceProvider.GetRequiredService<MietHahnDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:MietHahn", verbindung);
        builder.UseSetting("Datenbank:AutomatischMigrieren", "true");
        builder.UseSetting("Jobs:Aktiv", "false");
        builder.UseSetting("Anwendung:BasisUrl", "https://localhost/");
        builder.UseSetting("Anmeldung:AnfragenProMinute", "1000");
        builder.UseSetting("Dateiablage:Pfad", DateiPfad);
        foreach (var (schluessel, wert) in zusatz ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(schluessel, wert);
        }

        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Uhr));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(DateiPfad))
        {
            Directory.Delete(DateiPfad, recursive: true);
        }
    }
}
