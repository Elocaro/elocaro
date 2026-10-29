using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Dunning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "base_rate",
                table: "finance_settings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "base_rate_as_of",
                table: "finance_settings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "charge_default_interest",
                table: "finance_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "days_until_first_notice",
                table: "finance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<int>(
                name: "days_until_second_notice",
                table: "finance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<int>(
                name: "dunning_deadline_days",
                table: "finance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "dunning_grace_days",
                table: "finance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<decimal>(
                name: "dunning_minimum_amount",
                table: "finance_settings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<decimal>(
                name: "first_notice_fee",
                table: "finance_settings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "interest_margin_points",
                table: "finance_settings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<decimal>(
                name: "reminder_fee",
                table: "finance_settings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "second_notice_fee",
                table: "finance_settings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "dunning_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    until = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunning_holds", x => x.id);
                    table.ForeignKey(
                        name: "fk_dunning_holds_lease_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dunnings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proposed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    oldest_due = table.Column<DateOnly>(type: "date", nullable: false),
                    principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    termination_relevant = table.Column<bool>(type: "boolean", nullable: false),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_by_mail = table.Column<bool>(type: "boolean", nullable: false),
                    mail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fee_charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    interest_charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunnings", x => x.id);
                    table.ForeignKey(
                        name: "fk_dunnings_lease_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dunnings_stored_file_file_id",
                        column: x => x.file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dunning_holds_lease_id",
                table: "dunning_holds",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_dunnings_file_id",
                table: "dunnings",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_dunnings_lease_id_status",
                table: "dunnings",
                columns: new[] { "lease_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_dunnings_proposal_unique",
                table: "dunnings",
                column: "lease_id",
                unique: true,
                filter: "status = 'Proposed' AND NOT deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dunning_holds");

            migrationBuilder.DropTable(
                name: "dunnings");

            migrationBuilder.DropColumn(
                name: "base_rate",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "base_rate_as_of",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "charge_default_interest",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "days_until_first_notice",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "days_until_second_notice",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "dunning_deadline_days",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "dunning_grace_days",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "dunning_minimum_amount",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "first_notice_fee",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "interest_margin_points",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "reminder_fee",
                table: "finance_settings");

            migrationBuilder.DropColumn(
                name: "second_notice_fee",
                table: "finance_settings");
        }
    }
}
