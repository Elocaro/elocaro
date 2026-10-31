using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingMailContentPurge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_purged_at",
                table: "outgoing_mails",
                type: "timestamp with time zone",
                nullable: true);

            // Links in mails already delivered still work until they expire: clear them right away.
            migrationBuilder.Sql("""
                UPDATE outgoing_mails
                SET html = '[Inhalt entfernt]', text = '[Inhalt entfernt]', content_purged_at = now()
                WHERE status <> 'Pending'
                  AND template IN ('invitation-password', 'invitation-link', 'magic-link', 'password-reset', 'signature-request');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content_purged_at",
                table: "outgoing_mails");
        }
    }
}
