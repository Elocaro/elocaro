using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HeatingCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "heating_kind",
                table: "cost_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "heating_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consumption_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    joint_plant = table.Column<bool>(type: "boolean", nullable: false),
                    hot_water_heat_meter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hot_water_formula_factor = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    hot_water_temperature = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    fuel_kwh_per_unit = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    oil_tank_meter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    oil_valuation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    oil_opening_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    apply_carbon_costs = table.Column<bool>(type: "boolean", nullable: false),
                    carbon_special_case = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    carbon_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_heating_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_heating_settings_meter_hot_water_heat_meter_id",
                        column: x => x.hot_water_heat_meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_heating_settings_meter_oil_tank_meter_id",
                        column: x => x.oil_tank_meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_heating_settings_property_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_heating_settings_hot_water_heat_meter_id",
                table: "heating_settings",
                column: "hot_water_heat_meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_heating_settings_oil_tank_meter_id",
                table: "heating_settings",
                column: "oil_tank_meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_heating_settings_property_id",
                table: "heating_settings",
                column: "property_id",
                unique: true,
                filter: "NOT deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "heating_settings");

            migrationBuilder.DropColumn(
                name: "heating_kind",
                table: "cost_items");
        }
    }
}
