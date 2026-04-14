using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberCodeAndWorkSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MemberCode",
                table: "Employees",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkSchedule",
                table: "Employees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                defaultValue: "Default Work Schedule");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MemberCode",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WorkSchedule",
                table: "Employees");
        }
    }
}
