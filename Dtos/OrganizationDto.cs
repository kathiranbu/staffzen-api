namespace APM.StaffZen.API.Dtos
{
    // ── Request sent from the Getting-Started form ─────────────────────────
    public class CreateOrganizationDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CountryCode { get; set; }
        public string? Industry { get; set; }
        public string? OrganizationSize { get; set; }
        public string? OwnerRole { get; set; }

        /// <summary>Employee who is creating the organization (from session).</summary>
        public int EmployeeId { get; set; }
    }

    // ── Response returned after creation ──────────────────────────────────
    public class OrganizationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CountryCode { get; set; }
        public string? Industry { get; set; }
        public string? OrganizationSize { get; set; }
        public string? OwnerRole { get; set; }
        public int EmployeeId { get; set; }
        public DateTime CreatedAt { get; set; }

        // Summary of employees in this org
        public List<OrgEmployeeSummaryDto> Employees { get; set; } = new();
    }

    // ── Lightweight employee summary used inside OrganizationDto ──────────
    public class OrgEmployeeSummaryDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? ProfileImageUrl { get; set; }
    }

    // ── Save devices request ───────────────────────────────────────────────
    public class SaveDevicesDto
    {
        /// <summary>Comma-separated device keys e.g. "MobileApps,WebBrowser"</summary>
        public string? SelectedDevices { get; set; }
    }
}
