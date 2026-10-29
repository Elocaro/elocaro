using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RentIncreases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rent_cap_percent",
                table: "properties",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.CreateTable(
                name: "rent_increases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    new_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    planned_declaration = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    consent_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    declared_on = table.Column<DateOnly>(type: "date", nullable: true),
                    consent_on = table.Column<DateOnly>(type: "date", nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    basis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    comparative_rent_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    cap_percent = table.Column<int>(type: "integer", nullable: true),
                    cap_limit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    index_old_month = table.Column<DateOnly>(type: "date", nullable: true),
                    index_old_value = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    index_new_month = table.Column<DateOnly>(type: "date", nullable: true),
                    index_new_value = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_increases", x => x.id);
                    table.ForeignKey(
                        name: "fk_rent_increases_document_template_revisions_template_revisio",
                        column: x => x.template_revision_id,
                        principalTable: "document_template_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rent_increases_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rent_increases_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rent_increases_document_id",
                table: "rent_increases",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_rent_increases_lease_id_status",
                table: "rent_increases",
                columns: new[] { "lease_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rent_increases_template_revision_id",
                table: "rent_increases",
                column: "template_revision_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rent_increases");

            migrationBuilder.DropColumn(
                name: "rent_cap_percent",
                table: "properties");
        }
    }
}
