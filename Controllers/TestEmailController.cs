using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/test-email")]
    public class TestEmailController : ControllerBase
    {
        private readonly EmailService _emailService;
        private readonly ILogger<TestEmailController> _logger;
        private readonly IConfiguration _configuration;

        public TestEmailController(
            EmailService emailService,
            ILogger<TestEmailController> logger,
            IConfiguration configuration)
        {
            _emailService = emailService;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost]
        public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest request)
        {
            try
            {
                _logger.LogInformation("Test email requested for: {Email}", request.ToEmail);

                // Validate email settings first
                var smtpServer = _configuration["EmailSettings:SmtpServer"];
                var senderEmail = _configuration["EmailSettings:SenderEmail"];
                var password = _configuration["EmailSettings:Password"];
                var port = _configuration["EmailSettings:Port"];

                if (string.IsNullOrEmpty(smtpServer) ||
                    string.IsNullOrEmpty(senderEmail) ||
                    string.IsNullOrEmpty(password))
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Email settings are not configured properly",
                        error = "Missing configuration",
                        details = new
                        {
                            smtpConfigured = !string.IsNullOrEmpty(smtpServer),
                            senderConfigured = !string.IsNullOrEmpty(senderEmail),
                            passwordConfigured = !string.IsNullOrEmpty(password),
                            portConfigured = !string.IsNullOrEmpty(port)
                        }
                    });
                }

                var testLink = "https://example.com/test";
                var result = await _emailService.SendInviteEmail(
                    request.ToEmail,
                    request.ToName ?? "Test User",
                    testLink);

                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = $"Test email sent successfully to {request.ToEmail}",
                        sentFrom = senderEmail,
                        smtpServer = smtpServer,
                        port = port
                    });
                }
                else
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Failed to send test email. Check API logs for details.",
                        sentFrom = senderEmail,
                        smtpServer = smtpServer,
                        port = port
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test email");
                return Ok(new
                {
                    success = false,
                    message = "An error occurred while sending test email",
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpGet("check-config")]
        public IActionResult CheckEmailConfiguration()
        {
            var smtpServer = _configuration["EmailSettings:SmtpServer"];
            var senderEmail = _configuration["EmailSettings:SenderEmail"];
            var password = _configuration["EmailSettings:Password"];
            var port = _configuration["EmailSettings:Port"];
            var senderName = _configuration["EmailSettings:SenderName"];

            return Ok(new
            {
                configured = !string.IsNullOrEmpty(smtpServer) &&
                            !string.IsNullOrEmpty(senderEmail) &&
                            !string.IsNullOrEmpty(password),
                settings = new
                {
                    smtpServer = smtpServer ?? "NOT SET",
                    port = port ?? "NOT SET",
                    senderName = senderName ?? "NOT SET",
                    senderEmail = senderEmail ?? "NOT SET",
                    passwordConfigured = !string.IsNullOrEmpty(password),
                    passwordLength = password?.Length ?? 0
                }
            });
        }
    }
}
