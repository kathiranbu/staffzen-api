using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <summary>
    /// Adds three Time Tracking Policy (Verification) columns to WorkSchedules
    /// and two selfie-photo columns to TimeEntries so that clock-in / clock-out
    /// selfies are stored and displayed in the Timesheets → Time Entries view.
    ///
    /// All ALTER TABLE statements are individually guarded so this migration is
    /// fully idempotent — safe to run on databases that were already patched
    /// manually, and safe to run on a fresh schema.
    /// </summary>
    public partial class AddSelfieAndVerificationPolicy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── WorkSchedules: Verification policy columns ────────────────────
            //
            //  RequireFaceVerification  – master checkbox "Require verification by
            //                             Face Recognition". When true, both
            //                             RequireSelfie and UnusualBehavior are
            //                             also active (enforced in the API layer).
            //
            //  RequireSelfie            – "Require selfies when clocking in and out".
            //                             Automatically set to true when
            //                             RequireFaceVerification is true.
            //
            //  UnusualBehavior          – dropdown value: 'Blocked' | 'Flagged' | 'Allowed'.
            //                             Default = 'Blocked' (matches Jibble default).
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME='WorkSchedules'
                                 AND COLUMN_NAME='RequireFaceVerification')
                    ALTER TABLE WorkSchedules
                        ADD RequireFaceVerification BIT NOT NULL DEFAULT 0;

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME='WorkSchedules'
                                 AND COLUMN_NAME='RequireSelfie')
                    ALTER TABLE WorkSchedules
                        ADD RequireSelfie BIT NOT NULL DEFAULT 0;

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME='WorkSchedules'
                                 AND COLUMN_NAME='UnusualBehavior')
                    ALTER TABLE WorkSchedules
                        ADD UnusualBehavior NVARCHAR(20) NOT NULL DEFAULT N'Blocked';
            ");

            // ── TimeEntries: selfie photo URL columns ─────────────────────────
            //
            //  ClockInSelfieUrl   – relative URL of the selfie captured when the
            //                       employee presses Clock In, e.g.
            //                       /uploads/selfie_in_42_20260401083000.jpg
            //                       NULL when no selfie was captured.
            //
            //  ClockOutSelfieUrl  – same but for Clock Out.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME='TimeEntries'
                                 AND COLUMN_NAME='ClockInSelfieUrl')
                    ALTER TABLE TimeEntries
                        ADD ClockInSelfieUrl NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME='TimeEntries'
                                 AND COLUMN_NAME='ClockOutSelfieUrl')
                    ALTER TABLE TimeEntries
                        ADD ClockOutSelfieUrl NVARCHAR(MAX) NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='WorkSchedules'
                             AND COLUMN_NAME='RequireFaceVerification')
                    ALTER TABLE WorkSchedules DROP COLUMN RequireFaceVerification;

                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='WorkSchedules'
                             AND COLUMN_NAME='RequireSelfie')
                    ALTER TABLE WorkSchedules DROP COLUMN RequireSelfie;

                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='WorkSchedules'
                             AND COLUMN_NAME='UnusualBehavior')
                    ALTER TABLE WorkSchedules DROP COLUMN UnusualBehavior;

                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='TimeEntries'
                             AND COLUMN_NAME='ClockInSelfieUrl')
                    ALTER TABLE TimeEntries DROP COLUMN ClockInSelfieUrl;

                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='TimeEntries'
                             AND COLUMN_NAME='ClockOutSelfieUrl')
                    ALTER TABLE TimeEntries DROP COLUMN ClockOutSelfieUrl;
            ");
        }
    }
}
