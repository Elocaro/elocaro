using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Vermieterprofil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vermieterprofile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    zusatz = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    strasse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    plz = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ort = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    telefon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    bank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    steuernummer = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vermieterprofile", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vermieterprofile");
        }
    }
}
