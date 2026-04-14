using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    public partial class AddIsBreakEntry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add IsBreakEntry column — was present in the model but missing from DB,
            // causing "An error occurred while saving the entity changes" on every SaveChangesAsync.
            migrationBuilder.AddColumn<bool>(
                name: "IsBreakEntry",
                table: "TimeEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBreakEntry",
                table: "TimeEntries");
        }
    }
}
