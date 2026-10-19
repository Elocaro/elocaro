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
    /// Applies pending migrations and creates missing roles, provided
    /// <see cref="DatabaseOptions.AutoMigrate"/> is set.
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

        // Note: with an empty DB, EF/Npgsql logs a single failed SELECT on the
        // history table (level "fail"). This is expected behavior and not an error.
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
