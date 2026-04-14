namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Submitted by an employee when their attendance status is "Unmarked"
    /// (i.e. they forgot to clock out in a ReminderBased department).
    /// Admin reviews and approves / rejects / edits the requested clock-out time.
    ///
    /// Status flow:
    ///   Employee submits  →  Pending
    ///   Admin approves    →  Approved   (ClockOut written back to TimeEntry, status recalculated)
    ///   Admin rejects     →  Rejected
    /// </summary>
    public class AttendanceCorrectionRequest
    {
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public int OrganizationId { get; set; }

        /// <summary>The TimeEntry row whose ClockOut is missing.</summary>
        public int TimeEntryId { get; set; }

        /// <summary>Date of the attendance record (date portion of ClockIn).</summary>
        public DateTime AttendanceDate { get; set; }

        /// <summary>The clock-out time the employee is requesting (UTC).</summary>
        public DateTime RequestedClockOut { get; set; }

        /// <summary>Reason provided by the employee.</summary>
        public string? Reason { get; set; }

        /// <summary>Pending | Approved | Rejected</summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// The clock-out time the admin set when approving.
        /// May differ from RequestedClockOut if admin edited it.
        /// </summary>
        public DateTime? ApprovedClockOut { get; set; }

        /// <summary>Optional note from the admin on rejection.</summary>
        public string? AdminNote { get; set; }

        /// <summary>EmployeeId of the admin who acted on the request.</summary>
        public int? ReviewedByEmployeeId { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Employee? Employee { get; set; }
        public TimeEntry? TimeEntry { get; set; }
    }
}
