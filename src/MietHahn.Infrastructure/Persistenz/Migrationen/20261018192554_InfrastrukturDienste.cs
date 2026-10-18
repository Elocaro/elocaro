using System;
using System.Collections.Generic;
using MietHahn.Infrastructure.Persistenz.MigrationsHilfen;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class InfrastrukturDienste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ausgehende_mails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<List<string>>(type: "text[]", nullable: false),
                    betreff = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    html = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    anhang_datei_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    vorlage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bezug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    versuche = table.Column<int>(type: "integer", nullable: false),
                    naechster_versuch_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gesendet_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    letzter_fehler = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ausgehende_mails", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dateien",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_typ = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    groesse = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dateien", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_laeufe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gestartet_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    beendet_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erfolgreich = table.Column<bool>(type: "boolean", nullable: true),
                    fehler = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_laeufe", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ausgehende_mails_bezug",
                table: "ausgehende_mails",
                column: "bezug");

            migrationBuilder.CreateIndex(
                name: "ix_ausgehende_mails_naechster_versuch_am",
                table: "ausgehende_mails",
                column: "naechster_versuch_am",
                filter: "status = 'Ausstehend'");

            migrationBuilder.CreateIndex(
                name: "ix_dateien_sha256",
                table: "dateien",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_job_laeufe_job_name_gestartet_am",
                table: "job_laeufe",
                columns: new[] { "job_name", "gestartet_am" });

            // Tabellen für den persistenten Quartz-Job-Store (offizielles Skript, eingebettete Ressource).
            migrationBuilder.ErstelleQuartzSchema();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EntferneQuartzSchema();

            migrationBuilder.DropTable(
                name: "ausgehende_mails");

            migrationBuilder.DropTable(
                name: "dateien");

            migrationBuilder.DropTable(
                name: "job_laeufe");
        }
    }
}
