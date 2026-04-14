using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <summary>
    /// Creates the Locations table for geofence-based attendance tracking.
    /// IsMissing flag distinguishes pin-dragged "missing" entries from normal ones.
    /// Fully idempotent — safe to run on any existing schema.
    /// </summary>
    public partial class AddLocations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Locations')
                BEGIN
                    CREATE TABLE Locations (
                        Id           INT           IDENTITY(1,1) PRIMARY KEY,
                        Name         NVARCHAR(255) NOT NULL,
                        Latitude     FLOAT         NOT NULL,
                        Longitude    FLOAT         NOT NULL,
                        Street       NVARCHAR(MAX) NULL,
                        City         NVARCHAR(255) NULL,
                        Country      NVARCHAR(255) NULL,
                        PostalCode   NVARCHAR(50)  NULL,
                        RadiusMetres INT           NOT NULL DEFAULT 300,
                        IsMissing    BIT           NOT NULL DEFAULT 0,
                        IsArchived   BIT           NOT NULL DEFAULT 0,
                        CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE()
                    );
                    CREATE INDEX IX_Locations_IsArchived ON Locations(IsArchived);
                END;

                -- Idempotent column guards
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='IsMissing')
                    ALTER TABLE Locations ADD IsMissing BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='IsArchived')
                    ALTER TABLE Locations ADD IsArchived BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Locations' AND COLUMN_NAME='CreatedAt')
                    ALTER TABLE Locations ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE();
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Locations')
                    DROP TABLE Locations;
            ");
        }
    }
}
