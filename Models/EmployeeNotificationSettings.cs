namespace APM.StaffZen.API.Models
{
    public class EmployeeNotificationSettings
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }

        // ── Report channels ──────────────────────────────────
        public bool ReportsChannelEmail    { get; set; } = true;
        public bool ReportsChannelWhatsApp { get; set; } = false;
        public bool ReportsChannelSms      { get; set; } = false;

        public bool ReportsChannelPush   { get; set; } = false;

        // ── Reminder/Alert channels ───────────────────────────
        public bool RemindersChannelEmail    { get; set; } = true;
        public bool RemindersChannelWhatsApp { get; set; } = false;
        public bool RemindersChannelSms      { get; set; } = false;

        public bool RemindersChannelPush   { get; set; } = false;

        // ── Reports ──────────────────────────────────────────
        public bool NotifDailyAttendance { get; set; } = false;
        public string DailyAttendanceTime { get; set; } = "9:00 am";
        public string DailyAttendanceFreq { get; set; } = "everyday";

        public bool NotifWeeklyActivity { get; set; } = true;
        public string WeeklyActivityDay { get; set; } = "Monday";

        // ── Reminders ────────────────────────────────────────
        public bool NotifClockIn { get; set; } = false;
        public int ClockInMinutes { get; set; } = 5;

        public bool NotifClockOut { get; set; } = false;
        public int ClockOutMinutes { get; set; } = 5;

        public bool NotifEndBreak { get; set; } = false;
        public int EndBreakMinutes { get; set; } = 5;

        // ── Alerts ───────────────────────────────────────────
        public bool NotifTimeClockStarts { get; set; } = false;
        public bool NotifTimeOffRequests { get; set; } = true;

        // ── Subscriptions ────────────────────────────────────
        public bool SubProductUpdates { get; set; } = false;
        public bool SubPromotions { get; set; } = false;
        public bool SubUsageTracking { get; set; } = false;
    }
}
