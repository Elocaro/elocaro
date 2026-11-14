using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261114153059_RentReductions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "reduction_id",
            table: "charges",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "rent_reductions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                from = table.Column<DateOnly>(type: "date", nullable: false),
                to = table.Column<DateOnly>(type: "date", nullable: true),
                percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                monthly_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                acknowledged_on = table.Column<DateOnly>(type: "date", nullable: true),
                ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_rent_reductions", x => x.id);
                table.ForeignKey(
                    name: "fk_rent_reductions_leases_lease_id",
                    column: x => x.lease_id,
                    principalTable: "leases",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_charges_reduction_unique",
            table: "charges",
            columns: new[] { "reduction_id", "service_month" },
            unique: true,
            filter: "kind = 'RentReduction' AND NOT deleted");

        migrationBuilder.CreateIndex(
            name: "ix_rent_reductions_lease_id",
            table: "rent_reductions",
            column: "lease_id");

        migrationBuilder.AddForeignKey(
            name: "fk_charges_rent_reduction_reduction_id",
            table: "charges",
            column: "reduction_id",
            principalTable: "rent_reductions",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_charges_rent_reduction_reduction_id",
            table: "charges");

        migrationBuilder.DropTable(
            name: "rent_reductions");

        migrationBuilder.DropIndex(
            name: "ix_charges_reduction_unique",
            table: "charges");

        migrationBuilder.DropColumn(
            name: "reduction_id",
            table: "charges");
    }
}
