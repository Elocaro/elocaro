using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Zaehler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "zaehler",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objekt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    einheit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zimmer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    typ = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    messeinheit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    nummer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    lage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    hauptzaehler_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vorgaenger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    eingebaut_am = table.Column<DateOnly>(type: "date", nullable: false),
                    ausgebaut_am = table.Column<DateOnly>(type: "date", nullable: true),
                    eichfrist_bis = table.Column<DateOnly>(type: "date", nullable: true),
                    reihenfolge = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_zaehler", x => x.id);
                    table.ForeignKey(
                        name: "fk_zaehler_einheiten_einheit_id",
                        column: x => x.einheit_id,
                        principalTable: "einheiten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zaehler_objekte_objekt_id",
                        column: x => x.objekt_id,
                        principalTable: "objekte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zaehler_zaehler_hauptzaehler_id",
                        column: x => x.hauptzaehler_id,
                        principalTable: "zaehler",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zaehler_zaehler_vorgaenger_id",
                        column: x => x.vorgaenger_id,
                        principalTable: "zaehler",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zaehler_zimmer_zimmer_id",
                        column: x => x.zimmer_id,
                        principalTable: "zimmer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zaehlerstaende",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zaehler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    datum = table.Column<DateOnly>(type: "date", nullable: false),
                    wert = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    art = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ableser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    foto_datei_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notiz = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zaehlerstaende", x => x.id);
                    table.ForeignKey(
                        name: "fk_zaehlerstaende_dateien_foto_datei_id",
                        column: x => x.foto_datei_id,
                        principalTable: "dateien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zaehlerstaende_zaehler_zaehler_id",
                        column: x => x.zaehler_id,
                        principalTable: "zaehler",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_zaehler_einheit_id",
                table: "zaehler",
                column: "einheit_id");

            migrationBuilder.CreateIndex(
                name: "ix_zaehler_hauptzaehler_id",
                table: "zaehler",
                column: "hauptzaehler_id");

            migrationBuilder.CreateIndex(
                name: "ix_zaehler_nummer",
                table: "zaehler",
                column: "nummer");

            migrationBuilder.CreateIndex(
                name: "ix_zaehler_objekt_id",
                table: "zaehler",
                column: "objekt_id");

            migrationBuilder.CreateIndex(
                name: "ix_zaehler_vorgaenger_id",
                table: "zaehler",
                column: "vorgaenger_id");

            migrationBuilder.CreateIndex(
                name: "ix_zaehler_zimmer_id",
                table: "zaehler",
                column: "zimmer_id");

            migrationBuilder.CreateIndex(
                name: "ix_zaehlerstaende_foto_datei_id",
                table: "zaehlerstaende",
                column: "foto_datei_id");

            migrationBuilder.CreateIndex(
                name: "ix_zaehlerstaende_zaehler_id_datum",
                table: "zaehlerstaende",
                columns: new[] { "zaehler_id", "datum" },
                unique: true,
                filter: "NOT geloescht");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zaehlerstaende");

            migrationBuilder.DropTable(
                name: "zaehler");
        }
    }
}
