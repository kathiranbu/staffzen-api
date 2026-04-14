namespace APM.StaffZen.API.Models
{
    public class TimeOffRequest
    {
        public int      Id          { get; set; }
        public int      EmployeeId  { get; set; }
        public int      PolicyId    { get; set; }
        public DateTime StartDate   { get; set; }
        public DateTime? EndDate    { get; set; }
        public bool     IsHalfDay   { get; set; } = false;
        public string?  HalfDayPart { get; set; }
        public string?  Reason      { get; set; }

        /// <summary>Pending | Approved | Rejected</summary>
        public string   Status      { get; set; } = "Pending";

        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;

        // Navigation
        public Employee?      Employee { get; set; }
        public TimeOffPolicy? Policy   { get; set; }
    }
}
