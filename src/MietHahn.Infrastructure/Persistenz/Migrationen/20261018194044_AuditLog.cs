using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_eintraege",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zeitpunkt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    benutzer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entitaet_typ = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entitaet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aktion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    aenderungen = table.Column<string>(type: "jsonb", nullable: false),
                    zweck = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_eintraege", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_eintraege_entitaet_typ_entitaet_id_zeitpunkt",
                table: "audit_eintraege",
                columns: new[] { "entitaet_typ", "entitaet_id", "zeitpunkt" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_eintraege_zeitpunkt",
                table: "audit_eintraege",
                column: "zeitpunkt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_eintraege");
        }
    }
}
