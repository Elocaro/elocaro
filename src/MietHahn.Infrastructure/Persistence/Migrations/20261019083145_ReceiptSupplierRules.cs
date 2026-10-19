using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptSupplierRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_cost_type_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_key = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    cost_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_cost_type_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_cost_type_rules_supplier_key",
                table: "supplier_cost_type_rules",
                column: "supplier_key",
                unique: true,
                filter: "NOT deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_cost_type_rules");
        }
    }
}
