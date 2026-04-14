namespace APM.StaffZen.API.Dtos
{
    public class SetPasswordDto
    {
        public required string Token { get; set; }
        public required string Password { get; set; }
    }

}
