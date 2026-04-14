namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Two-stage leave approval request.
    ///
    /// Status flow:
    ///   User submits          →  Pending_TeamLead
    ///   Team Lead rejects     →  Rejected_TeamLead   (RejectReason set)
    ///   Team Lead direct-appr →  Approved
    ///   Team Lead forwards    →  Pending_HR
    ///   HR approves           →  Approved
    ///   HR rejects            →  Rejected_HR          (RejectReason set)
    /// </summary>
    public class LeaveRequest
    {
        public int      Id           { get; set; }
        public int      EmployeeId   { get; set; }
        public int?     LeaveTypeId  { get; set; }    // FK → TimeOffPolicy.Id (optional for legacy rows)
        public string?  LeaveTypeName { get; set; }   // snapshot of policy name at submission time
        public DateTime StartDate    { get; set; }
        public DateTime EndDate      { get; set; }
        public string?  Reason       { get; set; }

        /// <summary>Pending_TeamLead | Pending_HR | Approved | Rejected_TeamLead | Rejected_HR</summary>
        public string   Status       { get; set; } = "Pending_TeamLead";

        /// <summary>Who last reviewed: TeamLead | HR</summary>
        public string?  ReviewedBy   { get; set; }

        /// <summary>Rejection note added by Team Lead or HR.</summary>
        public string?  RejectReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Employee? Employee { get; set; }
    }
}
