using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareCheckPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "file_id",
                table: "care_checks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_care_checks_file_id",
                table: "care_checks",
                column: "file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_care_checks_stored_file_file_id",
                table: "care_checks",
                column: "file_id",
                principalTable: "stored_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_care_checks_stored_file_file_id",
                table: "care_checks");

            migrationBuilder.DropIndex(
                name: "ix_care_checks_file_id",
                table: "care_checks");

            migrationBuilder.DropColumn(
                name: "file_id",
                table: "care_checks");
        }
    }
}
