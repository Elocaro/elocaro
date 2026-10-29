using Npgsql;

namespace Elocaro.Infrastructure.Persistence;

/// <summary>Detects business-relevant PostgreSQL errors in an exception chain.</summary>
public static class DatabaseErrors
{
    /// <summary>
    /// Violation of an overlap protection (23P01). Depending on when the check happens, it arrives as
    /// <c>DbUpdateException</c> (immediately) or directly as <see cref="PostgresException"/> on commit.
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
