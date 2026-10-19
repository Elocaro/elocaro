using Npgsql;

namespace MietHahn.Infrastructure.Persistence;

/// <summary>Erkennt fachlich relevante PostgreSQL-Fehler in einer Ausnahmekette.</summary>
public static class DatabaseErrors
{
    /// <summary>
    /// Verstoß gegen einen Überlappungsschutz (23P01). Je nach Zeitpunkt der Prüfung kommt er als
    /// <c>DbUpdateException</c> (sofort) oder direkt als <see cref="PostgresException"/> beim Commit.
    /// </summary>
    public static bool IsOverlap(Exception errors) => SqlState(errors) == PostgresErrorCodes.ExclusionViolation;

    public static bool IsUniqueViolation(Exception errors) => SqlState(errors) == PostgresErrorCodes.UniqueViolation;

    private static string? SqlState(Exception? errors)
    {
        for (; errors is not null; errors = errors.InnerException)
        {
            if (errors is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }
}
