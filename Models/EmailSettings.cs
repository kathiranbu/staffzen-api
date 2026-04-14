namespace APM.StaffZen.API.Models
{
    public class EmailSettings
    {
        public required string SmtpServer { get; set; }
        public int Port { get; set; }
        public required string SenderName { get; set; }
        public required string SenderEmail { get; set; }
        public required string Password { get; set; }
        /// <summary>
        /// SMTP login username. For Brevo this differs from SenderEmail (e.g. a29466001@smtp-brevo.com).
        /// If not set, falls back to SenderEmail.
        /// </summary>
        public string? Login { get; set; }
    }
}
