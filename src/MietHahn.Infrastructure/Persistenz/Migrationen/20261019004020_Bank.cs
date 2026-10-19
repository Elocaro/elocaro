using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Bank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bankkonten",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "german"),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    bank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    typ = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    objekt_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bankkonten", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "csv_profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    trennzeichen = table.Column<char>(type: "character(1)", nullable: false),
                    zeichensatz = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    datumsformat = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dezimaltrennzeichen = table.Column<char>(type: "character(1)", nullable: false),
                    spalte_buchungsdatum = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    spalte_valutadatum = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_betrag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_soll = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_haben = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_name_eingang = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_name_ausgang = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_iban = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_bic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_verwendungszweck = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_buchungstext = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_end_to_end = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_mandatsreferenz = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_glaeubiger_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_waehrung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    spalte_status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    zerlegung = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csv_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bankimporte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bankkonto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dateiname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    profil = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    inhalt = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    gelesen = table.Column<int>(type: "integer", nullable: false),
                    neu = table.Column<int>(type: "integer", nullable: false),
                    duplikate = table.Column<int>(type: "integer", nullable: false),
                    von = table.Column<DateOnly>(type: "date", nullable: true),
                    bis = table.Column<DateOnly>(type: "date", nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bankimporte", x => x.id);
                    table.ForeignKey(
                        name: "fk_bankimporte_bankkonto_bankkonto_id",
                        column: x => x.bankkonto_id,
                        principalTable: "bankkonten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "umsaetze",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bankkonto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bankimport_id = table.Column<Guid>(type: "uuid", nullable: true),
                    buchungsdatum = table.Column<DateOnly>(type: "date", nullable: false),
                    valutadatum = table.Column<DateOnly>(type: "date", nullable: true),
                    betrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    waehrung = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    gegen_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    gegen_iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    gegen_bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    verwendungszweck = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    buchungstext = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    end_to_end_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mandatsreferenz = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glaeubiger_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notiz = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umsaetze", x => x.id);
                    table.ForeignKey(
                        name: "fk_umsaetze_bankimporte_bankimport_id",
                        column: x => x.bankimport_id,
                        principalTable: "bankimporte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_umsaetze_bankkonten_bankkonto_id",
                        column: x => x.bankkonto_id,
                        principalTable: "bankkonten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bankimporte_bankkonto_id",
                table: "bankimporte",
                column: "bankkonto_id");

            migrationBuilder.CreateIndex(
                name: "ix_bankkonten_iban",
                table: "bankkonten",
                column: "iban",
                unique: true,
                filter: "NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_csv_profile_name",
                table: "csv_profile",
                column: "name",
                unique: true,
                filter: "NOT geloescht");

            migrationBuilder.CreateIndex(
                name: "ix_umsaetze_bankimport_id",
                table: "umsaetze",
                column: "bankimport_id");

            migrationBuilder.CreateIndex(
                name: "ix_umsaetze_bankkonto_id_hash",
                table: "umsaetze",
                columns: new[] { "bankkonto_id", "hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_umsaetze_bankkonto_id_status_buchungsdatum",
                table: "umsaetze",
                columns: new[] { "bankkonto_id", "status", "buchungsdatum" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "csv_profile");

            migrationBuilder.DropTable(
                name: "umsaetze");

            migrationBuilder.DropTable(
                name: "bankimporte");

            migrationBuilder.DropTable(
                name: "bankkonten");
        }
    }
}
