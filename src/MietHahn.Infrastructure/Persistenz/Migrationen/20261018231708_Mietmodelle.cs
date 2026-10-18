using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Mietmodelle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "index_basis_monat",
                table: "mietvertraege",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "index_basis_wert",
                table: "mietvertraege",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mietmodell",
                table: "mietvertraege",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "mietstaffeln",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mietvertrag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ab = table.Column<DateOnly>(type: "date", nullable: false),
                    kaltmiete = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    angewendet_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mietstaffeln", x => x.id);
                    table.ForeignKey(
                        name: "fk_mietstaffeln_mietvertrag_mietvertrag_id",
                        column: x => x.mietvertrag_id,
                        principalTable: "mietvertraege",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vpi_werte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monat = table.Column<DateOnly>(type: "date", nullable: false),
                    wert = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    quelle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vpi_werte", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mietstaffeln_mietvertrag_id_ab",
                table: "mietstaffeln",
                columns: new[] { "mietvertrag_id", "ab" },
                unique: true,
                filter: "NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_vpi_werte_monat",
                table: "vpi_werte",
                column: "monat",
                unique: true,
                filter: "NOT geloescht");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mietstaffeln");

            migrationBuilder.DropTable(
                name: "vpi_werte");

            migrationBuilder.DropColumn(
                name: "index_basis_monat",
                table: "mietvertraege");

            migrationBuilder.DropColumn(
                name: "index_basis_wert",
                table: "mietvertraege");

            migrationBuilder.DropColumn(
                name: "mietmodell",
                table: "mietvertraege");
        }
    }
}
