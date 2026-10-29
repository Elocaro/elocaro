using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplyContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "supplier_statement",
                table: "receipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "supply_contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    supplier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cost_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    meter_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    basis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    heating_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    meter_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supply_contracts", x => x.id);
                    table.ForeignKey(
                        name: "fk_supply_contracts_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supply_tariffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    base_fee_per_month = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    validity_from = table.Column<DateOnly>(type: "date", nullable: false),
                    validity_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supply_tariffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_supply_tariffs_supply_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "supply_contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supply_contracts_property_id",
                table: "supply_contracts",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_supply_tariffs_contract_id",
                table: "supply_tariffs",
                column: "contract_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supply_tariffs");

            migrationBuilder.DropTable(
                name: "supply_contracts");

            migrationBuilder.DropColumn(
                name: "supplier_statement",
                table: "receipts");
        }
    }
}
