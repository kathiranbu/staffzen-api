namespace APM.StaffZen.API.Models
{
    public class WorkSchedule
    {
        public int    Id          { get; set; }
        public string Name        { get; set; } = "New Schedule";
        public bool   IsDefault   { get; set; } = false;
        public string Arrangement { get; set; } = "Fixed"; // Fixed | Flexible | Weekly

        // Working days as comma-separated string: "Mon,Tue,Wed,Thu,Fri"
        public string WorkingDays { get; set; } = "Mon,Tue,Wed,Thu,Fri";

        // Per-day slots stored as JSON: { "Mon": {"start":"09:00","end":"17:00","dailyHours":8.0}, ... }
        public string DaySlotsJson { get; set; } = "{}";

        public bool   IncludeBeforeStart { get; set; } = false;
        public int    WeeklyHours        { get; set; } = 0;
        public int    WeeklyMinutes      { get; set; } = 0;
        public string SplitAt            { get; set; } = "00:00";

        // Breaks / auto-deductions stored as JSON arrays
        public string BreaksJson         { get; set; } = "[]";
        public string AutoDeductionsJson { get; set; } = "[]";

        /// <summary>
        /// Grace period in minutes after the scheduled start time before an employee
        /// is considered Late. Default: 0 (no grace period).
        /// </summary>
        public int GraceMinutes { get; set; } = 0;

        // Overtime
        public bool   DailyOvertime              { get; set; }
        public bool   DailyOvertimeIsTime        { get; set; }
        public int    DailyOvertimeAfterHours    { get; set; } = 8;
        public int    DailyOvertimeAfterMins     { get; set; }
        public double DailyOvertimeMultiplier    { get; set; } = 1.5;

        public bool   DailyDoubleOvertime        { get; set; }
        public int    DailyDoubleOTAfterHours    { get; set; } = 10;
        public int    DailyDoubleOTAfterMins     { get; set; }
        public double DailyDoubleOTMultiplier    { get; set; } = 1.5;

        public bool   WeeklyOvertime             { get; set; }
        public int    WeeklyOvertimeAfterHours   { get; set; } = 40;
        public int    WeeklyOvertimeAfterMins    { get; set; }
        public double WeeklyOvertimeMultiplier   { get; set; } = 1.5;

        public bool   RestDayOvertime            { get; set; }
        public double RestDayOvertimeMultiplier  { get; set; } = 1.5;

        public bool   PublicHolidayOvertime         { get; set; }
        public double PublicHolidayOvertimeMultiplier { get; set; } = 1.5;

        public int? OrganizationId { get; set; }

        // ── Time Tracking Policy → Verification ──────────────────────────────
        /// <summary>
        /// Master checkbox: "Require verification by Face Recognition".
        /// When true, RequireSelfie is automatically forced true (enforced in
        /// WorkSchedulesController). The kiosk face-recognition flow
        /// (ClockInWithFace endpoint) is gated by this flag.
        /// </summary>
        public bool RequireFaceVerification { get; set; } = false;

        /// <summary>
        /// Sub-checkbox: "Require selfies when clocking in and out".
        /// Can be enabled independently of face recognition.
        /// Always true when RequireFaceVerification is true.
        /// When true, ClockIn / ClockOut endpoints reject requests
        /// that arrive without a SelfieBase64 photo.
        /// </summary>
        public bool RequireSelfie { get; set; } = false;

        /// <summary>
        /// Dropdown for unusual-behaviour handling during face verification.
        /// Valid values: "Blocked" | "Flagged" | "Allowed"
        /// Default: "Blocked" (matches Jibble default).
        /// </summary>
        public string UnusualBehavior { get; set; } = "Blocked";
    }
}
