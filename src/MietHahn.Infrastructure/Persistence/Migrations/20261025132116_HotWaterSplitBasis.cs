using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HotWaterSplitBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "split_basis",
                table: "heating_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "split_basis",
                table: "heating_settings");
        }
    }
}
