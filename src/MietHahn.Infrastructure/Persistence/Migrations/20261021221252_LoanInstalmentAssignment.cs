using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoanInstalmentAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "loan_id",
                table: "transaction_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "loan_interest",
                table: "transaction_assignments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transaction_assignments_loan_id",
                table: "transaction_assignments",
                column: "loan_id");

            migrationBuilder.AddForeignKey(
                name: "fk_transaction_assignments_loans_loan_id",
                table: "transaction_assignments",
                column: "loan_id",
                principalTable: "loans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transaction_assignments_loans_loan_id",
                table: "transaction_assignments");

            migrationBuilder.DropIndex(
                name: "ix_transaction_assignments_loan_id",
                table: "transaction_assignments");

            migrationBuilder.DropColumn(
                name: "loan_id",
                table: "transaction_assignments");

            migrationBuilder.DropColumn(
                name: "loan_interest",
                table: "transaction_assignments");
        }
    }
}
