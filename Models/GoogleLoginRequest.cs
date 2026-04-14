namespace APM.StaffZen.API.Models
{
    public class GoogleLoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Password { get; set; }
    }
}
