using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HandoverProtocols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "handover_protocols",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    signed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_signature_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    landlord_signature_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_handover_protocols", x => x.id);
                    table.ForeignKey(
                        name: "fk_handover_protocols_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_handover_protocols_lease_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_handover_protocols_stored_file_document_file_id",
                        column: x => x.document_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_handover_protocols_stored_file_landlord_signature_file_id",
                        column: x => x.landlord_signature_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_handover_protocols_stored_file_tenant_signature_file_id",
                        column: x => x.tenant_signature_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_handover_protocols_document_file_id",
                table: "handover_protocols",
                column: "document_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_handover_protocols_document_id",
                table: "handover_protocols",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_handover_protocols_landlord_signature_file_id",
                table: "handover_protocols",
                column: "landlord_signature_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_handover_protocols_lease_id_type",
                table: "handover_protocols",
                columns: new[] { "lease_id", "type" },
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.CreateIndex(
                name: "ix_handover_protocols_tenant_signature_file_id",
                table: "handover_protocols",
                column: "tenant_signature_file_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "handover_protocols");
        }
    }
}
