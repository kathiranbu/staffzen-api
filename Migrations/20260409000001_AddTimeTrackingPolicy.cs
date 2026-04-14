using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeTrackingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimeTrackingPolicies",
                columns: table => new
                {
                    Id             = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),

                    AutoClockOutEnabled         = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AutoClockOutAfterDuration   = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AutoClockOutAfterHours      = table.Column<int>(type: "int",  nullable: false, defaultValue: 8),
                    AutoClockOutAfterMins       = table.Column<int>(type: "int",  nullable: false, defaultValue: 0),
                    AutoClockOutAtTime          = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AutoClockOutTime            = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false, defaultValue: "23:00")
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

            migrationBuilder.CreateIndex(
                name: "IX_TimeTrackingPolicies_OrganizationId",
                table: "TimeTrackingPolicies",
                column: "OrganizationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TimeTrackingPolicies");
        }
    }
}
