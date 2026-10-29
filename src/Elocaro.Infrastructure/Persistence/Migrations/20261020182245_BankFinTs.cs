using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankFinTs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fin_ts_bank_code",
                table: "bank_accounts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fin_ts_url",
                table: "bank_accounts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fin_ts_user_id",
                table: "bank_accounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fin_ts_bank_code",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "fin_ts_url",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "fin_ts_user_id",
                table: "bank_accounts");
        }
    }
}
