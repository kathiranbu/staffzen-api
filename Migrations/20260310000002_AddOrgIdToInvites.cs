using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    public partial class AddOrgIdToInvites : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME = 'EmployeeInvites' AND COLUMN_NAME = 'OrganizationId')
                    ALTER TABLE EmployeeInvites ADD OrganizationId INT NOT NULL DEFAULT 0;

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME = 'EmployeeInvites' AND COLUMN_NAME = 'OrgRole')
                    ALTER TABLE EmployeeInvites ADD OrgRole NVARCHAR(50) NOT NULL DEFAULT N'Employee';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
