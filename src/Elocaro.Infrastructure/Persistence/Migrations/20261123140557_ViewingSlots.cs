using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261123140557_ViewingSlots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "viewing_links",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rental_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_viewing_links", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "viewing_slots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rental_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                minutes = table.Column<int>(type: "integer", nullable: false),
                prospect_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_viewing_slots", x => x.id);
                table.ForeignKey(
                    name: "fk_viewing_slots_prospects_prospect_id",
                    column: x => x.prospect_id,
                    principalTable: "prospects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_viewing_links_rental_unit_id",
            table: "viewing_links",
            column: "rental_unit_id");

        migrationBuilder.CreateIndex(
            name: "ix_viewing_links_token_hash",
            table: "viewing_links",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_viewing_slots_prospect_id",
            table: "viewing_slots",
            column: "prospect_id");

        migrationBuilder.CreateIndex(
            name: "ix_viewing_slots_rental_unit_id_starts_at",
            table: "viewing_slots",
            columns: new[] { "rental_unit_id", "starts_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "viewing_links");

        migrationBuilder.DropTable(
            name: "viewing_slots");
    }
}
