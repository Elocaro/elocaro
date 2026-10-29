using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Elocaro.Infrastructure.Persistence.MigrationHelpers;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperatingCostKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shared_flat_billing",
                table: "units",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "FlatRate");

            migrationBuilder.AddColumn<List<string>>(
                name: "excluded_cost_types",
                table: "leases",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<Guid>(
                name: "subtype_id",
                table: "cost_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cost_allocation_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    meter_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pre_deduction_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    pre_deduction_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pre_deduction_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_cost_allocation_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_allocation_rules_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cost_subtypes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_subtypes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_allocation_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_allocation_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_allocation_shares_cost_allocation_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "cost_allocation_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cost_allocation_shares_unit_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cost_items_subtype_id",
                table: "cost_items",
                column: "subtype_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_allocation_rules_property_id_cost_type",
                table: "cost_allocation_rules",
                columns: new[] { "property_id", "cost_type" });

            migrationBuilder.CreateIndex(
                name: "ix_cost_allocation_shares_rule_id",
                table: "cost_allocation_shares",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_allocation_shares_unit_id",
                table: "cost_allocation_shares",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_subtypes_cost_type_name",
                table: "cost_subtypes",
                columns: new[] { "cost_type", "name" },
                unique: true,
                filter: "NOT deleted");

            migrationBuilder.AddForeignKey(
                name: "fk_cost_items_cost_subtype_subtype_id",
                table: "cost_items",
                column: "subtype_id",
                principalTable: "cost_subtypes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateOverlapProtection("ex_cost_allocation_rules_property_cost_type_validity", "cost_allocation_rules", "validity_from", "validity_to", "property_id", "cost_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropOverlapProtection("ex_cost_allocation_rules_property_cost_type_validity", "cost_allocation_rules");

            migrationBuilder.DropForeignKey(
                name: "fk_cost_items_cost_subtype_subtype_id",
                table: "cost_items");

            migrationBuilder.DropTable(
                name: "cost_allocation_shares");

            migrationBuilder.DropTable(
                name: "cost_subtypes");

            migrationBuilder.DropTable(
                name: "cost_allocation_rules");

            migrationBuilder.DropIndex(
                name: "ix_cost_items_subtype_id",
                table: "cost_items");

            migrationBuilder.DropColumn(
                name: "shared_flat_billing",
                table: "units");

            migrationBuilder.DropColumn(
                name: "excluded_cost_types",
                table: "leases");

            migrationBuilder.DropColumn(
                name: "subtype_id",
                table: "cost_items");
        }
    }
}
