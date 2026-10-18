using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MietHahn.Infrastructure.Persistenz;

public static partial class DatenbankMigration
{
    /// <summary>Wendet ausstehende Migrationen an, sofern <see cref="DatenbankOptionen.AutomatischMigrieren"/> gesetzt ist.</summary>
    public static async Task MigriereDatenbankAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var optionen = scope.ServiceProvider.GetRequiredService<IOptions<DatenbankOptionen>>().Value;
        if (!optionen.AutomatischMigrieren)
        {
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatenbankMigration));
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();

        // Hinweis: Bei einer leeren DB protokolliert EF/Npgsql einmalig einen fehlgeschlagenen SELECT auf die
        // History-Tabelle (Level "fail"). Das ist erwartetes Verhalten und kein Fehler.
        var ausstehend = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (ausstehend.Count == 0)
        {
            return;
        }

        MigrationenWerdenAngewendet(logger, ausstehend.Count, ausstehend);
        await db.Database.MigrateAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wende {Anzahl} Datenbank-Migration(en) an: {Migrationen}")]
    private static partial void MigrationenWerdenAngewendet(ILogger logger, int anzahl, IReadOnlyList<string> migrationen);
}
