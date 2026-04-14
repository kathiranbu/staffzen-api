namespace APM.StaffZen.API.Models
{
    public class TimeEntry
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }

        /// <summary>
        /// The organization this time entry belongs to.
        /// Each org-membership produces independent clock-in/out records.
        /// Null for legacy entries created before multi-org support.
        /// </summary>
        public int? OrganizationId { get; set; }

        public DateTime ClockIn { get; set; }
        public DateTime? ClockOut { get; set; }
        public string? WorkedHours { get; set; }   // e.g. "6 hours 30 mins"
        public bool IsManual     { get; set; } = false;
        public bool IsHourEntry  { get; set; } = false;
        public bool IsBreakEntry { get; set; } = false;

        /// <summary>
        /// Attendance status for this entry: Present | Late | Unmarked | Pending | Absent
        /// Null/empty for entries not yet processed by the background service.
        /// </summary>
        public string? AttendanceStatus { get; set; }

        /// <summary>
        /// The calendar date (date portion of ClockIn) for quick filtering.
        /// Set when AttendanceStatus is stamped by ReminderBasedClockOutService.
        /// </summary>
        public DateTime? AttendanceDate { get; set; }

        /// <summary>
        /// Relative URL of the selfie captured when the employee pressed Clock In.
        /// e.g. /uploads/selfie_in_42_20260401083000.jpg
        /// Null when no selfie was captured (policy off, or manual/hour entry).
        /// Displayed in Timesheets → Time Entries in place of the static profile photo.
        /// </summary>
        public string? ClockInSelfieUrl  { get; set; }

        /// <summary>
        /// Relative URL of the selfie captured when the employee pressed Clock Out.
        /// e.g. /uploads/selfie_out_42_20260401183000.jpg
        /// Null when no selfie was captured.
        /// </summary>
        public string? ClockOutSelfieUrl { get; set; }

        public Employee? Employee { get; set; }
    }
}
