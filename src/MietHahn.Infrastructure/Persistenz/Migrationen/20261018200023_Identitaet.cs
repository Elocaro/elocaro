using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MietHahn.Infrastructure.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Identitaet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "benutzer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anzeigename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    letzte_anmeldung_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    gesperrt = table.Column<bool>(type: "boolean", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benutzer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rollen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rollen", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "anmeldelinks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    benutzer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    gueltig_bis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    eingeloest_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erstellt_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geaendert_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    geaendert_von = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    geloescht = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anmeldelinks", x => x.id);
                    table.ForeignKey(
                        name: "fk_anmeldelinks_benutzer_benutzer_id",
                        column: x => x.benutzer_id,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "benutzer_anmeldungen_extern",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benutzer_anmeldungen_extern", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_benutzer_anmeldungen_extern_benutzer_user_id",
                        column: x => x.user_id,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "benutzer_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benutzer_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_benutzer_claims_benutzer_user_id",
                        column: x => x.user_id,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "benutzer_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benutzer_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_benutzer_tokens_benutzer_user_id",
                        column: x => x.user_id,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "benutzer_rollen",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benutzer_rollen", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_benutzer_rollen_benutzer_user_id",
                        column: x => x.user_id,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_benutzer_rollen_rollen_role_id",
                        column: x => x.role_id,
                        principalTable: "rollen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rollen_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rollen_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_rollen_claims_rollen_role_id",
                        column: x => x.role_id,
                        principalTable: "rollen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anmeldelinks_benutzer_id",
                table: "anmeldelinks",
                column: "benutzer_id");

            migrationBuilder.CreateIndex(
                name: "ix_anmeldelinks_token_hash",
                table: "anmeldelinks",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "benutzer",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "benutzer",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_benutzer_anmeldungen_extern_user_id",
                table: "benutzer_anmeldungen_extern",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_benutzer_claims_user_id",
                table: "benutzer_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_benutzer_rollen_role_id",
                table: "benutzer_rollen",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "rollen",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rollen_claims_role_id",
                table: "rollen_claims",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anmeldelinks");

            migrationBuilder.DropTable(
                name: "benutzer_anmeldungen_extern");

            migrationBuilder.DropTable(
                name: "benutzer_claims");

            migrationBuilder.DropTable(
                name: "benutzer_rollen");

            migrationBuilder.DropTable(
                name: "benutzer_tokens");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "rollen_claims");

            migrationBuilder.DropTable(
                name: "benutzer");

            migrationBuilder.DropTable(
                name: "rollen");
        }
    }
}
