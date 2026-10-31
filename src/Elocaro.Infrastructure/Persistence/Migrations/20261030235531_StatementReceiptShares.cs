using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatementReceiptShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statement_receipt_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_receipt_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_statement_receipt_shares_billing_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "billing_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statement_receipt_shares_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statement_receipt_shares_receipt_id",
                table: "statement_receipt_shares",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_receipt_shares_statement_id_receipt_id",
                table: "statement_receipt_shares",
                columns: new[] { "statement_id", "receipt_id" },
                unique: true,
                filter: "NOT deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statement_receipt_shares");
        }
    }
}
