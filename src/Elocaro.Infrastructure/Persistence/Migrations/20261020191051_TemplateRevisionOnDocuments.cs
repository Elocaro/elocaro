using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TemplateRevisionOnDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "template_revision_id",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_template_revision_id",
                table: "documents",
                column: "template_revision_id");

            migrationBuilder.AddForeignKey(
                name: "fk_documents_document_template_revision_template_revision_id",
                table: "documents",
                column: "template_revision_id",
                principalTable: "document_template_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documents_document_template_revision_template_revision_id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_template_revision_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "template_revision_id",
                table: "documents");
        }
    }
}
