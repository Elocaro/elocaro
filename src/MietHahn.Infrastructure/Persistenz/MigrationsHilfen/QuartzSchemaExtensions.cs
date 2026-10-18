using Microsoft.EntityFrameworkCore.Migrations;
using MietHahn.Infrastructure.Vorlagen;

namespace MietHahn.Infrastructure.Persistenz.MigrationsHilfen;

/// <summary>Legt die Tabellen des Quartz-Job-Stores an bzw. entfernt sie (Quartz.NET 3.x, PostgreSQL).</summary>
public static class QuartzSchemaExtensions
{
    private const string Skript = "Persistenz/Sql/quartz_3_22_6_postgres.sql";

    private static readonly string[] Tabellen =
    [
        "qrtz_fired_triggers", "qrtz_paused_trigger_grps", "qrtz_scheduler_state", "qrtz_locks",
        "qrtz_simprop_triggers", "qrtz_simple_triggers", "qrtz_cron_triggers", "qrtz_blob_triggers",
        "qrtz_triggers", "qrtz_job_details", "qrtz_calendars",
    ];

    public static void ErstelleQuartzSchema(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(EingebetteteRessource.LesenText(Skript));
    }

    public static void EntferneQuartzSchema(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        foreach (var tabelle in Tabellen)
        {
            migrationBuilder.Sql($"DROP TABLE IF EXISTS {tabelle};");
        }
    }
}
