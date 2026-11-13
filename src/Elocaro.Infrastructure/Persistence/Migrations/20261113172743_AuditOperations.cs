using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261113172743_AuditOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "operation_id",
            table: "audit_entries",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                user = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_operations", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_entries_operation_id",
            table: "audit_entries",
            column: "operation_id");

        migrationBuilder.CreateIndex(
            name: "ix_operations_timestamp",
            table: "operations",
            column: "timestamp");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "operations");

        migrationBuilder.DropIndex(
            name: "ix_audit_entries_operation_id",
            table: "audit_entries");

        migrationBuilder.DropColumn(
            name: "operation_id",
            table: "audit_entries");
    }
}
