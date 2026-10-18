using Npgsql;

namespace MietHahn.Infrastructure.Persistenz;

/// <summary>Erkennt fachlich relevante PostgreSQL-Fehler in einer Ausnahmekette.</summary>
public static class Datenbankfehler
{
    /// <summary>
    /// Verstoß gegen einen Überlappungsschutz (23P01). Je nach Zeitpunkt der Prüfung kommt er als
    /// <c>DbUpdateException</c> (sofort) oder direkt als <see cref="PostgresException"/> beim Commit.
    /// </summary>
    public static bool IstUeberlappung(Exception fehler) => SqlState(fehler) == PostgresErrorCodes.ExclusionViolation;

    public static bool IstEindeutigkeitsverletzung(Exception fehler) => SqlState(fehler) == PostgresErrorCodes.UniqueViolation;

    private static string? SqlState(Exception? fehler)
    {
        for (; fehler is not null; fehler = fehler.InnerException)
        {
            if (fehler is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }
}
