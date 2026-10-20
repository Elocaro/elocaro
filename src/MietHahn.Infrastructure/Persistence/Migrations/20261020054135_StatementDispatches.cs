using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatementDispatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statement_dispatches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    booked_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: true),
                    receipt_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_dispatches", x => x.id);
                    table.ForeignKey(
                        name: "fk_statement_dispatches_billing_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "billing_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statement_dispatches_charges_charge_id",
                        column: x => x.charge_id,
                        principalTable: "charges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statement_dispatches_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statement_dispatches_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statement_dispatches_outgoing_mails_mail_id",
                        column: x => x.mail_id,
                        principalTable: "outgoing_mails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statement_dispatches_stored_file_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prepayment_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    proposed = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prepayment_proposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_prepayment_proposals_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prepayment_proposals_statement_dispatch_dispatch_id",
                        column: x => x.dispatch_id,
                        principalTable: "statement_dispatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_prepayment_proposals_dispatch_id",
                table: "prepayment_proposals",
                column: "dispatch_id");

            migrationBuilder.CreateIndex(
                name: "ix_prepayment_proposals_lease_id",
                table: "prepayment_proposals",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_dispatches_charge_id",
                table: "statement_dispatches",
                column: "charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_dispatches_document_id",
                table: "statement_dispatches",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_dispatches_file_id",
                table: "statement_dispatches",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_dispatches_lease_id",
                table: "statement_dispatches",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_dispatches_mail_id",
                table: "statement_dispatches",
                column: "mail_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_dispatches_statement_id_lease_id",
                table: "statement_dispatches",
                columns: new[] { "statement_id", "lease_id" },
                unique: true,
                filter: "NOT deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prepayment_proposals");

            migrationBuilder.DropTable(
                name: "statement_dispatches");
        }
    }
}
