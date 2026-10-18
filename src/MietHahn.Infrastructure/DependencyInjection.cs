using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MietHahn.Application.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure;

public static class DependencyInjection
{
    public const string VerbindungsName = "MietHahn";

    /// <summary>Registriert Persistenz und Infrastrukturdienste.</summary>
    public static IServiceCollection AddMietHahnInfrastruktur(this IServiceCollection services, IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);

        services.Configure<DatenbankOptionen>(konfiguration.GetSection(DatenbankOptionen.Abschnitt));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAktuellerBenutzer, SystemBenutzer>();
        services.AddScoped<EntitaetSpeichernInterceptor>();

        services.AddDbContext<MietHahnDbContext>((sp, optionen) =>
        {
            var verbindung = konfiguration.GetConnectionString(VerbindungsName);
            if (string.IsNullOrWhiteSpace(verbindung))
            {
                throw new InvalidOperationException($"Verbindungszeichenfolge 'ConnectionStrings:{VerbindungsName}' fehlt.");
            }

            KonfiguriereNpgsql(optionen, verbindung);
            optionen.AddInterceptors(sp.GetRequiredService<EntitaetSpeichernInterceptor>());
        });

        return services;
    }

    internal static void KonfiguriereNpgsql(DbContextOptionsBuilder optionen, string verbindung) =>
        optionen
            .UseNpgsql(verbindung, npgsql => npgsql.MigrationsHistoryTable("__ef_migrationen"))
            .UseSnakeCaseNamingConvention();
}
