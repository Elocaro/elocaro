using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261116081646_Prospects : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The module keeps personal data of strangers: it is off in existing installations until the landlord switches it on.
        migrationBuilder.Sql("UPDATE module_settings SET disabled_modules = array_append(disabled_modules, 'Prospects') WHERE NOT ('Prospects' = ANY(disabled_modules));");

        migrationBuilder.CreateTable(
            name: "prospects",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rental_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                viewing_on = table.Column<DateOnly>(type: "date", nullable: true),
                notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                delete_on = table.Column<DateOnly>(type: "date", nullable: true),
                person_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_prospects", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_prospects_delete_on",
            table: "prospects",
            column: "delete_on");

        migrationBuilder.CreateIndex(
            name: "ix_prospects_rental_unit_id",
            table: "prospects",
            column: "rental_unit_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "prospects");
    }
}
