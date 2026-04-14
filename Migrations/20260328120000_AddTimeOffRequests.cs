using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    public partial class AddTimeOffRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TimeOffRequests')
                BEGIN
                    CREATE TABLE TimeOffRequests (
                        Id          INT           IDENTITY(1,1) PRIMARY KEY,
                        EmployeeId  INT           NOT NULL,
                        PolicyId    INT           NOT NULL,
                        StartDate   DATETIME2     NOT NULL,
                        EndDate     DATETIME2     NULL,
                        IsHalfDay   BIT           NOT NULL DEFAULT 0,
                        HalfDayPart NVARCHAR(100) NULL,
                        Reason      NVARCHAR(MAX) NULL,
                        Status      NVARCHAR(20)  NOT NULL DEFAULT N'Pending',
                        CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT FK_TimeOffRequests_Employees
                            FOREIGN KEY (EmployeeId) REFERENCES Employees(Id) ON DELETE CASCADE,
                        CONSTRAINT FK_TimeOffRequests_Policies
                            FOREIGN KEY (PolicyId) REFERENCES TimeOffPolicies(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IX_TimeOffRequests_EmployeeId ON TimeOffRequests(EmployeeId);
                    CREATE INDEX IX_TimeOffRequests_StartDate  ON TimeOffRequests(StartDate);
                END;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID('TimeOffRequests','U') IS NOT NULL
                    DROP TABLE TimeOffRequests;
            ");
        }
    }
}
