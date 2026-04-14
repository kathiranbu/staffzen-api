namespace APM.StaffZen.API.Dtos
{
    public class CompleteInviteDto
    {
        public string Token { get; set; } = null!;

        public string FullName { get; set; } = null!;

        public string MobileNumber { get; set; } = null!;

        public DateTime DateOfBirth { get; set; }

        public string ProfileImageBase64 { get; set; } = null!;   // ⭐ IMPORTANT
    }


}
