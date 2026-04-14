using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveTypeToLeaveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeaveTypeId",
                table: "LeaveRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaveTypeName",
                table: "LeaveRequests",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaveTypeId",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "LeaveTypeName",
                table: "LeaveRequests");
        }
    }
}
