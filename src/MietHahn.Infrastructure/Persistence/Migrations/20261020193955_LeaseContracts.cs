using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaseContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "furnishing_list",
                table: "rooms",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lease_contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    signed_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lease_contracts", x => x.id);
                    table.ForeignKey(
                        name: "fk_lease_contracts_document_template_revisions_template_revisi",
                        column: x => x.template_revision_id,
                        principalTable: "document_template_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lease_contracts_documents_signed_document_id",
                        column: x => x.signed_document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lease_contracts_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lease_contracts_stored_file_draft_file_id",
                        column: x => x.draft_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lease_contracts_draft_file_id",
                table: "lease_contracts",
                column: "draft_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_lease_contracts_signed_document_id",
                table: "lease_contracts",
                column: "signed_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_lease_contracts_template_revision_id",
                table: "lease_contracts",
                column: "template_revision_id");

            migrationBuilder.CreateIndex(
                name: "ux_lease_contracts_open_draft",
                table: "lease_contracts",
                column: "lease_id",
                unique: true,
                filter: "status = 'Draft' AND NOT deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lease_contracts");

            migrationBuilder.DropColumn(
                name: "furnishing_list",
                table: "rooms");
        }
    }
}
