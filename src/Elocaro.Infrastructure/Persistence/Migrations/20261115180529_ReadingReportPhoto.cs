using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261115180529_ReadingReportPhoto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "photo_file_id",
            table: "reading_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_reading_reports_photo_file_id",
            table: "reading_reports",
            column: "photo_file_id");

        migrationBuilder.AddForeignKey(
            name: "fk_reading_reports_stored_file_photo_file_id",
            table: "reading_reports",
            column: "photo_file_id",
            principalTable: "stored_files",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_reading_reports_stored_file_photo_file_id",
            table: "reading_reports");

        migrationBuilder.DropIndex(
            name: "ix_reading_reports_photo_file_id",
            table: "reading_reports");

        migrationBuilder.DropColumn(
            name: "photo_file_id",
            table: "reading_reports");
    }
}
