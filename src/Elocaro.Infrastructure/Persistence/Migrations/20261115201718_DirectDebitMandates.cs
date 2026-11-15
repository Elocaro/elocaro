using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261115201718_DirectDebitMandates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "creditor_id",
            table: "landlord_profiles",
            type: "character varying(35)",
            maxLength: 35,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "direct_debit_mandates",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                reference = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                debtor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                debtor_iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                debtor_bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                signed_on = table.Column<DateOnly>(type: "date", nullable: false),
                sequence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                revoked_on = table.Column<DateOnly>(type: "date", nullable: true),
                note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                deleted = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_direct_debit_mandates", x => x.id);
                table.ForeignKey(
                    name: "fk_direct_debit_mandates_lease_lease_id",
                    column: x => x.lease_id,
                    principalTable: "leases",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_direct_debit_mandates_lease_id",
            table: "direct_debit_mandates",
            column: "lease_id");

        migrationBuilder.CreateIndex(
            name: "ix_direct_debit_mandates_reference",
            table: "direct_debit_mandates",
            column: "reference",
            unique: true,
            filter: "NOT deleted");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "direct_debit_mandates");

        migrationBuilder.DropColumn(
            name: "creditor_id",
            table: "landlord_profiles");
    }
}
