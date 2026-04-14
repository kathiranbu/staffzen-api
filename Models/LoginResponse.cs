namespace APM.StaffZen.API.Models
{
    public class LoginResponse
    {
        public int     Id              { get; set; }
        public string  FullName        { get; set; } = string.Empty;
        public string  Email           { get; set; } = string.Empty;
        public string  Role            { get; set; } = string.Empty;
        public bool    IsNewAccount    { get; set; }
        public bool    IsOnboarded     { get; set; }
        public string? OrganizationName { get; set; }

        // Active org context
        public int?    ActiveOrganizationId   { get; set; }
        public string? ActiveOrgRole          { get; set; }

        /// <summary>All orgs this user belongs to.</summary>
        public List<UserOrgMembership> Organizations { get; set; } = new();
    }

    public class UserOrgMembership
    {
        public int    OrganizationId   { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public string OrgRole          { get; set; } = string.Empty;
        public bool   IsOwner          { get; set; }
    }
}
