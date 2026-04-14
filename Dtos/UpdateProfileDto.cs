namespace APM.StaffZen.API.Dtos
{
    public class UpdateProfileDto
    {
        public string FullName { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
        public string? CountryCode { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? ProfileImageUrl { get; set; }
    }
}
