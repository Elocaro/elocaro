using Elocaro.Infrastructure.Persistence.MigrationHelpers;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <summary>Brings the tables of the job store to the Quartz.NET 4.6 layout (official upgrade scripts).</summary>
    public partial class UpgradeQuartzSchemaTo46 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpgradeQuartzSchemaTo46();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DowngradeQuartzSchemaFrom46();
        }
    }
}
