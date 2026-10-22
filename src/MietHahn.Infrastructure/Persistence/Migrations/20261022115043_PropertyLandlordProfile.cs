using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MietHahn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PropertyLandlordProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "landlord_profile_id",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_properties_landlord_profile_id",
                table: "properties",
                column: "landlord_profile_id");

            migrationBuilder.AddForeignKey(
                name: "fk_properties_landlord_profiles_landlord_profile_id",
                table: "properties",
                column: "landlord_profile_id",
                principalTable: "landlord_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_properties_landlord_profiles_landlord_profile_id",
                table: "properties");

            migrationBuilder.DropIndex(
                name: "ix_properties_landlord_profile_id",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "landlord_profile_id",
                table: "properties");
        }
    }
}
