using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261116083839_SelfDisclosures : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "self_disclosures",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                prospect_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                persons = table.Column<int>(type: "integer", nullable: true),
                occupation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                net_income = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                pets = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                move_in_on = table.Column<DateOnly>(type: "date", nullable: true),
                message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_self_disclosures", x => x.id);
                table.ForeignKey(
                    name: "fk_self_disclosures_prospects_prospect_id",
                    column: x => x.prospect_id,
                    principalTable: "prospects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_self_disclosures_prospect_id",
            table: "self_disclosures",
            column: "prospect_id");

        migrationBuilder.CreateIndex(
            name: "ix_self_disclosures_token_hash",
            table: "self_disclosures",
            column: "token_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "self_disclosures");
    }
}
