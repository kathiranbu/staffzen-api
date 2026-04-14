namespace APM.StaffZen.API.Dtos
{
    public class TestEmailRequest
    {
        public required string ToEmail { get; set; }
        public string? ToName { get; set; }
    }
}
