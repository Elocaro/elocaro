using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MietHahn.Infrastructure.Persistenz.MigrationsHilfen;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Personen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anrede = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    titel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    vorname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "german"),
                    nachname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    firma = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "german"),
                    kontaktart = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    taetigkeit = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    geburtsdatum = table.Column<DateOnly>(type: "date", nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telefon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    mobil = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notizen = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    benutzer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anonymisiert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personen", x => x.id);
                    table.ForeignKey(
                        name: "fk_personen_benutzer_benutzer_id",
                        column: x => x.benutzer_id,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bankverbindungen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iban = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    kontoinhaber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bankverbindungen", x => x.id);
                    table.ForeignKey(
                        name: "fk_bankverbindungen_person_person_id",
                        column: x => x.person_id,
                        principalTable: "personen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "person_anschriften",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zusatz = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    strasse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    plz = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ort = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    land = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    gueltigkeit_bis = table.Column<DateOnly>(type: "date", nullable: true),
                    gueltigkeit_von = table.Column<DateOnly>(type: "date", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_anschriften", x => x.id);
                    table.ForeignKey(
                        name: "fk_person_anschriften_person_person_id",
                        column: x => x.person_id,
                        principalTable: "personen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bankverbindungen_iban",
                table: "bankverbindungen",
                column: "iban");

            migrationBuilder.CreateIndex(
                name: "ix_bankverbindungen_person_id",
                table: "bankverbindungen",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_anschriften_person_id",
                table: "person_anschriften",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_personen_benutzer_id",
                table: "personen",
                column: "benutzer_id",
                unique: true,
                filter: "benutzer_id IS NOT NULL AND NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_personen_nachname",
                table: "personen",
                column: "nachname");

            // Je Person höchstens eine gültige Anschrift pro Tag
            migrationBuilder.ErstelleUeberlappungsschutz("ex_person_anschriften_person_gueltigkeit", "person_anschriften", "gueltigkeit_von", "gueltigkeit_bis", "person_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EntferneUeberlappungsschutz("ex_person_anschriften_person_gueltigkeit", "person_anschriften");

            migrationBuilder.DropTable(
                name: "bankverbindungen");

            migrationBuilder.DropTable(
                name: "person_anschriften");

            migrationBuilder.DropTable(
                name: "personen");
        }
    }
}
