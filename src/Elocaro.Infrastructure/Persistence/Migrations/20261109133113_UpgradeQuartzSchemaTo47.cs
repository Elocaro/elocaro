using Elocaro.Infrastructure.Persistence.MigrationHelpers;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elocaro.Infrastructure.Persistence.Migrations
{
    /// <summary>Brings the tables of the job store to the Quartz.NET 4.7 layout (column <c>fixed_delay</c> of the triggers).</summary>
    public partial class UpgradeQuartzSchemaTo47 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpgradeQuartzSchemaTo47();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DowngradeQuartzSchemaFrom47();
        }
    }
}
