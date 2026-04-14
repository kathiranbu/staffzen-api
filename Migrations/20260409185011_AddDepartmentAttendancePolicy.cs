using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentAttendancePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_EmployeeId_OrganizationId",
                table: "TimeEntries");

            migrationBuilder.AlterColumn<string>(
                name: "UnusualBehavior",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "AutoClockOutTime",
                table: "TimeTrackingPolicies",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldDefaultValue: "23:00");

            migrationBuilder.AlterColumn<bool>(
                name: "AutoClockOutEnabled",
                table: "TimeTrackingPolicies",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "AutoClockOutAtTime",
                table: "TimeTrackingPolicies",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "AutoClockOutAfterMins",
                table: "TimeTrackingPolicies",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "AutoClockOutAfterHours",
                table: "TimeTrackingPolicies",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 8);

            migrationBuilder.AlterColumn<bool>(
                name: "AutoClockOutAfterDuration",
                table: "TimeTrackingPolicies",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PolicyType",
                table: "TimeTrackingPolicies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReminderBeforeEndMins",
                table: "TimeTrackingPolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WarningAfterEndMins",
                table: "TimeTrackingPolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceDate",
                table: "TimeEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceStatus",
                table: "TimeEntries",
                type: "nvarchar(max)",
                nullable: true);

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
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WarningAfterEndMins",
                table: "Groups",
                type: "int",
                nullable: false,
                defaultValue: 0);

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
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_EmployeeId_AttendanceDate",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "EmployeeId", "AttendanceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_OrganizationId_Status",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_TimeEntryId",
                table: "AttendanceCorrectionRequests",
                column: "TimeEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "PolicyType",
                table: "TimeTrackingPolicies");

            migrationBuilder.DropColumn(
                name: "ReminderBeforeEndMins",
                table: "TimeTrackingPolicies");

            migrationBuilder.DropColumn(
                name: "WarningAfterEndMins",
                table: "TimeTrackingPolicies");

            migrationBuilder.DropColumn(
                name: "AttendanceDate",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "AttendanceStatus",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "AttendancePolicyType",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ReminderBeforeEndMins",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "WarningAfterEndMins",
                table: "Groups");

            migrationBuilder.AlterColumn<string>(
                name: "UnusualBehavior",
                table: "WorkSchedules",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AutoClockOutTime",
                table: "TimeTrackingPolicies",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "23:00",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<bool>(
                name: "AutoClockOutEnabled",
                table: "TimeTrackingPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<bool>(
                name: "AutoClockOutAtTime",
                table: "TimeTrackingPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<int>(
                name: "AutoClockOutAfterMins",
                table: "TimeTrackingPolicies",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "AutoClockOutAfterHours",
                table: "TimeTrackingPolicies",
                type: "int",
                nullable: false,
                defaultValue: 8,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<bool>(
                name: "AutoClockOutAfterDuration",
                table: "TimeTrackingPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_EmployeeId_OrganizationId",
                table: "TimeEntries",
                columns: new[] { "EmployeeId", "OrganizationId" });
        }
    }
}
