using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeOffPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── TimeOffPolicies ───────────────────────────────────────────────
            // Idempotent: creates the table if it doesn't exist, otherwise
            // applies only the column changes needed to reach the target schema.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TimeOffPolicies')
                BEGIN
                    CREATE TABLE TimeOffPolicies (
                        Id                       INT           IDENTITY(1,1) PRIMARY KEY,
                        Name                     NVARCHAR(200) NOT NULL,
                        CompensationType         NVARCHAR(MAX) NOT NULL DEFAULT '',
                        Unit                     NVARCHAR(MAX) NOT NULL DEFAULT '',
                        AccrualType              NVARCHAR(MAX) NOT NULL DEFAULT '',
                        AnnualEntitlement        FLOAT         NOT NULL DEFAULT 0,
                        ExcludePublicHolidays    BIT           NOT NULL DEFAULT 0,
                        ExcludeNonWorkingDays    BIT           NOT NULL DEFAULT 0,
                        AllowCarryForward        BIT           NOT NULL DEFAULT 0,
                        CarryForwardLimit        FLOAT         NULL,
                        CarryForwardExpiryMonths INT           NULL,
                        IsActive                 BIT           NOT NULL DEFAULT 1,
                        CreatedAt                DATETIME2     NOT NULL DEFAULT '0001-01-01 00:00:00'
                    );
                END
                ELSE
                BEGIN
                    -- Table exists (possibly from old manual migration). Bring it to target schema.

                    -- Drop MaxDaysPerYear if it exists
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='MaxDaysPerYear')
                    BEGIN
                        DECLARE @con1 SYSNAME;
                        SELECT @con1 = d.name FROM sys.default_constraints d
                            JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                            WHERE d.parent_object_id = OBJECT_ID('TimeOffPolicies') AND c.name = 'MaxDaysPerYear';
                        IF @con1 IS NOT NULL EXEC('ALTER TABLE TimeOffPolicies DROP CONSTRAINT [' + @con1 + ']');
                        ALTER TABLE TimeOffPolicies DROP COLUMN MaxDaysPerYear;
                    END

                    -- Rename Type -> Unit if Type exists and Unit doesn't
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='Type')
                       AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='Unit')
                        EXEC sp_rename 'TimeOffPolicies.Type', 'Unit', 'COLUMN';

                    -- Rename RequiresApproval -> IsActive if RequiresApproval exists and IsActive doesn't
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='RequiresApproval')
                       AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='IsActive')
                        EXEC sp_rename 'TimeOffPolicies.RequiresApproval', 'IsActive', 'COLUMN';

                    -- Rename OrganizationId -> CarryForwardExpiryMonths if needed
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='OrganizationId')
                       AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='CarryForwardExpiryMonths')
                        EXEC sp_rename 'TimeOffPolicies.OrganizationId', 'CarryForwardExpiryMonths', 'COLUMN';

                    -- Add missing columns
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='AccrualType')
                        ALTER TABLE TimeOffPolicies ADD AccrualType NVARCHAR(MAX) NOT NULL DEFAULT '';

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='AllowCarryForward')
                        ALTER TABLE TimeOffPolicies ADD AllowCarryForward BIT NOT NULL DEFAULT 0;

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='AnnualEntitlement')
                        ALTER TABLE TimeOffPolicies ADD AnnualEntitlement FLOAT NOT NULL DEFAULT 0;

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='CarryForwardLimit')
                        ALTER TABLE TimeOffPolicies ADD CarryForwardLimit FLOAT NULL;

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='CompensationType')
                        ALTER TABLE TimeOffPolicies ADD CompensationType NVARCHAR(MAX) NOT NULL DEFAULT '';

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='CreatedAt')
                        ALTER TABLE TimeOffPolicies ADD CreatedAt DATETIME2 NOT NULL DEFAULT '0001-01-01 00:00:00';

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='ExcludeNonWorkingDays')
                        ALTER TABLE TimeOffPolicies ADD ExcludeNonWorkingDays BIT NOT NULL DEFAULT 0;

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeOffPolicies' AND COLUMN_NAME='ExcludePublicHolidays')
                        ALTER TABLE TimeOffPolicies ADD ExcludePublicHolidays BIT NOT NULL DEFAULT 0;
                END
            ");

            // ── TimeOffPolicyAssignments ──────────────────────────────────────
            if (!TableExists("TimeOffPolicyAssignments"))
            {
                migrationBuilder.CreateTable(
                    name: "TimeOffPolicyAssignments",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "int", nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        PolicyId = table.Column<int>(type: "int", nullable: false),
                        EmployeeId = table.Column<int>(type: "int", nullable: true),
                        GroupId = table.Column<int>(type: "int", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_TimeOffPolicyAssignments", x => x.Id);
                        table.ForeignKey(
                            name: "FK_TimeOffPolicyAssignments_TimeOffPolicies_PolicyId",
                            column: x => x.PolicyId,
                            principalTable: "TimeOffPolicies",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_TimeOffPolicyAssignments_PolicyId",
                    table: "TimeOffPolicyAssignments",
                    column: "PolicyId");
            }

            // ── Other table changes ───────────────────────────────────────────

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_EmployeeNotificationSettings_EmployeeId')
                    DROP INDEX IX_EmployeeNotificationSettings_EmployeeId ON EmployeeNotificationSettings;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='Street')
                   AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='Address')
                    EXEC sp_rename 'Locations.Address', 'Street', 'COLUMN';
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='OrganizationId')
                BEGIN
                    DECLARE @con2 SYSNAME;
                    SELECT @con2 = d.name FROM sys.default_constraints d
                        JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                        WHERE d.parent_object_id = OBJECT_ID('Locations') AND c.name = 'OrganizationId';
                    IF @con2 IS NOT NULL EXEC('ALTER TABLE Locations DROP CONSTRAINT [' + @con2 + ']');
                    ALTER TABLE Locations DROP COLUMN OrganizationId;
                END
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='RadiusMeters')
                BEGIN
                    DECLARE @con3 SYSNAME;
                    SELECT @con3 = d.name FROM sys.default_constraints d
                        JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                        WHERE d.parent_object_id = OBJECT_ID('Locations') AND c.name = 'RadiusMeters';
                    IF @con3 IS NOT NULL EXEC('ALTER TABLE Locations DROP CONSTRAINT [' + @con3 + ']');
                    ALTER TABLE Locations DROP COLUMN RadiusMeters;
                END
            ");

            migrationBuilder.AlterColumn<string>(
                name: "WorkingDays",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldDefaultValue: "Mon,Tue,Wed,Thu,Fri");

            migrationBuilder.AlterColumn<double>(
                name: "WeeklyOvertimeMultiplier",
                table: "WorkSchedules",
                type: "float",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float",
                oldDefaultValue: 1.5);

            migrationBuilder.AlterColumn<int>(
                name: "WeeklyOvertimeAfterMins",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "WeeklyOvertimeAfterHours",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 40);

            migrationBuilder.AlterColumn<bool>(
                name: "WeeklyOvertime",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "WeeklyMinutes",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "WeeklyHours",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "SplitAt",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10,
                oldDefaultValue: "00:00");

            migrationBuilder.AlterColumn<double>(
                name: "RestDayOvertimeMultiplier",
                table: "WorkSchedules",
                type: "float",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float",
                oldDefaultValue: 1.5);

            migrationBuilder.AlterColumn<bool>(
                name: "RestDayOvertime",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<double>(
                name: "PublicHolidayOvertimeMultiplier",
                table: "WorkSchedules",
                type: "float",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float",
                oldDefaultValue: 1.5);

            migrationBuilder.AlterColumn<bool>(
                name: "PublicHolidayOvertime",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldDefaultValue: "New Schedule");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDefault",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "IncludeBeforeStart",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "DaySlotsJson",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "{}");

            migrationBuilder.AlterColumn<double>(
                name: "DailyOvertimeMultiplier",
                table: "WorkSchedules",
                type: "float",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float",
                oldDefaultValue: 1.5);

            migrationBuilder.AlterColumn<bool>(
                name: "DailyOvertimeIsTime",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "DailyOvertimeAfterMins",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "DailyOvertimeAfterHours",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 8);

            migrationBuilder.AlterColumn<bool>(
                name: "DailyOvertime",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "DailyDoubleOvertime",
                table: "WorkSchedules",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<double>(
                name: "DailyDoubleOTMultiplier",
                table: "WorkSchedules",
                type: "float",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float",
                oldDefaultValue: 1.5);

            migrationBuilder.AlterColumn<int>(
                name: "DailyDoubleOTAfterMins",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "DailyDoubleOTAfterHours",
                table: "WorkSchedules",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 8);

            migrationBuilder.AlterColumn<string>(
                name: "BreaksJson",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "[]");

            migrationBuilder.AlterColumn<string>(
                name: "AutoDeductionsJson",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "[]");

            migrationBuilder.AlterColumn<string>(
                name: "Arrangement",
                table: "WorkSchedules",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Fixed");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "TimeEntryChangeLogs",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsManual",
                table: "TimeEntries",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "IsHourEntry",
                table: "TimeEntries",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "IsBreakEntry",
                table: "TimeEntries",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OwnerRole",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OrganizationSize",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)");

            migrationBuilder.AlterColumn<string>(
                name: "Industry",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Country",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldNullable: true);

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Organizations' AND COLUMN_NAME='SelectedDevices')
                    ALTER TABLE Organizations ADD SelectedDevices NVARCHAR(MAX) NULL;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "OrgRole",
                table: "OrganizationMembers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldDefaultValue: "Employee");

            migrationBuilder.AlterColumn<double>(
                name: "Longitude",
                table: "Locations",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "float",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Latitude",
                table: "Locations",
                type: "float",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "float",
                oldNullable: true);

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='City')
                    ALTER TABLE Locations ADD City NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='Country')
                    ALTER TABLE Locations ADD Country NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='CreatedAt')
                    ALTER TABLE Locations ADD CreatedAt DATETIME2 NOT NULL DEFAULT '0001-01-01 00:00:00';
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='IsArchived')
                    ALTER TABLE Locations ADD IsArchived BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='IsMissing')
                    ALTER TABLE Locations ADD IsMissing BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='PostalCode')
                    ALTER TABLE Locations ADD PostalCode NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='RadiusMetres')
                    ALTER TABLE Locations ADD RadiusMetres INT NOT NULL DEFAULT 0;
            ");

            // HolidayCalendars was never created by any prior migration.
            // Create it if missing; otherwise ensure all required columns exist.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='HolidayCalendars')
                BEGIN
                    CREATE TABLE HolidayCalendars (
                        Id             INT           IDENTITY(1,1) PRIMARY KEY,
                        Name           NVARCHAR(MAX) NOT NULL DEFAULT '',
                        Country        NVARCHAR(MAX) NOT NULL DEFAULT '',
                        IsDefault      BIT           NOT NULL DEFAULT 0,
                        OrganizationId INT           NULL,
                        HolidaysJson   NVARCHAR(MAX) NOT NULL DEFAULT '[]'
                    );
                END
                ELSE
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='HolidayCalendars' AND COLUMN_NAME='IsDefault')
                        ALTER TABLE HolidayCalendars ADD IsDefault BIT NOT NULL DEFAULT 0;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='HolidayCalendars' AND COLUMN_NAME='OrganizationId')
                        ALTER TABLE HolidayCalendars ADD OrganizationId INT NULL;
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='HolidayCalendars' AND COLUMN_NAME='HolidaysJson')
                        ALTER TABLE HolidayCalendars ADD HolidaysJson NVARCHAR(MAX) NOT NULL DEFAULT '[]';
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='HolidayCalendars' AND COLUMN_NAME='Country')
                        ALTER TABLE HolidayCalendars ADD Country NVARCHAR(MAX) NOT NULL DEFAULT '';
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='HolidayCalendars' AND COLUMN_NAME='Name')
                        ALTER TABLE HolidayCalendars ADD Name NVARCHAR(MAX) NOT NULL DEFAULT '';
                END
            ");

            migrationBuilder.AlterColumn<string>(
                name: "WorkSchedule",
                table: "Employees",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldDefaultValue: "Default Work Schedule");

            migrationBuilder.AlterColumn<string>(
                name: "MemberCode",
                table: "Employees",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsOnboarded",
                table: "Employees",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "WeeklyActivityDay",
                table: "EmployeeNotificationSettings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "Monday");

            migrationBuilder.AlterColumn<bool>(
                name: "SubUsageTracking",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "SubPromotions",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "SubProductUpdates",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "ReportsChannelWhatsApp",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "ReportsChannelSms",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "ReportsChannelPush",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "ReportsChannelEmail",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "RemindersChannelWhatsApp",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "RemindersChannelSms",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "RemindersChannelPush",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "RemindersChannelEmail",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifWeeklyActivity",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifTimeOffRequests",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifTimeClockStarts",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifEndBreak",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifDailyAttendance",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifClockOut",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifClockIn",
                table: "EmployeeNotificationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "EndBreakMinutes",
                table: "EmployeeNotificationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 5);

            migrationBuilder.AlterColumn<string>(
                name: "DailyAttendanceTime",
                table: "EmployeeNotificationSettings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "9:00 am");

            migrationBuilder.AlterColumn<string>(
                name: "DailyAttendanceFreq",
                table: "EmployeeNotificationSettings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "everyday");

            migrationBuilder.AlterColumn<int>(
                name: "ClockOutMinutes",
                table: "EmployeeNotificationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 5);

            migrationBuilder.AlterColumn<int>(
                name: "ClockInMinutes",
                table: "EmployeeNotificationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 5);

            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "EmployeeInvites",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "OrgRole",
                table: "EmployeeInvites",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldDefaultValue: "Employee");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_OrganizationMembers_EmployeeId' AND object_id = OBJECT_ID('OrganizationMembers'))
                    CREATE INDEX IX_OrganizationMembers_EmployeeId ON OrganizationMembers(EmployeeId);
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Employees_OrganizationId' AND object_id = OBJECT_ID('Employees'))
                    CREATE INDEX IX_Employees_OrganizationId ON Employees(OrganizationId);
            ");
        }

        // Helper — not called by EF, used inline above via Sql()
        private static bool TableExists(string tableName) => false; // always create via SQL block

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeOffPolicyAssignments");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationMembers_EmployeeId",
                table: "OrganizationMembers");

            migrationBuilder.DropIndex(
                name: "IX_Employees_OrganizationId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "AccrualType",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "AllowCarryForward",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "AnnualEntitlement",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "CarryForwardLimit",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "CompensationType",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "ExcludeNonWorkingDays",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "ExcludePublicHolidays",
                table: "TimeOffPolicies");

            migrationBuilder.DropColumn(
                name: "SelectedDevices",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "IsMissing",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "RadiusMetres",
                table: "Locations");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='HolidayCalendars' AND COLUMN_NAME='IsDefault')
                BEGIN
                    DECLARE @con SYSNAME;
                    SELECT @con = d.name FROM sys.default_constraints d
                        JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                        WHERE d.parent_object_id = OBJECT_ID('HolidayCalendars') AND c.name = 'IsDefault';
                    IF @con IS NOT NULL EXEC('ALTER TABLE HolidayCalendars DROP CONSTRAINT [' + @con + ']');
                    ALTER TABLE HolidayCalendars DROP COLUMN IsDefault;
                END
            ");

            migrationBuilder.RenameColumn(
                name: "Unit",
                table: "TimeOffPolicies",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "TimeOffPolicies",
                newName: "RequiresApproval");

            migrationBuilder.RenameColumn(
                name: "CarryForwardExpiryMonths",
                table: "TimeOffPolicies",
                newName: "OrganizationId");

            migrationBuilder.RenameColumn(
                name: "Street",
                table: "Locations",
                newName: "Address");

            migrationBuilder.AddColumn<int>(
                name: "MaxDaysPerYear",
                table: "TimeOffPolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Locations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RadiusMeters",
                table: "Locations",
                type: "float",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeNotificationSettings_EmployeeId",
                table: "EmployeeNotificationSettings",
                column: "EmployeeId",
                unique: true);
        }
    }
}
