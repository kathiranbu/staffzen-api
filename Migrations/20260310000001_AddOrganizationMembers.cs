using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    public partial class AddOrganizationMembers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'OrganizationMembers')
                BEGIN
                    CREATE TABLE OrganizationMembers (
                        Id             INT           IDENTITY(1,1) PRIMARY KEY,
                        OrganizationId INT           NOT NULL,
                        EmployeeId     INT           NOT NULL,
                        OrgRole        NVARCHAR(50)  NOT NULL DEFAULT N'Employee',
                        JoinedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                        IsActive       BIT           NOT NULL DEFAULT 1,
                        CONSTRAINT FK_OrgMembers_Organizations FOREIGN KEY (OrganizationId)
                            REFERENCES Organizations(Id) ON DELETE CASCADE,
                        CONSTRAINT FK_OrgMembers_Employees FOREIGN KEY (EmployeeId)
                            REFERENCES Employees(Id) ON DELETE CASCADE,
                        CONSTRAINT UQ_OrgMembers UNIQUE (OrganizationId, EmployeeId)
                    );
                    CREATE INDEX IX_OrgMembers_OrgId     ON OrganizationMembers(OrganizationId);
                    CREATE INDEX IX_OrgMembers_EmpId     ON OrganizationMembers(EmployeeId);
                END;
            ");

            // Seed existing org owners into OrganizationMembers so nothing breaks
            migrationBuilder.Sql(@"
                INSERT INTO OrganizationMembers (OrganizationId, EmployeeId, OrgRole, JoinedAt, IsActive)
                SELECT o.Id, o.EmployeeId, 'Admin', o.CreatedAt, 1
                FROM Organizations o
                WHERE NOT EXISTS (
                    SELECT 1 FROM OrganizationMembers m
                    WHERE m.OrganizationId = o.Id AND m.EmployeeId = o.EmployeeId
                );

                -- Also seed all employees that have OrganizationId set (were added via old invite flow)
                INSERT INTO OrganizationMembers (OrganizationId, EmployeeId, OrgRole, JoinedAt, IsActive)
                SELECT e.OrganizationId, e.Id, e.Role, GETUTCDATE(), e.IsActive
                FROM Employees e
                WHERE e.OrganizationId IS NOT NULL
                  AND e.IsActive = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM OrganizationMembers m
                      WHERE m.OrganizationId = e.OrganizationId AND m.EmployeeId = e.Id
                  );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID('OrganizationMembers','U') IS NOT NULL
                    DROP TABLE OrganizationMembers;
            ");
        }
    }
}
