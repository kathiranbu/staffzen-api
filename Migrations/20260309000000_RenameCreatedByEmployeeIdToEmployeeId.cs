using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class RenameCreatedByEmployeeIdToEmployeeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: only renames if the old column still exists
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'Organizations'
                      AND COLUMN_NAME = 'CreatedByEmployeeId'
                )
                BEGIN
                    EXEC sp_rename 'Organizations.CreatedByEmployeeId', 'EmployeeId', 'COLUMN';
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'Organizations'
                      AND COLUMN_NAME = 'EmployeeId'
                )
                BEGIN
                    EXEC sp_rename 'Organizations.EmployeeId', 'CreatedByEmployeeId', 'COLUMN';
                END
            ");
        }
    }
}
