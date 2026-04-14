namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Join table: which employees belong to which organizations, and what role they hold there.
    /// An employee can belong to many organizations with different roles in each.
    /// </summary>
    public class OrganizationMember
    {
        public int Id { get; set; }

        public int OrganizationId { get; set; }
        public Organization Organization { get; set; } = null!;

        public int EmployeeId { get; set; }
        public Employee Employee { get; set; } = null!;

        /// <summary>Role within THIS organization: "Admin", "Manager", "Team Lead", "HR", "Employee", "User"</summary>
        public string OrgRole { get; set; } = "Employee";

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
    }
}
