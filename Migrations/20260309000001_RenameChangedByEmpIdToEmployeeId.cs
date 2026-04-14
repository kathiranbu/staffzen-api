using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class RenameChangedByEmpIdToEmployeeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: This migration is intentionally a no-op.
            // The original intent was to rename ChangedByEmpId → EmployeeId, but
            // TimeEntryChangeLogs already has an EmployeeId column (the entry owner).
            // Renaming would create a duplicate column name. The C# model retains
            // ChangedByEmpId as a separate property mapped explicitly via HasColumnName.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_TimeEntryChangeLogs_EmployeeId'
                      AND object_id = OBJECT_ID('TimeEntryChangeLogs')
                )
                BEGIN
                    DROP INDEX IX_TimeEntryChangeLogs_EmployeeId ON TimeEntryChangeLogs;
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME  = 'TimeEntryChangeLogs'
                      AND COLUMN_NAME = 'EmployeeId'
                )
                BEGIN
                    EXEC sp_rename 'TimeEntryChangeLogs.EmployeeId', 'ChangedByEmpId', 'COLUMN';
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_TimeEntryChangeLogs_ChangedByEmpId'
                      AND object_id = OBJECT_ID('TimeEntryChangeLogs')
                )
                BEGIN
                    CREATE INDEX IX_TimeEntryChangeLogs_ChangedByEmpId ON TimeEntryChangeLogs(ChangedByEmpId);
                END
            ");
        }
    }
}
