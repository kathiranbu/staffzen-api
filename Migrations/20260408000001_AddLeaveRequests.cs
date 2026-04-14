using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId   = table.Column<int>(type: "int", nullable: false),
                    StartDate    = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate      = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason       = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status       = table.Column<string>(type: "nvarchar(max)", nullable: false,
                                      defaultValue: "Pending_TeamLead"),
                    ReviewedBy   = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt    = table.Column<DateTime>(type: "datetime2", nullable: false,
                                      defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId",
                table: "LeaveRequests",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LeaveRequests");
        }
    }
}
