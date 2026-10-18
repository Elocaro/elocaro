using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MietHahn.Infrastructure.Persistenz.MigrationsHilfen;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Mietvertraege : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mietvertraege",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mietobjekt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    einheit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zimmer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    befristet_bis = table.Column<DateOnly>(type: "date", nullable: true),
                    kuendigung_eingegangen_am = table.Column<DateOnly>(type: "date", nullable: true),
                    kuendigung_durch = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    kuendigungsfrist_monate = table.Column<int>(type: "integer", nullable: false),
                    abrechnungsart = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    heizkosten_verbrauchsabhaengig = table.Column<bool>(type: "boolean", nullable: false),
                    faellig_am_werktag = table.Column<int>(type: "integer", nullable: false),
                    zahlungsart = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kaution = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    kaution_notiz = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notizen = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    laufzeit_bis = table.Column<DateOnly>(type: "date", nullable: true),
                    laufzeit_von = table.Column<DateOnly>(type: "date", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mietvertraege", x => x.id);
                    table.ForeignKey(
                        name: "fk_mietvertraege_einheiten_einheit_id",
                        column: x => x.einheit_id,
                        principalTable: "einheiten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mietvertraege_zimmer_zimmer_id",
                        column: x => x.zimmer_id,
                        principalTable: "zimmer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mietbestandteile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mietvertrag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    art = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    betrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_mietbestandteile", x => x.id);
                    table.ForeignKey(
                        name: "fk_mietbestandteile_mietvertrag_mietvertrag_id",
                        column: x => x.mietvertrag_id,
                        principalTable: "mietvertraege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "personenstaende",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mietvertrag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anzahl = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_personenstaende", x => x.id);
                    table.ForeignKey(
                        name: "fk_personenstaende_mietvertraege_mietvertrag_id",
                        column: x => x.mietvertrag_id,
                        principalTable: "mietvertraege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vertragsparteien",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mietvertrag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rolle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reihenfolge = table.Column<int>(type: "integer", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vertragsparteien", x => x.id);
                    table.ForeignKey(
                        name: "fk_vertragsparteien_mietvertraege_mietvertrag_id",
                        column: x => x.mietvertrag_id,
                        principalTable: "mietvertraege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vertragsparteien_personen_person_id",
                        column: x => x.person_id,
                        principalTable: "personen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mietbestandteile_mietvertrag_id",
                table: "mietbestandteile",
                column: "mietvertrag_id");

            migrationBuilder.CreateIndex(
                name: "ix_mietvertraege_einheit_id",
                table: "mietvertraege",
                column: "einheit_id");

            migrationBuilder.CreateIndex(
                name: "ix_mietvertraege_mietobjekt_id",
                table: "mietvertraege",
                column: "mietobjekt_id");

            migrationBuilder.CreateIndex(
                name: "ix_mietvertraege_zimmer_id",
                table: "mietvertraege",
                column: "zimmer_id");

            migrationBuilder.CreateIndex(
                name: "ix_personenstaende_mietvertrag_id",
                table: "personenstaende",
                column: "mietvertrag_id");

            migrationBuilder.CreateIndex(
                name: "ix_vertragsparteien_mietvertrag_id_person_id",
                table: "vertragsparteien",
                columns: new[] { "mietvertrag_id", "person_id" },
                unique: true,
                filter: "NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_vertragsparteien_person_id",
                table: "vertragsparteien",
                column: "person_id");

            // Je Mietobjekt höchstens ein Vertrag pro Tag; je Vertrag ein Betrag je Bestandteilart und eine Personenanzahl pro Tag
            migrationBuilder.ErstelleUeberlappungsschutz("ex_mietvertraege_mietobjekt_laufzeit", "mietvertraege", "laufzeit_von", "laufzeit_bis", "mietobjekt_id");
            migrationBuilder.ErstelleUeberlappungsschutz("ex_mietbestandteile_vertrag_art_gueltigkeit", "mietbestandteile", "gueltigkeit_von", "gueltigkeit_bis", "mietvertrag_id", "art");
            migrationBuilder.ErstelleUeberlappungsschutz("ex_personenstaende_vertrag_gueltigkeit", "personenstaende", "gueltigkeit_von", "gueltigkeit_bis", "mietvertrag_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EntferneUeberlappungsschutz("ex_mietvertraege_mietobjekt_laufzeit", "mietvertraege");
            migrationBuilder.EntferneUeberlappungsschutz("ex_mietbestandteile_vertrag_art_gueltigkeit", "mietbestandteile");
            migrationBuilder.EntferneUeberlappungsschutz("ex_personenstaende_vertrag_gueltigkeit", "personenstaende");

            migrationBuilder.DropTable(
                name: "mietbestandteile");

            migrationBuilder.DropTable(
                name: "personenstaende");

            migrationBuilder.DropTable(
                name: "vertragsparteien");

            migrationBuilder.DropTable(
                name: "mietvertraege");
        }
    }
}
