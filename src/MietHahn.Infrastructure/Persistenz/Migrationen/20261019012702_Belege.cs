using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Belege : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "belege",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lieferant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lieferant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "german"),
                    rechnungsnummer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rechnungsdatum = table.Column<DateOnly>(type: "date", nullable: false),
                    leistung_von = table.Column<DateOnly>(type: "date", nullable: true),
                    leistung_bis = table.Column<DateOnly>(type: "date", nullable: true),
                    brutto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    netto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    umsatzsteuer = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    faellig_am = table.Column<DateOnly>(type: "date", nullable: true),
                    bezahlt_am = table.Column<DateOnly>(type: "date", nullable: true),
                    brennstoffmenge = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    brennstoffeinheit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    co2kg = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    notiz = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_belege", x => x.id);
                    table.ForeignKey(
                        name: "fk_belege_person_lieferant_id",
                        column: x => x.lieferant_id,
                        principalTable: "personen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beleg_dateien",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    beleg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    datei_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seite = table.Column<int>(type: "integer", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beleg_dateien", x => x.id);
                    table.ForeignKey(
                        name: "fk_beleg_dateien_beleg_beleg_id",
                        column: x => x.beleg_id,
                        principalTable: "belege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_beleg_dateien_datei_datei_id",
                        column: x => x.datei_id,
                        principalTable: "dateien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kostenpositionen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    beleg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kostenart = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    betrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    objekt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    einheit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    umlagefaehig = table.Column<bool>(type: "boolean", nullable: false),
                    paragraf35a = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    arbeitskosten = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    notiz = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    abrechnungszeitraum_bis = table.Column<DateOnly>(type: "date", nullable: true),
                    abrechnungszeitraum_von = table.Column<DateOnly>(type: "date", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kostenpositionen", x => x.id);
                    table.ForeignKey(
                        name: "fk_kostenpositionen_belege_beleg_id",
                        column: x => x.beleg_id,
                        principalTable: "belege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_kostenpositionen_einheiten_einheit_id",
                        column: x => x.einheit_id,
                        principalTable: "einheiten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_kostenpositionen_objekt_objekt_id",
                        column: x => x.objekt_id,
                        principalTable: "objekte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_beleg_dateien_beleg_id",
                table: "beleg_dateien",
                column: "beleg_id");

            migrationBuilder.CreateIndex(
                name: "ix_beleg_dateien_datei_id",
                table: "beleg_dateien",
                column: "datei_id");

            migrationBuilder.CreateIndex(
                name: "ix_belege_lieferant_id",
                table: "belege",
                column: "lieferant_id");

            migrationBuilder.CreateIndex(
                name: "ix_belege_rechnungsdatum",
                table: "belege",
                column: "rechnungsdatum");

            migrationBuilder.CreateIndex(
                name: "ix_kostenpositionen_beleg_id",
                table: "kostenpositionen",
                column: "beleg_id");

            migrationBuilder.CreateIndex(
                name: "ix_kostenpositionen_einheit_id",
                table: "kostenpositionen",
                column: "einheit_id");

            migrationBuilder.CreateIndex(
                name: "ix_kostenpositionen_objekt_id_kostenart",
                table: "kostenpositionen",
                columns: new[] { "objekt_id", "kostenart" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beleg_dateien");

            migrationBuilder.DropTable(
                name: "kostenpositionen");

            migrationBuilder.DropTable(
                name: "belege");
        }
    }
}
