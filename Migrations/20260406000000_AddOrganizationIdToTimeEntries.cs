using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationIdToTimeEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nullable OrganizationId column to TimeEntries.
            // Nullable so legacy rows (before multi-org) remain valid.
            // Guarded with IF NOT EXISTS for full idempotency.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME  = 'TimeEntries'
                      AND COLUMN_NAME = 'OrganizationId'
                )
                    ALTER TABLE TimeEntries
                        ADD OrganizationId INT NULL;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name      = 'IX_TimeEntries_EmployeeId_OrganizationId'
                      AND object_id = OBJECT_ID('TimeEntries')
                )
                    CREATE INDEX IX_TimeEntries_EmployeeId_OrganizationId
                        ON TimeEntries (EmployeeId, OrganizationId);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name      = 'IX_TimeEntries_EmployeeId_OrganizationId'
                      AND object_id = OBJECT_ID('TimeEntries')
                )
                    DROP INDEX IX_TimeEntries_EmployeeId_OrganizationId ON TimeEntries;

                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME  = 'TimeEntries'
                      AND COLUMN_NAME = 'OrganizationId'
                )
                    ALTER TABLE TimeEntries DROP COLUMN OrganizationId;
            ");
        }
    }
}
