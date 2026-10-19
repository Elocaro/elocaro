using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionAssignmentAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    counterparty_iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    counterparty_contains = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    purpose_contains = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cost_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocable = table.Column<bool>(type: "boolean", nullable: true),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_match_rules_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_match_rules_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_match_rules_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transaction_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_created = table.Column<bool>(type: "boolean", nullable: false),
                    paid_on_set = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transaction_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_transaction_assignments_cost_items_cost_item_id",
                        column: x => x.cost_item_id,
                        principalTable: "cost_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transaction_assignments_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transaction_assignments_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transaction_assignments_transaction_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "bank_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_match_rules_lease_id",
                table: "match_rules",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_match_rules_priority",
                table: "match_rules",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_match_rules_property_id",
                table: "match_rules",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_match_rules_unit_id",
                table: "match_rules",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_assignments_cost_item_id",
                table: "transaction_assignments",
                column: "cost_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_assignments_payment_id",
                table: "transaction_assignments",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_assignments_receipt_id",
                table: "transaction_assignments",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_assignments_transaction_id",
                table: "transaction_assignments",
                column: "transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_rules");

            migrationBuilder.DropTable(
                name: "transaction_assignments");
        }
    }
}
