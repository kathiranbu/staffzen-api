using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    public partial class AddWorkSchedulesTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkSchedules",
                columns: table => new
                {
                    Id                              = table.Column<int>(type: "int", nullable: false)
                                                          .Annotation("SqlServer:Identity", "1, 1"),
                    Name                            = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: "New Schedule"),
                    IsDefault                       = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    Arrangement                     = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,  nullable: false, defaultValue: "Fixed"),
                    WorkingDays                     = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "Mon,Tue,Wed,Thu,Fri"),
                    DaySlotsJson                    = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    IncludeBeforeStart              = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    WeeklyHours                     = table.Column<int>(type: "int",              nullable: false, defaultValue: 0),
                    WeeklyMinutes                   = table.Column<int>(type: "int",              nullable: false, defaultValue: 0),
                    SplitAt                         = table.Column<string>(type: "nvarchar(10)",  maxLength: 10,  nullable: false, defaultValue: "00:00"),
                    BreaksJson                      = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    AutoDeductionsJson              = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    DailyOvertime                   = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    DailyOvertimeIsTime             = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    DailyOvertimeAfterHours         = table.Column<int>(type: "int",              nullable: false, defaultValue: 8),
                    DailyOvertimeAfterMins          = table.Column<int>(type: "int",              nullable: false, defaultValue: 0),
                    DailyOvertimeMultiplier         = table.Column<double>(type: "float",         nullable: false, defaultValue: 1.5),
                    DailyDoubleOvertime             = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    DailyDoubleOTAfterHours         = table.Column<int>(type: "int",              nullable: false, defaultValue: 8),
                    DailyDoubleOTAfterMins          = table.Column<int>(type: "int",              nullable: false, defaultValue: 0),
                    DailyDoubleOTMultiplier         = table.Column<double>(type: "float",         nullable: false, defaultValue: 1.5),
                    WeeklyOvertime                  = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    WeeklyOvertimeAfterHours        = table.Column<int>(type: "int",              nullable: false, defaultValue: 40),
                    WeeklyOvertimeAfterMins         = table.Column<int>(type: "int",              nullable: false, defaultValue: 0),
                    WeeklyOvertimeMultiplier        = table.Column<double>(type: "float",         nullable: false, defaultValue: 1.5),
                    RestDayOvertime                 = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    RestDayOvertimeMultiplier       = table.Column<double>(type: "float",         nullable: false, defaultValue: 1.5),
                    PublicHolidayOvertime           = table.Column<bool>(type: "bit",             nullable: false, defaultValue: false),
                    PublicHolidayOvertimeMultiplier = table.Column<double>(type: "float",         nullable: false, defaultValue: 1.5),
                    OrganizationId                  = table.Column<int>(type: "int",              nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WorkSchedules", x => x.Id));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkSchedules");
        }
    }
}
