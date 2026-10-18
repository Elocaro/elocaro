using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Integration.Tests.Infrastruktur;

/// <summary>
/// Vollständig registrierte Infrastruktur gegen eine eigene, migrierte Datenbank, mit temporärer Dateiablage
/// und steuerbarer Uhr. Jobs (Quartz) sind standardmäßig aus.
/// </summary>
public sealed class InfrastrukturUmgebung : IAsyncDisposable
{
    public static readonly DateTimeOffset Startzeit = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

    private InfrastrukturUmgebung(ServiceProvider services, FakeTimeProvider uhr, string dateiPfad, string verbindung)
    {
        Services = services;
        Uhr = uhr;
        DateiPfad = dateiPfad;
        Verbindung = verbindung;
    }

    public ServiceProvider Services { get; }

    public FakeTimeProvider Uhr { get; }

    public string DateiPfad { get; }

    public string Verbindung { get; }

    public static async Task<InfrastrukturUmgebung> ErstellenAsync(
        PostgresFixture postgres,
        IDictionary<string, string?>? konfiguration = null,
        Action<IServiceCollection>? anpassen = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var verbindung = await postgres.NeueDatenbankAsync(ct);
        var dateiPfad = Path.Combine(Path.GetTempPath(), "miethahn-tests", Guid.NewGuid().ToString("N"));

        var werte = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MietHahn"] = verbindung,
            ["Jobs:Aktiv"] = "false",
            ["Dateiablage:Pfad"] = dateiPfad,
        };
        foreach (var (schluessel, wert) in konfiguration ?? new Dictionary<string, string?>())
        {
            werte[schluessel] = wert;
        }

        var uhr = new FakeTimeProvider(Startzeit);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(uhr);
        services.AddMietHahnInfrastruktur(new ConfigurationBuilder().AddInMemoryCollection(werte).Build());
        anpassen?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.MigriereDatenbankAsync(ct);
        return new InfrastrukturUmgebung(provider, uhr, dateiPfad, verbindung);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (Directory.Exists(DateiPfad))
        {
            Directory.Delete(DateiPfad, recursive: true);
        }
    }
}
