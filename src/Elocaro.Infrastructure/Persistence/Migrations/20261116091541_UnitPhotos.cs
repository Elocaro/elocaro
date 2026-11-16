using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261116091541_UnitPhotos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "unit_photos",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rental_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_id = table.Column<Guid>(type: "uuid", nullable: false),
                caption = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                order = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_unit_photos", x => x.id);
                table.ForeignKey(
                    name: "fk_unit_photos_stored_files_file_id",
                    column: x => x.file_id,
                    principalTable: "stored_files",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_unit_photos_file_id",
            table: "unit_photos",
            column: "file_id");

        migrationBuilder.CreateIndex(
            name: "ix_unit_photos_rental_unit_id",
            table: "unit_photos",
            column: "rental_unit_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "unit_photos");
    }
}
