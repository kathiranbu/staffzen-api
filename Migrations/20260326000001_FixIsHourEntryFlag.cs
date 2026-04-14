using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <summary>
    /// Data-fix migration: back-fills IsHourEntry = true for all TimeEntry rows that
    /// were created via the "Add Hours" feature but have IsHourEntry = false because
    /// they were inserted before the IsHourEntry column was added (migration
    /// 20260312000002_AddIsManualIsHourEntry), or were inserted by an older version
    /// of the code that did not yet set the flag.
    ///
    /// Detection strategy — a row is an hour entry when it has a corresponding
    /// TimeEntryChangeLogs row with Action = 'AddedHour'.  This is 100 % reliable
    /// because the AddManualEntry controller has always written that log action for
    /// the "hour" entry type, even before the IsHourEntry column existed.
    ///
    /// Secondary fallback (no changelog row): treat as an hour entry when
    ///   IsManual = 1 AND ClockOut IS NOT NULL AND WorkedHours IS NOT NULL
    ///   AND the ClockIn time-of-day is exactly 00:00:00 (midnight)
    ///   AND IsBreakEntry = 0
    /// This pattern matches only hour entries; real manual clock-ins recorded at
    /// midnight are extremely rare and would still have IsHourEntry correctly set
    /// by newer code.
    /// </summary>
    public partial class FixIsHourEntryFlag : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only run data-fix if IsHourEntry column exists (guard for safety)
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME  = 'TimeEntries'
                      AND COLUMN_NAME = 'IsHourEntry'
                )
                BEGIN
                    -- Pass 1: use the changelog as the authoritative source
                    UPDATE te
                    SET    te.IsHourEntry = 1
                    FROM   TimeEntries te
                    INNER JOIN TimeEntryChangeLogs cl
                           ON  cl.TimeEntryId = te.Id
                           AND cl.Action      = 'AddedHour'
                    WHERE  te.IsHourEntry = 0;

                    -- Pass 2: pattern-based fallback for rows with no changelog
                    UPDATE TimeEntries
                    SET    IsHourEntry = 1
                    WHERE  IsHourEntry  = 0
                      AND  IsManual     = 1
                      AND  IsBreakEntry = 0
                      AND  ClockOut     IS NOT NULL
                      AND  WorkedHours  IS NOT NULL
                      AND  CONVERT(time, ClockIn) = '00:00:00';
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible — we cannot distinguish which rows were flipped.
        }
    }
}
