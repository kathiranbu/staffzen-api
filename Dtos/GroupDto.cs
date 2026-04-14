namespace APM.StaffZen.API.Dtos
{
    public class GroupDto
    {
        public int     Id                   { get; set; }
        public string  Name                 { get; set; } = "";
        public string? Description          { get; set; }
        public int     MemberCount          { get; set; }
        public string? AttendancePolicyType { get; set; }
        public int     ReminderBeforeEndMins { get; set; } = 15;
        public int     WarningAfterEndMins   { get; set; } = 30;
    }

    public class CreateGroupDto
    {
        public string  Name                  { get; set; } = "";
        public string? Description           { get; set; }
        public string? AttendancePolicyType  { get; set; }
        public int     ReminderBeforeEndMins { get; set; } = 15;
        public int     WarningAfterEndMins   { get; set; } = 30;
    }

    public class UpdateGroupDto
    {
        public string  Name                  { get; set; } = "";
        public string? Description           { get; set; }
        public string? AttendancePolicyType  { get; set; }
        public int     ReminderBeforeEndMins { get; set; } = 15;
        public int     WarningAfterEndMins   { get; set; } = 30;
    }

    public class UpdateGroupAttendancePolicyDto
    {
        /// <summary>"AutoClockOut" | "ReminderBased" | null (inherit org default)</summary>
        public string? AttendancePolicyType  { get; set; }
        public int     ReminderBeforeEndMins { get; set; } = 15;
        public int     WarningAfterEndMins   { get; set; } = 30;
    }
}
