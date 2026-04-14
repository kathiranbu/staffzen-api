namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Defines the rules for one leave type (e.g. Annual Leave, Sick Leave, LOP).
    /// This is a configuration record only — it does NOT apply leave or deduct salary.
    /// Attendance, leave-request, and payroll modules read these rules at runtime.
    /// </summary>
    public class TimeOffPolicy
    {
        public int Id { get; set; }

        /// <summary>Display name, e.g. "Annual Leave", "Sick Leave".</summary>
        public string Name { get; set; } = "";

        /// <summary>"Paid" → counts towards payroll. "Unpaid" → not billable, salary deducted.</summary>
        public string CompensationType { get; set; } = "Paid";

        /// <summary>"Days" or "Hours". Leave requests must be in this unit.</summary>
        public string Unit { get; set; } = "Days";

        /// <summary>
        /// How leave is earned (Paid policies only):
        ///   "None"    – earned immediately, no proration.
        ///   "Annual"  – earned yearly, prorated.
        ///   "Monthly" – earned monthly over a yearly cycle.
        /// </summary>
        public string AccrualType { get; set; } = "None";

        /// <summary>Total days/hours an employee is entitled to per year.</summary>
        public double AnnualEntitlement { get; set; } = 0;

        /// <summary>If true, public holidays within leave period are NOT counted as leave days.</summary>
        public bool ExcludePublicHolidays { get; set; } = false;

        /// <summary>If true, rest/non-working days within leave period are NOT counted as leave days.</summary>
        public bool ExcludeNonWorkingDays { get; set; } = false;

        /// <summary>Allow unused balance to carry forward to next cycle.</summary>
        public bool AllowCarryForward { get; set; } = false;

        /// <summary>Max days/hours that can carry forward. Null = no cap.</summary>
        public double? CarryForwardLimit { get; set; }

        /// <summary>Months after new cycle starts before carried balance expires. Null = never.</summary>
        public int? CarryForwardExpiryMonths { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Assigns a policy to specific employees and/or groups.
    /// A policy with NO assignments applies to ALL employees automatically.
    /// </summary>
    public class TimeOffPolicyAssignment
    {
        public int Id { get; set; }
        public int PolicyId { get; set; }

        /// <summary>Null if this is a group-level assignment.</summary>
        public int? EmployeeId { get; set; }

        /// <summary>Null if this is an employee-level assignment.</summary>
        public int? GroupId { get; set; }
    }
}
