using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceDescriptor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded with IF NOT EXISTS so this migration is fully idempotent —
            // safe on a fresh DB, safe on a DB where the column was added manually,
            // and safe when re-running after a previously failed migration attempt.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME  = 'Employees'
                      AND COLUMN_NAME = 'FaceDescriptor'
                )
                    ALTER TABLE Employees
                        ADD FaceDescriptor NVARCHAR(MAX) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME  = 'Employees'
                      AND COLUMN_NAME = 'FaceDescriptor'
                )
                    ALTER TABLE Employees DROP COLUMN FaceDescriptor;
            ");
        }
    }
}
