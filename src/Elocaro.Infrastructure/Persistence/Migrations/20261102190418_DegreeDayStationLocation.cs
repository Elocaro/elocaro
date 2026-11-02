using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DegreeDayStationLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "latitude",
                table: "degree_day_values",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "longitude",
                table: "degree_day_values",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "latitude",
                table: "degree_day_values");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "degree_day_values");
        }
    }
}
