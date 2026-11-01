using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DegreeDayStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "degree_day_station_id",
                table: "heating_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "degree_day_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    station_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    degree_days = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: false),
                    long_term_mean = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_degree_day_values", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_degree_day_values_station_id_month",
                table: "degree_day_values",
                columns: new[] { "station_id", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "degree_day_values");

            migrationBuilder.DropColumn(
                name: "degree_day_station_id",
                table: "heating_settings");
        }
    }
}
