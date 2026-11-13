using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261113204725_UndoOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "undo_of",
            table: "operations",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "restore",
            table: "audit_entries",
            type: "jsonb",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_operations_undo_of",
            table: "operations",
            column: "undo_of");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_operations_undo_of",
            table: "operations");

        migrationBuilder.DropColumn(
            name: "undo_of",
            table: "operations");

        migrationBuilder.DropColumn(
            name: "restore",
            table: "audit_entries");
    }
}
