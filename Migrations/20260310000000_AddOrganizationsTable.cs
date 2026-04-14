using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create Organizations table if it doesn't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Organizations')
                BEGIN
                    CREATE TABLE Organizations (
                        Id               INT            IDENTITY(1,1) PRIMARY KEY,
                        Name             NVARCHAR(256)  NOT NULL,
                        Country          NVARCHAR(100)  NULL,
                        PhoneNumber      NVARCHAR(50)   NULL,
                        CountryCode      NVARCHAR(20)   NULL,
                        Industry         NVARCHAR(100)  NULL,
                        OrganizationSize NVARCHAR(50)   NULL,
                        OwnerRole        NVARCHAR(100)  NULL,
                        SelectedDevices  NVARCHAR(MAX)  NULL,
                        EmployeeId       INT            NOT NULL DEFAULT 0,
                        CreatedAt        DATETIME2      NOT NULL DEFAULT GETUTCDATE()
                    );
                END;

                -- Add any missing columns to existing Organizations table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'Country')
                    ALTER TABLE Organizations ADD Country NVARCHAR(100) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'PhoneNumber')
                    ALTER TABLE Organizations ADD PhoneNumber NVARCHAR(50) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'CountryCode')
                    ALTER TABLE Organizations ADD CountryCode NVARCHAR(20) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'Industry')
                    ALTER TABLE Organizations ADD Industry NVARCHAR(100) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'OrganizationSize')
                    ALTER TABLE Organizations ADD OrganizationSize NVARCHAR(50) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'OwnerRole')
                    ALTER TABLE Organizations ADD OwnerRole NVARCHAR(100) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'SelectedDevices')
                    ALTER TABLE Organizations ADD SelectedDevices NVARCHAR(MAX) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'EmployeeId')
                    ALTER TABLE Organizations ADD EmployeeId INT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'CreatedAt')
                    ALTER TABLE Organizations ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE();
            ");

            // Add OrganizationId FK column to Employees if it doesn't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Employees' AND COLUMN_NAME = 'OrganizationId')
                BEGIN
                    ALTER TABLE Employees ADD OrganizationId INT NULL;
                END;
            ");

            // Add FK constraint from Employees.OrganizationId -> Organizations.Id (if not exists)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = 'FK_Employees_Organizations_OrganizationId'
                )
                AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Organizations')
                BEGIN
                    ALTER TABLE Employees
                        ADD CONSTRAINT FK_Employees_Organizations_OrganizationId
                        FOREIGN KEY (OrganizationId)
                        REFERENCES Organizations(Id)
                        ON DELETE SET NULL;
                END;
            ");

            // Create index on Employees.OrganizationId if not exists
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_Employees_OrganizationId'
                      AND object_id = OBJECT_ID('Employees')
                )
                BEGIN
                    CREATE INDEX IX_Employees_OrganizationId ON Employees(OrganizationId);
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = 'FK_Employees_Organizations_OrganizationId'
                )
                    ALTER TABLE Employees DROP CONSTRAINT FK_Employees_Organizations_OrganizationId;

                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Employees' AND COLUMN_NAME = 'OrganizationId')
                    ALTER TABLE Employees DROP COLUMN OrganizationId;

                IF OBJECT_ID('Organizations', 'U') IS NOT NULL
                    DROP TABLE Organizations;
            ");
        }
    }
}
