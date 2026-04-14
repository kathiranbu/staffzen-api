namespace APM.StaffZen.API.Dtos
{
    public class InviteRequestDto
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string CountryCode { get; set; } = "+91";
        public string PhoneNumber { get; set; } = "";
        public string Role { get; set; } = "Employee";
        public int? GroupId { get; set; }
        public string? OrganizationName { get; set; }

        /// <summary>The organization this person is being invited to join.</summary>
        public int? OrganizationId { get; set; }

        /// <summary>Their role within that organization.</summary>
        public string OrgRole { get; set; } = "Employee";
    }
}
