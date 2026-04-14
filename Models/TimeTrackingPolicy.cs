namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Stores the automatic clock-out (and other time-tracking policy) settings
    /// for an organization. One row per organization.
    /// </summary>
    public class TimeTrackingPolicy
    {
        public int Id             { get; set; }
        public int OrganizationId { get; set; }

        // ── Automatic Clock Out ──────────────────────────────────────────
        /// <summary>Master toggle: auto clock-out is enabled for this org.</summary>
        public bool AutoClockOutEnabled { get; set; } = false;

        /// <summary>Clock everyone out after a fixed duration (AfterHours:AfterMins).</summary>
        public bool AutoClockOutAfterDuration { get; set; } = false;
        public int  AutoClockOutAfterHours    { get; set; } = 8;
        public int  AutoClockOutAfterMins     { get; set; } = 0;

        /// <summary>Clock everyone out at a specific wall-clock time (HH:mm, local).</summary>
        public bool   AutoClockOutAtTime { get; set; } = false;
        public string AutoClockOutTime   { get; set; } = "23:00";

        // Navigation
        public Organization? Organization { get; set; }
    }
}
