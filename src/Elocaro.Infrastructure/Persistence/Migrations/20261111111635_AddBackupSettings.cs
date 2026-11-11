using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBackupSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "backup_settings",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                protected_password = table.Column<string>(type: "text", nullable: false),
                protected_credentials = table.Column<string>(type: "text", nullable: true),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                keep_daily = table.Column<int>(type: "integer", nullable: false),
                keep_weekly = table.Column<int>(type: "integer", nullable: false),
                keep_monthly = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backup_settings", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "backup_settings");
    }
}
