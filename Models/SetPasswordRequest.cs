namespace APM.StaffZen.API.Models
{
    public class SetPasswordRequest
    {
        public string Email    { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
