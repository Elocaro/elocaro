using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MietHahn.Infrastructure.Identity;

namespace MietHahn.Infrastructure.Persistence;

public static partial class DatabaseMigration
{
    /// <summary>
    /// Wendet ausstehende Migrationen an und legt fehlende Rollen an, sofern
    /// <see cref="DatabaseOptions.AutoMigrate"/> gesetzt ist.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.AutoMigrate)
        {
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigration));
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();

        // Hinweis: Bei einer leeren DB protokolliert EF/Npgsql einmalig einen fehlgeschlagenen SELECT auf die
        // History-Tabelle (Level "fail"). Das ist erwartetes Verhalten und kein Fehler.
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            MigrationsAreApplied(logger, pending.Count, pending);
            await db.Database.MigrateAsync(cancellationToken);
        }

        await AccountService.EnsureRolesAsync(scope.ServiceProvider.GetRequiredService<RoleManager<Role>>());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wende {Count} Datenbank-Migration(en) an: {Migrations}")]
    private static partial void MigrationsAreApplied(ILogger logger, int count, IReadOnlyList<string> migrations);
}
