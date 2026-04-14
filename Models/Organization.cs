namespace APM.StaffZen.API.Models
{
    public class Organization
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Country { get; set; }

        public string? PhoneNumber { get; set; }

        public string? CountryCode { get; set; }

        public string? Industry { get; set; }

        public string? OrganizationSize { get; set; }

        public string? OwnerRole { get; set; }

        /// <summary>Comma-separated list of selected devices e.g. "MobileApps,WebBrowser"</summary>
        public string? SelectedDevices { get; set; }

        /// <summary>The employee who created / owns this organization.</summary>
        public int EmployeeId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation — one org has many employees
        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }
}
