namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Persistent in-app notification stored per recipient.
    ///
    /// Leave flow:
    ///   User applies      → notify ALL Admin members of the org  (Type = "LeaveApplied")
    ///   Admin approves    → notify the requesting employee        (Type = "LeaveApproved")
    ///   Admin rejects     → notify the requesting employee        (Type = "LeaveRejected")
    ///
    /// ReferenceId always holds the LeaveRequest.Id so the UI can fetch that
    /// single record when the notification is clicked.
    /// </summary>
    public class Notification
    {
        public int    Id          { get; set; }

        /// <summary>Employee who should receive this notification.</summary>
        public int    RecipientId { get; set; }

        /// <summary>LeaveApplied | LeaveApproved | LeaveRejected</summary>
        public string Type        { get; set; } = "";

        /// <summary>Short heading shown in bold inside the panel.</summary>
        public string Title       { get; set; } = "";

        /// <summary>One-line detail message.</summary>
        public string Message     { get; set; } = "";

        /// <summary>LeaveRequest.Id that triggered this notification.</summary>
        public int    ReferenceId { get; set; }

        public bool   IsRead      { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Employee? Recipient { get; set; }
    }
}
