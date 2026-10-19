using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Dokumente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dokumente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    datei_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "german"),
                    kategorie = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    datum = table.Column<DateOnly>(type: "date", nullable: true),
                    ablauf_am = table.Column<DateOnly>(type: "date", nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    notiz = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    objekt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    einheit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mietvertrag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fuer_mieter_freigegeben = table.Column<bool>(type: "boolean", nullable: false),
                    fuer_hausmeister_freigegeben = table.Column<bool>(type: "boolean", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dokumente", x => x.id);
                    table.ForeignKey(
                        name: "fk_dokumente_dateien_datei_id",
                        column: x => x.datei_id,
                        principalTable: "dateien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dokumente_einheit_einheit_id",
                        column: x => x.einheit_id,
                        principalTable: "einheiten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dokumente_mietvertrag_mietvertrag_id",
                        column: x => x.mietvertrag_id,
                        principalTable: "mietvertraege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dokumente_objekt_objekt_id",
                        column: x => x.objekt_id,
                        principalTable: "objekte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dokumente_person_person_id",
                        column: x => x.person_id,
                        principalTable: "personen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dokumente_ablauf_am",
                table: "dokumente",
                column: "ablauf_am",
                filter: "ablauf_am IS NOT NULL AND NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_dokumente_datei_id",
                table: "dokumente",
                column: "datei_id");

            migrationBuilder.CreateIndex(
                name: "ix_dokumente_einheit_id",
                table: "dokumente",
                column: "einheit_id");

            migrationBuilder.CreateIndex(
                name: "ix_dokumente_mietvertrag_id",
                table: "dokumente",
                column: "mietvertrag_id");

            migrationBuilder.CreateIndex(
                name: "ix_dokumente_objekt_id",
                table: "dokumente",
                column: "objekt_id");

            migrationBuilder.CreateIndex(
                name: "ix_dokumente_person_id",
                table: "dokumente",
                column: "person_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dokumente");
        }
    }
}
