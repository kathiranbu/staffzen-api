using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class RenameEmployeesIdToEmployeeId : Migration
    {
        // This migration is intentionally empty.
        // The Employees primary key column stays as "Id" — EF maps Employee.Id to "Id" by convention.
        // HasColumnName("EmployeeId") was removed from DbContext to fix "Invalid column name 'EmployeeId'" errors.
        protected override void Up(MigrationBuilder migrationBuilder) { }
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
