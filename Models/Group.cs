namespace APM.StaffZen.API.Models
{
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Attendance policy type for this department/group.
        /// Values: "AutoClockOut" | "ReminderBased"
        /// ReminderBased: employees receive reminder/warning notifications and must submit correction requests.
        /// AutoClockOut: employees are automatically clocked out by the background service.
        /// </summary>
        public string? AttendancePolicyType { get; set; }

        /// <summary>
        /// For ReminderBased groups: minutes before shift end to send the clock-out reminder.
        /// Default: 15 minutes.
        /// </summary>
        public int ReminderBeforeEndMins { get; set; } = 15;

        /// <summary>
        /// For ReminderBased groups: minutes after shift end to send the missed clock-out warning.
        /// After this window passes the attendance is marked Unmarked.
        /// Default: 30 minutes.
        /// </summary>
        public int WarningAfterEndMins { get; set; } = 30;
    }
}
