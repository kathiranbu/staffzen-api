namespace APM.StaffZen.API.Dtos
{
    /// <summary>Returned in list and detail views. Includes a computed assignee summary.</summary>
    public class TimeOffPolicyDto
    {
        public int    Id                       { get; set; }
        public string Name                     { get; set; } = "";
        public string CompensationType         { get; set; } = "Paid";
        public string Unit                     { get; set; } = "Days";
        public string AccrualType              { get; set; } = "None";
        public double AnnualEntitlement        { get; set; }
        public bool   ExcludePublicHolidays    { get; set; }
        public bool   ExcludeNonWorkingDays    { get; set; }
        public bool   AllowCarryForward        { get; set; }
        public double? CarryForwardLimit       { get; set; }
        public int?   CarryForwardExpiryMonths { get; set; }
        public bool   IsActive                 { get; set; }
        public DateTime CreatedAt              { get; set; }

        /// <summary>Human-readable summary: "All Employees", "2 Groups", "Dev Team, 3 Members"…</summary>
        public string AssigneeSummary          { get; set; } = "All Employees";

        /// <summary>Full assignment lists for editing.</summary>
        public List<int> AssignedEmployeeIds   { get; set; } = new();
        public List<int> AssignedGroupIds      { get; set; } = new();
    }

    /// <summary>Used for both Create and Update.</summary>
    public class SaveTimeOffPolicyDto
    {
        public string Name                     { get; set; } = "";
        public string CompensationType         { get; set; } = "Paid";
        public string Unit                     { get; set; } = "Days";
        public string AccrualType              { get; set; } = "None";
        public double AnnualEntitlement        { get; set; } = 0;
        public bool   ExcludePublicHolidays    { get; set; } = false;
        public bool   ExcludeNonWorkingDays    { get; set; } = false;
        public bool   AllowCarryForward        { get; set; } = false;
        public double? CarryForwardLimit       { get; set; }
        public int?   CarryForwardExpiryMonths { get; set; }

        /// <summary>Employee IDs to assign. Empty list = apply to all employees.</summary>
        public List<int> AssignedEmployeeIds   { get; set; } = new();

        /// <summary>Group IDs to assign. Empty list = apply to all groups.</summary>
        public List<int> AssignedGroupIds      { get; set; } = new();
    }
}
