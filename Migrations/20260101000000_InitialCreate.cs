using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────
            // All CREATE TABLE statements are wrapped in IF NOT EXISTS so this
            // migration is fully idempotent. It runs safely on a brand-new DB
            // AND on a teammate's PC that already has a partial/old schema.
            // Every ALTER TABLE column addition is also individually guarded.
            // ─────────────────────────────────────────────────────────────────

            // ── EmployeeInvites ───────────────────────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='EmployeeInvites')
                    CREATE TABLE EmployeeInvites (
                        Id         INT           IDENTITY(1,1) PRIMARY KEY,
                        Email      NVARCHAR(MAX) NOT NULL,
                        Role       NVARCHAR(MAX) NOT NULL,
                        Token      NVARCHAR(MAX) NOT NULL,
                        ExpiryDate DATETIME2     NOT NULL,
                        IsUsed     BIT           NOT NULL DEFAULT 0
                    );
            ");

            // ── Groups ────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Groups')
                    CREATE TABLE Groups (
                        Id          INT           IDENTITY(1,1) PRIMARY KEY,
                        Name        NVARCHAR(MAX) NOT NULL,
                        Description NVARCHAR(MAX) NULL,
                        CreatedDate DATETIME2     NOT NULL DEFAULT GETUTCDATE()
                    );
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Groups' AND COLUMN_NAME='Description')
                    ALTER TABLE Groups ADD Description NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Groups' AND COLUMN_NAME='CreatedDate')
                    ALTER TABLE Groups ADD CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE();
            ");

            // ── Employees ─────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Employees')
                    CREATE TABLE Employees (
                        Id                  INT           IDENTITY(1,1) PRIMARY KEY,
                        FullName            NVARCHAR(MAX) NOT NULL,
                        Email               NVARCHAR(MAX) NOT NULL,
                        PasswordHash        NVARCHAR(MAX) NULL,
                        PasswordSalt        NVARCHAR(MAX) NULL,
                        Role                NVARCHAR(MAX) NOT NULL DEFAULT N'user',
                        IsActive            BIT           NOT NULL DEFAULT 0,
                        DateOfBirth         DATETIME2     NULL,
                        MobileNumber        NVARCHAR(MAX) NULL,
                        CountryCode         NVARCHAR(MAX) NULL,
                        ProfileImageUrl     NVARCHAR(MAX) NULL,
                        GroupId             INT           NULL,
                        PasswordResetToken  NVARCHAR(MAX) NULL,
                        PasswordResetExpiry DATETIME2     NULL,
                        FcmToken            NVARCHAR(MAX) NULL
                    );
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='PasswordHash')
                    ALTER TABLE Employees ADD PasswordHash NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='PasswordSalt')
                    ALTER TABLE Employees ADD PasswordSalt NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='DateOfBirth')
                    ALTER TABLE Employees ADD DateOfBirth DATETIME2 NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='MobileNumber')
                    ALTER TABLE Employees ADD MobileNumber NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='CountryCode')
                    ALTER TABLE Employees ADD CountryCode NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='ProfileImageUrl')
                    ALTER TABLE Employees ADD ProfileImageUrl NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='GroupId')
                    ALTER TABLE Employees ADD GroupId INT NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='PasswordResetToken')
                    ALTER TABLE Employees ADD PasswordResetToken NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='PasswordResetExpiry')
                    ALTER TABLE Employees ADD PasswordResetExpiry DATETIME2 NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='FcmToken')
                    ALTER TABLE Employees ADD FcmToken NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='IsOnboarded')
                BEGIN
                    ALTER TABLE Employees ADD IsOnboarded BIT NOT NULL DEFAULT 1;
                END
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Employees' AND COLUMN_NAME='FaceDescriptor')
                    ALTER TABLE Employees ADD FaceDescriptor NVARCHAR(MAX) NULL;
            ");

            // ── TimeEntries ───────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TimeEntries')
                BEGIN
                    CREATE TABLE TimeEntries (
                        Id          INT           IDENTITY(1,1) PRIMARY KEY,
                        EmployeeId  INT           NOT NULL,
                        ClockIn     DATETIME2     NOT NULL,
                        ClockOut    DATETIME2     NULL,
                        WorkedHours NVARCHAR(MAX) NULL,
                        CONSTRAINT FK_TimeEntries_Employees_EmployeeId FOREIGN KEY (EmployeeId)
                            REFERENCES Employees(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IX_TimeEntries_EmployeeId ON TimeEntries(EmployeeId);
                END;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TimeEntries' AND COLUMN_NAME='WorkedHours')
                    ALTER TABLE TimeEntries ADD WorkedHours NVARCHAR(MAX) NULL;
            ");

            // ── TimeEntryChangeLogs ───────────────────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TimeEntryChangeLogs')
                BEGIN
                    CREATE TABLE TimeEntryChangeLogs (
                        Id              INT           IDENTITY(1,1) PRIMARY KEY,
                        TimeEntryId     INT           NOT NULL,
                        ChangedByEmpId  INT           NOT NULL,
                        Action          NVARCHAR(50)  NOT NULL,
                        OldClockIn      DATETIME2     NULL,
                        OldClockOut     DATETIME2     NULL,
                        NewClockIn      DATETIME2     NULL,
                        NewClockOut     DATETIME2     NULL,
                        ReasonForChange NVARCHAR(MAX) NULL,
                        ChangedAt       DATETIME2     NOT NULL,
                        CONSTRAINT FK_TimeEntryChangeLogs_TimeEntries_TimeEntryId FOREIGN KEY (TimeEntryId)
                            REFERENCES TimeEntries(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IX_TimeEntryChangeLogs_TimeEntryId    ON TimeEntryChangeLogs(TimeEntryId);
                    CREATE INDEX IX_TimeEntryChangeLogs_ChangedByEmpId ON TimeEntryChangeLogs(ChangedByEmpId);
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TimeEntryChangeLogs_ChangedByEmpId' AND object_id=OBJECT_ID('TimeEntryChangeLogs'))
                    CREATE INDEX IX_TimeEntryChangeLogs_ChangedByEmpId ON TimeEntryChangeLogs(ChangedByEmpId);
            ");

            // ── EmployeeNotificationSettings ──────────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='EmployeeNotificationSettings')
                BEGIN
                    CREATE TABLE EmployeeNotificationSettings (
                        Id                       INT           IDENTITY(1,1) PRIMARY KEY,
                        EmployeeId               INT           NOT NULL,
                        ReportsChannelEmail      BIT           NOT NULL DEFAULT 1,
                        ReportsChannelWhatsApp   BIT           NOT NULL DEFAULT 0,
                        ReportsChannelSms        BIT           NOT NULL DEFAULT 0,
                        ReportsChannelPush       BIT           NOT NULL DEFAULT 0,
                        RemindersChannelEmail    BIT           NOT NULL DEFAULT 1,
                        RemindersChannelWhatsApp BIT           NOT NULL DEFAULT 0,
                        RemindersChannelSms      BIT           NOT NULL DEFAULT 0,
                        RemindersChannelPush     BIT           NOT NULL DEFAULT 0,
                        NotifDailyAttendance     BIT           NOT NULL DEFAULT 0,
                        DailyAttendanceTime      NVARCHAR(MAX) NOT NULL DEFAULT N'9:00 am',
                        DailyAttendanceFreq      NVARCHAR(MAX) NOT NULL DEFAULT N'everyday',
                        NotifWeeklyActivity      BIT           NOT NULL DEFAULT 1,
                        WeeklyActivityDay        NVARCHAR(MAX) NOT NULL DEFAULT N'Monday',
                        NotifClockIn             BIT           NOT NULL DEFAULT 0,
                        ClockInMinutes           INT           NOT NULL DEFAULT 5,
                        NotifClockOut            BIT           NOT NULL DEFAULT 0,
                        ClockOutMinutes          INT           NOT NULL DEFAULT 5,
                        NotifEndBreak            BIT           NOT NULL DEFAULT 0,
                        EndBreakMinutes          INT           NOT NULL DEFAULT 5,
                        NotifTimeClockStarts     BIT           NOT NULL DEFAULT 0,
                        NotifTimeOffRequests     BIT           NOT NULL DEFAULT 1,
                        SubProductUpdates        BIT           NOT NULL DEFAULT 0,
                        SubPromotions            BIT           NOT NULL DEFAULT 0,
                        SubUsageTracking         BIT           NOT NULL DEFAULT 0
                    );
                    CREATE UNIQUE INDEX IX_EmployeeNotificationSettings_EmployeeId
                        ON EmployeeNotificationSettings(EmployeeId);
                END;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='ReportsChannelEmail')
                    ALTER TABLE EmployeeNotificationSettings ADD ReportsChannelEmail BIT NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='ReportsChannelWhatsApp')
                    ALTER TABLE EmployeeNotificationSettings ADD ReportsChannelWhatsApp BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='ReportsChannelSms')
                    ALTER TABLE EmployeeNotificationSettings ADD ReportsChannelSms BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='ReportsChannelPush')
                    ALTER TABLE EmployeeNotificationSettings ADD ReportsChannelPush BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='RemindersChannelEmail')
                    ALTER TABLE EmployeeNotificationSettings ADD RemindersChannelEmail BIT NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='RemindersChannelWhatsApp')
                    ALTER TABLE EmployeeNotificationSettings ADD RemindersChannelWhatsApp BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='RemindersChannelSms')
                    ALTER TABLE EmployeeNotificationSettings ADD RemindersChannelSms BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='RemindersChannelPush')
                    ALTER TABLE EmployeeNotificationSettings ADD RemindersChannelPush BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='NotifDailyAttendance')
                    ALTER TABLE EmployeeNotificationSettings ADD NotifDailyAttendance BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='DailyAttendanceTime')
                    ALTER TABLE EmployeeNotificationSettings ADD DailyAttendanceTime NVARCHAR(MAX) NOT NULL DEFAULT N'9:00 am';
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='DailyAttendanceFreq')
                    ALTER TABLE EmployeeNotificationSettings ADD DailyAttendanceFreq NVARCHAR(MAX) NOT NULL DEFAULT N'everyday';
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='NotifWeeklyActivity')
                    ALTER TABLE EmployeeNotificationSettings ADD NotifWeeklyActivity BIT NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='WeeklyActivityDay')
                    ALTER TABLE EmployeeNotificationSettings ADD WeeklyActivityDay NVARCHAR(MAX) NOT NULL DEFAULT N'Monday';
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='NotifClockIn')
                    ALTER TABLE EmployeeNotificationSettings ADD NotifClockIn BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='ClockInMinutes')
                    ALTER TABLE EmployeeNotificationSettings ADD ClockInMinutes INT NOT NULL DEFAULT 5;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='NotifClockOut')
                    ALTER TABLE EmployeeNotificationSettings ADD NotifClockOut BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='ClockOutMinutes')
                    ALTER TABLE EmployeeNotificationSettings ADD ClockOutMinutes INT NOT NULL DEFAULT 5;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='NotifEndBreak')
                    ALTER TABLE EmployeeNotificationSettings ADD NotifEndBreak BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='EndBreakMinutes')
                    ALTER TABLE EmployeeNotificationSettings ADD EndBreakMinutes INT NOT NULL DEFAULT 5;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='NotifTimeClockStarts')
                    ALTER TABLE EmployeeNotificationSettings ADD NotifTimeClockStarts BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='NotifTimeOffRequests')
                    ALTER TABLE EmployeeNotificationSettings ADD NotifTimeOffRequests BIT NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='SubProductUpdates')
                    ALTER TABLE EmployeeNotificationSettings ADD SubProductUpdates BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='SubPromotions')
                    ALTER TABLE EmployeeNotificationSettings ADD SubPromotions BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='EmployeeNotificationSettings' AND COLUMN_NAME='SubUsageTracking')
                    ALTER TABLE EmployeeNotificationSettings ADD SubUsageTracking BIT NOT NULL DEFAULT 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('TimeEntryChangeLogs','U') IS NOT NULL DROP TABLE TimeEntryChangeLogs;");
            migrationBuilder.Sql("IF OBJECT_ID('TimeEntries','U')         IS NOT NULL DROP TABLE TimeEntries;");
            migrationBuilder.Sql("IF OBJECT_ID('EmployeeNotificationSettings','U') IS NOT NULL DROP TABLE EmployeeNotificationSettings;");
            migrationBuilder.Sql("IF OBJECT_ID('Employees','U')           IS NOT NULL DROP TABLE Employees;");
            migrationBuilder.Sql("IF OBJECT_ID('Groups','U')              IS NOT NULL DROP TABLE Groups;");
            migrationBuilder.Sql("IF OBJECT_ID('EmployeeInvites','U')     IS NOT NULL DROP TABLE EmployeeInvites;");
        }
    }
}
