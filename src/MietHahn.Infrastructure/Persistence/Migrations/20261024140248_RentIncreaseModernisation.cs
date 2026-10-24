using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RentIncreaseModernisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "effective_extended",
                table: "rent_increases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "maintenance_share",
                table: "rent_increases",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "modernisation_costs",
                table: "rent_increases",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "effective_extended",
                table: "rent_increases");

            migrationBuilder.DropColumn(
                name: "maintenance_share",
                table: "rent_increases");

            migrationBuilder.DropColumn(
                name: "modernisation_costs",
                table: "rent_increases");
        }
    }
}
