namespace APM.StaffZen.API.Models
{
    public class EmployeeInvite
    {
        public int Id { get; set; }
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public string Token { get; set; } = "";
        public DateTime ExpiryDate { get; set; }
        public bool IsUsed { get; set; }

        /// <summary>The org this invite is for. 0 = no specific org (legacy).</summary>
        public int OrganizationId { get; set; }

        /// <summary>Role within that org.</summary>
        public string OrgRole { get; set; } = "Employee";
    }
}
