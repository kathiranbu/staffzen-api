using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeInvites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    OrgRole = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeInvites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: true),
                    Speed = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeNotificationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    ReportsChannelEmail = table.Column<bool>(type: "boolean", nullable: false),
                    ReportsChannelWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    ReportsChannelSms = table.Column<bool>(type: "boolean", nullable: false),
                    ReportsChannelPush = table.Column<bool>(type: "boolean", nullable: false),
                    RemindersChannelEmail = table.Column<bool>(type: "boolean", nullable: false),
                    RemindersChannelWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    RemindersChannelSms = table.Column<bool>(type: "boolean", nullable: false),
                    RemindersChannelPush = table.Column<bool>(type: "boolean", nullable: false),
                    NotifDailyAttendance = table.Column<bool>(type: "boolean", nullable: false),
                    DailyAttendanceTime = table.Column<string>(type: "text", nullable: false),
                    DailyAttendanceFreq = table.Column<string>(type: "text", nullable: false),
                    NotifWeeklyActivity = table.Column<bool>(type: "boolean", nullable: false),
                    WeeklyActivityDay = table.Column<string>(type: "text", nullable: false),
                    NotifClockIn = table.Column<bool>(type: "boolean", nullable: false),
                    ClockInMinutes = table.Column<int>(type: "integer", nullable: false),
                    NotifClockOut = table.Column<bool>(type: "boolean", nullable: false),
                    ClockOutMinutes = table.Column<int>(type: "integer", nullable: false),
                    NotifEndBreak = table.Column<bool>(type: "boolean", nullable: false),
                    EndBreakMinutes = table.Column<int>(type: "integer", nullable: false),
                    NotifTimeClockStarts = table.Column<bool>(type: "boolean", nullable: false),
                    NotifTimeOffRequests = table.Column<bool>(type: "boolean", nullable: false),
                    SubProductUpdates = table.Column<bool>(type: "boolean", nullable: false),
                    SubPromotions = table.Column<bool>(type: "boolean", nullable: false),
                    SubUsageTracking = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeNotificationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttendancePolicyType = table.Column<string>(type: "text", nullable: true),
                    ReminderBeforeEndMins = table.Column<int>(type: "integer", nullable: false),
                    WarningAfterEndMins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HolidayCalendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    HolidaysJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolidayCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Street = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    PostalCode = table.Column<string>(type: "text", nullable: true),
                    RadiusMetres = table.Column<int>(type: "integer", nullable: false),
                    IsMissing = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    CountryCode = table.Column<string>(type: "text", nullable: true),
                    Industry = table.Column<string>(type: "text", nullable: true),
                    OrganizationSize = table.Column<string>(type: "text", nullable: true),
                    OwnerRole = table.Column<string>(type: "text", nullable: true),
                    SelectedDevices = table.Column<string>(type: "text", nullable: true),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CompensationType = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    AccrualType = table.Column<string>(type: "text", nullable: false),
                    AnnualEntitlement = table.Column<double>(type: "double precision", nullable: false),
                    ExcludePublicHolidays = table.Column<bool>(type: "boolean", nullable: false),
                    ExcludeNonWorkingDays = table.Column<bool>(type: "boolean", nullable: false),
                    AllowCarryForward = table.Column<bool>(type: "boolean", nullable: false),
                    CarryForwardLimit = table.Column<double>(type: "double precision", nullable: true),
                    CarryForwardExpiryMonths = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Arrangement = table.Column<string>(type: "text", nullable: false),
                    WorkingDays = table.Column<string>(type: "text", nullable: false),
                    DaySlotsJson = table.Column<string>(type: "text", nullable: false),
                    IncludeBeforeStart = table.Column<bool>(type: "boolean", nullable: false),
                    WeeklyHours = table.Column<int>(type: "integer", nullable: false),
                    WeeklyMinutes = table.Column<int>(type: "integer", nullable: false),
                    SplitAt = table.Column<string>(type: "text", nullable: false),
                    BreaksJson = table.Column<string>(type: "text", nullable: false),
                    AutoDeductionsJson = table.Column<string>(type: "text", nullable: false),
                    GraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    DailyOvertime = table.Column<bool>(type: "boolean", nullable: false),
                    DailyOvertimeIsTime = table.Column<bool>(type: "boolean", nullable: false),
                    DailyOvertimeAfterHours = table.Column<int>(type: "integer", nullable: false),
                    DailyOvertimeAfterMins = table.Column<int>(type: "integer", nullable: false),
                    DailyOvertimeMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    DailyDoubleOvertime = table.Column<bool>(type: "boolean", nullable: false),
                    DailyDoubleOTAfterHours = table.Column<int>(type: "integer", nullable: false),
                    DailyDoubleOTAfterMins = table.Column<int>(type: "integer", nullable: false),
                    DailyDoubleOTMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    WeeklyOvertime = table.Column<bool>(type: "boolean", nullable: false),
                    WeeklyOvertimeAfterHours = table.Column<int>(type: "integer", nullable: false),
                    WeeklyOvertimeAfterMins = table.Column<int>(type: "integer", nullable: false),
                    WeeklyOvertimeMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    RestDayOvertime = table.Column<bool>(type: "boolean", nullable: false),
                    RestDayOvertimeMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    PublicHolidayOvertime = table.Column<bool>(type: "boolean", nullable: false),
                    PublicHolidayOvertimeMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    RequireFaceVerification = table.Column<bool>(type: "boolean", nullable: false),
                    RequireSelfie = table.Column<bool>(type: "boolean", nullable: false),
                    UnusualBehavior = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    PasswordSalt = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MobileNumber = table.Column<string>(type: "text", nullable: true),
                    CountryCode = table.Column<string>(type: "text", nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "text", nullable: true),
                    GroupId = table.Column<int>(type: "integer", nullable: true),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "text", nullable: true),
                    PasswordResetExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FcmToken = table.Column<string>(type: "text", nullable: true),
                    IsOnboarded = table.Column<bool>(type: "boolean", nullable: false),
                    MemberCode = table.Column<string>(type: "text", nullable: true),
                    WorkSchedule = table.Column<string>(type: "text", nullable: true),
                    FaceDescriptor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TimeTrackingPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    AutoClockOutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoClockOutAfterDuration = table.Column<bool>(type: "boolean", nullable: false),
                    AutoClockOutAfterHours = table.Column<int>(type: "integer", nullable: false),
                    AutoClockOutAfterMins = table.Column<int>(type: "integer", nullable: false),
                    AutoClockOutAtTime = table.Column<bool>(type: "boolean", nullable: false),
                    AutoClockOutTime = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTrackingPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeTrackingPolicies_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffPolicyAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PolicyId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: true),
                    GroupId = table.Column<int>(type: "integer", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    LeaveTypeId = table.Column<int>(type: "integer", nullable: true),
                    LeaveTypeName = table.Column<string>(type: "text", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ReviewedBy = table.Column<string>(type: "text", nullable: true),
                    RejectReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ReferenceId = table.Column<int>(type: "integer", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Employees_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    OrgRole = table.Column<string>(type: "text", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    ClockIn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClockOut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkedHours = table.Column<string>(type: "text", nullable: true),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    IsHourEntry = table.Column<bool>(type: "boolean", nullable: false),
                    IsBreakEntry = table.Column<bool>(type: "boolean", nullable: false),
                    AttendanceStatus = table.Column<string>(type: "text", nullable: true),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClockInSelfieUrl = table.Column<string>(type: "text", nullable: true),
                    ClockOutSelfieUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    PolicyId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsHalfDay = table.Column<bool>(type: "boolean", nullable: false),
                    HalfDayPart = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeOffRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimeOffRequests_TimeOffPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "TimeOffPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceCorrectionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    TimeEntryId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedClockOut = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ApprovedClockOut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminNote = table.Column<string>(type: "text", nullable: true),
                    ReviewedByEmployeeId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "TimeEntryChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimeEntryId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    OldClockIn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OldClockOut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewClockIn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewClockOut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReasonForChange = table.Column<string>(type: "text", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntryChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntryChangeLogs_TimeEntries_TimeEntryId",
                        column: x => x.TimeEntryId,
                        principalTable: "TimeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLocations_EmployeeId_RecordedAt",
                table: "EmployeeLocations",
                columns: new[] { "EmployeeId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_OrganizationId",
                table: "Employees",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId",
                table: "LeaveRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId",
                table: "Notifications",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_EmployeeId",
                table: "OrganizationMembers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_OrganizationId_EmployeeId",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_EmployeeId",
                table: "TimeEntries",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryChangeLogs_TimeEntryId",
                table: "TimeEntryChangeLogs",
                column: "TimeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffPolicyAssignments_PolicyId",
                table: "TimeOffPolicyAssignments",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_EmployeeId",
                table: "TimeOffRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_PolicyId",
                table: "TimeOffRequests",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTrackingPolicies_OrganizationId",
                table: "TimeTrackingPolicies",
                column: "OrganizationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCorrectionRequests");

            migrationBuilder.DropTable(
                name: "EmployeeInvites");

            migrationBuilder.DropTable(
                name: "EmployeeLocations");

            migrationBuilder.DropTable(
                name: "EmployeeNotificationSettings");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "HolidayCalendars");

            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OrganizationMembers");

            migrationBuilder.DropTable(
                name: "TimeEntryChangeLogs");

            migrationBuilder.DropTable(
                name: "TimeOffPolicyAssignments");

            migrationBuilder.DropTable(
                name: "TimeOffRequests");

            migrationBuilder.DropTable(
                name: "TimeTrackingPolicies");

            migrationBuilder.DropTable(
                name: "WorkSchedules");

            migrationBuilder.DropTable(
                name: "TimeEntries");

            migrationBuilder.DropTable(
                name: "TimeOffPolicies");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Organizations");
        }
    }
}
