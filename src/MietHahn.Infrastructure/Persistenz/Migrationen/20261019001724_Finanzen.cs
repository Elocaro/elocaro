using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Finanzen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finanzeinstellungen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    buchhaltung_ab = table.Column<DateOnly>(type: "date", nullable: true),
                    verrechnung = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finanzeinstellungen", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sollstellungen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mietvertrag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    art = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bestandteil = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    leistungsmonat = table.Column<DateOnly>(type: "date", nullable: true),
                    faellig = table.Column<DateOnly>(type: "date", nullable: false),
                    betrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("pk_sollstellungen", x => x.id);
                    table.ForeignKey(
                        name: "fk_sollstellungen_mietvertraege_mietvertrag_id",
                        column: x => x.mietvertrag_id,
                        principalTable: "mietvertraege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zahlungen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mietvertrag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    datum = table.Column<DateOnly>(type: "date", nullable: false),
                    betrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    herkunft = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verwendungszweck = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sollstellung_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zahlungen", x => x.id);
                    table.ForeignKey(
                        name: "fk_zahlungen_mietvertraege_mietvertrag_id",
                        column: x => x.mietvertrag_id,
                        principalTable: "mietvertraege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zahlungen_sollstellungen_sollstellung_id",
                        column: x => x.sollstellung_id,
                        principalTable: "sollstellungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sollstellungen_mietvertrag_id_faellig",
                table: "sollstellungen",
                columns: new[] { "mietvertrag_id", "faellig" });

            migrationBuilder.CreateIndex(
                name: "ix_sollstellungen_monatsmiete_eindeutig",
                table: "sollstellungen",
                columns: new[] { "mietvertrag_id", "leistungsmonat", "bestandteil" },
                unique: true,
                filter: "art = 'Miete' AND NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_zahlungen_mietvertrag_id_datum",
                table: "zahlungen",
                columns: new[] { "mietvertrag_id", "datum" });

            migrationBuilder.CreateIndex(
                name: "ix_zahlungen_sollstellung_id",
                table: "zahlungen",
                column: "sollstellung_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finanzeinstellungen");

            migrationBuilder.DropTable(
                name: "zahlungen");

            migrationBuilder.DropTable(
                name: "sollstellungen");
        }
    }
}
