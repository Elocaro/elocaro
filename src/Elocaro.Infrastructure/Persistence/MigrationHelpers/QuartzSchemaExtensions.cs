using Elocaro.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elocaro.Infrastructure.Persistence.MigrationHelpers;

/// <summary>Creates, upgrades or removes the tables of the Quartz job store (PostgreSQL).</summary>
public static class QuartzSchemaExtensions
{
    private const string Script = "Persistence/Sql/quartz_3_22_6_postgres.sql";
    private const string UpgradeScript = "Persistence/Sql/quartz_4_6_upgrade_postgres.sql";
    private const string IndexScript = "Persistence/Sql/quartz_4_0_indexes_postgres.sql";

    private static readonly string[] Tables =
    [
        "qrtz_fired_triggers", "qrtz_paused_trigger_grps", "qrtz_scheduler_state", "qrtz_locks",
        "qrtz_simprop_triggers", "qrtz_simple_triggers", "qrtz_cron_triggers", "qrtz_blob_triggers",
        "qrtz_triggers", "qrtz_job_details", "qrtz_calendars",
    ];

    public static void CreateQuartzSchema(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(EmbeddedResource.ReadText(Script));
    }

    /// <summary>
    /// Upgrades a 3.22 schema to the layout Quartz.NET 4.6 validates at startup: the official migration scripts (new columns,
    /// the table of paused job groups), then the index set of a fresh 4.x installation. All statements are repeatable; meant
    /// for a single node that is not running while the migration runs.
    /// </summary>
    public static void UpgradeQuartzSchemaTo46(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(EmbeddedResource.ReadText(UpgradeScript));
        migrationBuilder.Sql(EmbeddedResource.ReadText(IndexScript));
    }

    /// <summary>Removes what the 4.6 upgrade added (columns and tables); the index set is left as it is.</summary>
    public static void DowngradeQuartzSchemaFrom46(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("DROP TABLE IF EXISTS qrtz_paused_job_grps;");
        migrationBuilder.Sql("ALTER TABLE qrtz_triggers DROP COLUMN IF EXISTS misfire_orig_fire_time, DROP COLUMN IF EXISTS execution_group, DROP COLUMN IF EXISTS preferred_node, DROP COLUMN IF EXISTS preferred_node_auto, DROP COLUMN IF EXISTS retry_policy, DROP COLUMN IF EXISTS retry_attempt, DROP COLUMN IF EXISTS continues_trigger_name, DROP COLUMN IF EXISTS continues_trigger_group, DROP COLUMN IF EXISTS continuation_condition, DROP COLUMN IF EXISTS overlap_policy, DROP COLUMN IF EXISTS pause_reason, DROP COLUMN IF EXISTS paused_by, DROP COLUMN IF EXISTS paused_at, DROP COLUMN IF EXISTS misfire_threshold;");
        migrationBuilder.Sql("ALTER TABLE qrtz_fired_triggers DROP COLUMN IF EXISTS execution_group, DROP COLUMN IF EXISTS progress, DROP COLUMN IF EXISTS progress_message, DROP COLUMN IF EXISTS interrupt_requested_time;");
        migrationBuilder.Sql("ALTER TABLE qrtz_paused_trigger_grps DROP COLUMN IF EXISTS pause_reason, DROP COLUMN IF EXISTS paused_by, DROP COLUMN IF EXISTS paused_at;");
        migrationBuilder.Sql("ALTER TABLE qrtz_scheduler_state DROP COLUMN IF EXISTS quartz_version;");
    }

    public static void DropQuartzSchema(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"DROP TABLE IF EXISTS {table};");
        }
    }
}
