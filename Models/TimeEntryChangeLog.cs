namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Audit log for every manual change made to a TimeEntry.
    /// EmployeeId stores the employee who made the change (was renamed from ChangedByEmpId).
    /// </summary>
    public class TimeEntryChangeLog
    {
        public int       Id              { get; set; }
        public int       TimeEntryId     { get; set; }   // FK → TimeEntries (cascade in DB)
        public int       EmployeeId      { get; set; }   // who made the change (renamed from ChangedByEmpId)
        public string    Action          { get; set; } = "";
        public DateTime? OldClockIn      { get; set; }
        public DateTime? OldClockOut     { get; set; }
        public DateTime? NewClockIn      { get; set; }
        public DateTime? NewClockOut     { get; set; }
        public string?   ReasonForChange { get; set; }
        public DateTime  ChangedAt       { get; set; } = DateTime.UtcNow;

        // Only TimeEntry nav kept — it IS mapped in the migration.
        // ChangedBy nav REMOVED — caused EF to enforce a non-existent FK → SaveChanges failed.
        public TimeEntry? TimeEntry { get; set; }
    }
}
