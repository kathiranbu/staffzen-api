using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceStatusAndCorrectionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── TimeEntry: AttendanceStatus + AttendanceDate ──────────────────
            migrationBuilder.AddColumn<string>(
                name: "AttendanceStatus",
                table: "TimeEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceDate",
                table: "TimeEntries",
                type: "datetime2",
                nullable: true);

            // ── Group: AttendancePolicyType, ReminderBeforeEndMins, WarningAfterEndMins ─
            migrationBuilder.AddColumn<string>(
                name: "AttendancePolicyType",
                table: "Groups",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderBeforeEndMins",
                table: "Groups",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "WarningAfterEndMins",
                table: "Groups",
                type: "int",
                nullable: false,
                defaultValue: 30);

            // ── AttendanceCorrectionRequests table ────────────────────────────
            migrationBuilder.CreateTable(
                name: "AttendanceCorrectionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    TimeEntryId = table.Column<int>(type: "int", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedClockOut = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Pending"),
                    ApprovedClockOut = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrectionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrectionRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrectionRequests_TimeEntries_TimeEntryId",
                        column: x => x.TimeEntryId,
                        principalTable: "TimeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_EmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_TimeEntryId",
                table: "AttendanceCorrectionRequests",
                column: "TimeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_OrganizationId_Status",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "OrganizationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(name: "AttendancePolicyType", table: "Groups");
            migrationBuilder.DropColumn(name: "ReminderBeforeEndMins", table: "Groups");
            migrationBuilder.DropColumn(name: "WarningAfterEndMins", table: "Groups");

            migrationBuilder.DropColumn(name: "AttendanceStatus", table: "TimeEntries");
            migrationBuilder.DropColumn(name: "AttendanceDate", table: "TimeEntries");
        }
    }
}
