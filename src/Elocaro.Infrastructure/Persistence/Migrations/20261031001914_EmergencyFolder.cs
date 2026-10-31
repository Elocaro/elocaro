using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmergencyFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "emergency_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    important_contacts = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    document_location = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    further_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    last_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emergency_folders", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emergency_folders");
        }
    }
}
