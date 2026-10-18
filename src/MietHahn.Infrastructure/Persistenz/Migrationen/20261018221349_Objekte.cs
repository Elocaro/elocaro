using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MietHahn.Infrastructure.Persistenz.MigrationsHilfen;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Objekte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "objekte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    strasse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    plz = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ort = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    baujahr = table.Column<int>(type: "integer", nullable: true),
                    gemarkung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    flurstueck = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    grundsteuer_aktenzeichen = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    heizungsart = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    energietraeger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bild_datei_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notizen = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objekte", x => x.id);
                    table.ForeignKey(
                        name: "fk_objekte_dateien_bild_datei_id",
                        column: x => x.bild_datei_id,
                        principalTable: "dateien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "einheiten",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objekt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    lage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    typ = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reihenfolge = table.Column<int>(type: "integer", nullable: false),
                    zimmeranzahl = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    bad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    balkon = table.Column<bool>(type: "boolean", nullable: false),
                    einbaukueche = table.Column<bool>(type: "boolean", nullable: false),
                    energiestandard = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ausstattung = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_einheiten", x => x.id);
                    table.ForeignKey(
                        name: "fk_einheiten_objekt_objekt_id",
                        column: x => x.objekt_id,
                        principalTable: "objekte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "objekt_flaechen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objekt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gesamtflaeche = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    gemeinschaftsflaeche = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
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
                    table.PrimaryKey("pk_objekt_flaechen", x => x.id);
                    table.ForeignKey(
                        name: "fk_objekt_flaechen_objekt_objekt_id",
                        column: x => x.objekt_id,
                        principalTable: "objekte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "objekt_zuordnungen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objekt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    benutzer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objekt_zuordnungen", x => x.id);
                    table.ForeignKey(
                        name: "fk_objekt_zuordnungen_benutzer_benutzer_id",
                        column: x => x.benutzer_id,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_objekt_zuordnungen_objekte_objekt_id",
                        column: x => x.objekt_id,
                        principalTable: "objekte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "einheit_flaechen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    einheit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wohnflaeche = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    heizflaeche = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
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
                    table.PrimaryKey("pk_einheit_flaechen", x => x.id);
                    table.ForeignKey(
                        name: "fk_einheit_flaechen_einheit_einheit_id",
                        column: x => x.einheit_id,
                        principalTable: "einheiten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zimmer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    einheit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    reihenfolge = table.Column<int>(type: "integer", nullable: false),
                    moeblierung = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notizen = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zimmer", x => x.id);
                    table.ForeignKey(
                        name: "fk_zimmer_einheit_einheit_id",
                        column: x => x.einheit_id,
                        principalTable: "einheiten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zimmer_flaechen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zimmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flaeche = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    anteil_gemeinschaftsflaeche = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
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
                    table.PrimaryKey("pk_zimmer_flaechen", x => x.id);
                    table.ForeignKey(
                        name: "fk_zimmer_flaechen_zimmer_zimmer_id",
                        column: x => x.zimmer_id,
                        principalTable: "zimmer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_einheit_flaechen_einheit_id",
                table: "einheit_flaechen",
                column: "einheit_id");

            migrationBuilder.CreateIndex(
                name: "ix_einheiten_objekt_id",
                table: "einheiten",
                column: "objekt_id");

            migrationBuilder.CreateIndex(
                name: "ix_objekt_flaechen_objekt_id",
                table: "objekt_flaechen",
                column: "objekt_id");

            migrationBuilder.CreateIndex(
                name: "ix_objekt_zuordnungen_benutzer_id_objekt_id",
                table: "objekt_zuordnungen",
                columns: new[] { "benutzer_id", "objekt_id" },
                unique: true,
                filter: "NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_objekt_zuordnungen_objekt_id",
                table: "objekt_zuordnungen",
                column: "objekt_id");

            migrationBuilder.CreateIndex(
                name: "ix_objekte_bild_datei_id",
                table: "objekte",
                column: "bild_datei_id");

            migrationBuilder.CreateIndex(
                name: "ix_zimmer_einheit_id",
                table: "zimmer",
                column: "einheit_id");

            migrationBuilder.CreateIndex(
                name: "ix_zimmer_flaechen_zimmer_id",
                table: "zimmer_flaechen",
                column: "zimmer_id");

            // Flächen: je Objekt/Einheit/Zimmer höchstens ein gültiger Stand pro Tag
            migrationBuilder.ErstelleUeberlappungsschutz("ex_objekt_flaechen_objekt_gueltigkeit", "objekt_flaechen", "gueltigkeit_von", "gueltigkeit_bis", "objekt_id");
            migrationBuilder.ErstelleUeberlappungsschutz("ex_einheit_flaechen_einheit_gueltigkeit", "einheit_flaechen", "gueltigkeit_von", "gueltigkeit_bis", "einheit_id");
            migrationBuilder.ErstelleUeberlappungsschutz("ex_zimmer_flaechen_zimmer_gueltigkeit", "zimmer_flaechen", "gueltigkeit_von", "gueltigkeit_bis", "zimmer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EntferneUeberlappungsschutz("ex_objekt_flaechen_objekt_gueltigkeit", "objekt_flaechen");
            migrationBuilder.EntferneUeberlappungsschutz("ex_einheit_flaechen_einheit_gueltigkeit", "einheit_flaechen");
            migrationBuilder.EntferneUeberlappungsschutz("ex_zimmer_flaechen_zimmer_gueltigkeit", "zimmer_flaechen");

            migrationBuilder.DropTable(
                name: "einheit_flaechen");

            migrationBuilder.DropTable(
                name: "objekt_flaechen");

            migrationBuilder.DropTable(
                name: "objekt_zuordnungen");

            migrationBuilder.DropTable(
                name: "zimmer_flaechen");

            migrationBuilder.DropTable(
                name: "zimmer");

            migrationBuilder.DropTable(
                name: "einheiten");

            migrationBuilder.DropTable(
                name: "objekte");
        }
    }
}
