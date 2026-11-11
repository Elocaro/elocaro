using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class BackupTargets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "interval_days",
            table: "backup_settings",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "name",
            table: "backup_settings",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "Sicherung");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "interval_days",
            table: "backup_settings");

        migrationBuilder.DropColumn(
            name: "name",
            table: "backup_settings");
    }
}
