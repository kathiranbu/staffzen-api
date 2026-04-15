using APM.StaffZen.API.Models;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace APM.StaffZen.API.Services
{
    /// <summary>
    /// Sends transactional email via Brevo's HTTP API (v3/smtp/email).
    /// Render blocks outbound SMTP ports (25, 465, 587), so we use HTTPS on 443 instead.
    ///
    /// Required Render env vars (same names as before, re-mapped):
    ///   EmailSettings__SenderEmail  – your verified Brevo sender address
    ///   EmailSettings__SenderName   – display name  (e.g. "ApmStaffZen")
    ///   EmailSettings__Password     – your Brevo API key  (xkeysib-...)
    ///
    /// EmailSettings__SmtpServer, Port, and Login are no longer used but can stay
    /// in appsettings.json so the model binding doesn't break.
    /// </summary>
    public class EmailService
    {
        private const string BrevoApiUrl = "https://api.brevo.com/v3/smtp/email";

        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public EmailService(
            IOptions<EmailSettings> settings,
            ILogger<EmailService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _settings           = settings.Value;
            _logger             = logger;
            _httpClientFactory  = httpClientFactory;
        }

        // ------------------------------------------------------------------ //
        //  Core send helper                                                   //
        // ------------------------------------------------------------------ //

        private async Task<bool> SendViaBrevoAsync(string toEmail, string toName, string subject, string htmlContent)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SenderEmail) || string.IsNullOrEmpty(_settings.Password))
                {
                    _logger.LogError("Email settings are not properly configured (SenderEmail or Password/ApiKey missing)");
                    return false;
                }

                var payload = new
                {
                    sender      = new { name = _settings.SenderName, email = _settings.SenderEmail },
                    to          = new[] { new { email = toEmail, name = toName } },
                    subject,
                    htmlContent
                };

                var json = JsonSerializer.Serialize(payload);
                using var client  = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, BrevoApiUrl);
                request.Headers.Add("api-key", _settings.Password);   // Password field holds the Brevo API key
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request);
                var body     = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Email sent successfully to {Email} via Brevo HTTP API", toEmail);
                    return true;
                }

                _logger.LogError("Brevo API returned {Status} for {Email}. Body: {Body}",
                    (int)response.StatusCode, toEmail, body);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending email to {Email}", toEmail);
                return false;
            }
        }

        // ------------------------------------------------------------------ //
        //  Public methods (identical signatures to the original)              //
        // ------------------------------------------------------------------ //

        public async Task<bool> SendInviteEmail(string toEmail, string fullName, string inviteLink, string? organizationName = null)
        {
            _logger.LogInformation("Attempting to send invite email to: {Email}", toEmail);
            var orgDisplay = !string.IsNullOrWhiteSpace(organizationName) ? organizationName : "StaffZen";

            var html = $@"
                <html>
                <body style='font-family: Arial, sans-serif; background: #f5f6f8; margin: 0; padding: 20px;'>
                    <div style='max-width: 600px; margin: 0 auto; background: #fff; border-radius: 12px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,.08);'>
                        <div style='background: #ff7a00; padding: 28px 32px;'>
                            <h2 style='margin: 0; color: #fff; font-size: 22px;'>You're invited to ApmStaffZen!</h2>
                        </div>
                        <div style='padding: 28px 32px;'>
                            <p style='margin: 0 0 12px; color: #374151; font-size: 15px;'>Hi <strong>{fullName}</strong>,</p>
                            <p style='margin: 0 0 20px; color: #6b7280; font-size: 14px;'>
                                <strong>{orgDisplay}</strong> has invited you to join their team on ApmStaffZen.
                                Please complete your profile to get started.
                            </p>
                            <p style='margin: 30px 0; text-align: center;'>
                                <a href='{inviteLink}'
                                   style='background: #ff7a00; color: white; padding: 14px 36px;
                                          text-decoration: none; border-radius: 8px; display: inline-block; font-size: 15px; font-weight: bold;'>
                                    Complete Your Profile
                                </a>
                            </p>
                            <p style='color: #6b7280; font-size: 13px;'>
                                This link will expire in <strong>3 days</strong>. If you didn't expect this invitation, you can safely ignore this email.
                            </p>
                            <p style='color: #6b7280; font-size: 13px;'>
                                Or copy and paste this link in your browser:<br/>
                                <span style='color: #3b82f6;'>{inviteLink}</span>
                            </p>
                        </div>
                        <div style='background: #f9fafb; padding: 16px 32px; border-top: 1px solid #e5e7eb;'>
                            <p style='margin: 0; color: #9ca3af; font-size: 12px;'>This is an automated notification from ApmStaffZen on behalf of {orgDisplay}.</p>
                        </div>
                    </div>
                </body>
                </html>";

            return await SendViaBrevoAsync(toEmail, fullName, $"You're invited to join {orgDisplay} on ApmStaffZen", html);
        }

        public async Task<bool> SendPasswordResetEmail(string toEmail, string fullName, string resetLink)
        {
            _logger.LogInformation("Sending password reset email to: {Email}", toEmail);

            var html = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #ff7a00;'>Reset Your Password</h2>
                        <p>Hi {fullName},</p>
                        <p>We received a request to reset your StaffZen password. Click the button below to create a new password. This link expires in <strong>1 hour</strong>.</p>
                        <p style='margin: 30px 0;'>
                            <a href='{resetLink}'
                               style='background: #ff7a00; color: white; padding: 12px 30px;
                                      text-decoration: none; border-radius: 8px; display: inline-block;'>
                                Reset Password
                            </a>
                        </p>
                        <p style='color: #6b7280; font-size: 14px;'>
                            If you didn't request a password reset, you can safely ignore this email.
                        </p>
                        <p style='color: #6b7280; font-size: 14px;'>
                            Or copy and paste this link:<br/>
                            <span style='color: #3b82f6;'>{resetLink}</span>
                        </p>
                    </div>
                </body>
                </html>";

            return await SendViaBrevoAsync(toEmail, fullName, "Reset Your StaffZen Password", html);
        }

        public async Task<bool> SendClockInEmail(string toEmail, string fullName, DateTime clockInTime)
        {
            var timeStr = clockInTime.ToString("hh:mm tt");
            var dateStr = clockInTime.ToString("dddd, MMMM d, yyyy");

            var html = $@"
                <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                    <div style='background:#ff6b35;padding:28px 32px;'>
                        <h2 style='margin:0;color:#fff;font-size:20px;'>&#9200; You just clocked in!</h2>
                    </div>
                    <div style='padding:28px 32px;'>
                        <p style='margin:0 0 12px;color:#374151;font-size:15px;'>Hi <strong>{fullName}</strong>,</p>
                        <p style='margin:0 0 20px;color:#6b7280;font-size:14px;'>Your attendance has been recorded.</p>
                        <div style='background:#f9fafb;border-radius:8px;padding:16px 20px;margin-bottom:24px;border-left:4px solid #ff6b35;'>
                            <div style='color:#374151;font-size:14px;margin-bottom:6px;'><strong>Date:</strong> {dateStr}</div>
                            <div style='color:#374151;font-size:14px;'><strong>Clock In:</strong> {timeStr}</div>
                        </div>
                        <p style='margin:0;color:#9ca3af;font-size:12px;'>This is an automated notification from APM-StaffZen.</p>
                    </div>
                </div>
                </body></html>";

            return await SendViaBrevoAsync(toEmail, fullName, $"Clocked In - {timeStr}", html);
        }

        public async Task<bool> SendClockOutEmail(string toEmail, string fullName, DateTime clockInTime, DateTime clockOutTime, string workedHours)
        {
            var inStr   = clockInTime.ToString("hh:mm tt");
            var outStr  = clockOutTime.ToString("hh:mm tt");
            var dateStr = clockOutTime.ToString("dddd, MMMM d, yyyy");

            var html = $@"
                <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                    <div style='background:#22c55e;padding:28px 32px;'>
                        <h2 style='margin:0;color:#fff;font-size:20px;'>You just clocked out!</h2>
                    </div>
                    <div style='padding:28px 32px;'>
                        <p style='margin:0 0 12px;color:#374151;font-size:15px;'>Hi <strong>{fullName}</strong>,</p>
                        <p style='margin:0 0 20px;color:#6b7280;font-size:14px;'>Great work today! Here is your session summary.</p>
                        <div style='background:#f9fafb;border-radius:8px;padding:16px 20px;margin-bottom:24px;border-left:4px solid #22c55e;'>
                            <div style='color:#374151;font-size:14px;margin-bottom:6px;'><strong>Date:</strong> {dateStr}</div>
                            <div style='color:#374151;font-size:14px;margin-bottom:6px;'><strong>Clock In:</strong>  {inStr}</div>
                            <div style='color:#374151;font-size:14px;margin-bottom:6px;'><strong>Clock Out:</strong> {outStr}</div>
                            <div style='color:#374151;font-size:14px;'><strong>Total:</strong> {workedHours}</div>
                        </div>
                        <p style='margin:0;color:#9ca3af;font-size:12px;'>This is an automated notification from APM-StaffZen.</p>
                    </div>
                </div>
                </body></html>";

            return await SendViaBrevoAsync(toEmail, fullName, $"Clocked Out - {outStr} ({workedHours})", html);
        }

        public async Task<bool> SendClockOutReminderEmail(string toEmail, string fullName, DateTime shiftEnd)
        {
            var endStr  = shiftEnd.ToString("hh:mm tt");
            var dateStr = shiftEnd.ToString("dddd, MMMM d, yyyy");

            var html = $@"
                <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                    <div style='background:#f59e0b;padding:28px 32px;'>
                        <h2 style='margin:0;color:#fff;font-size:20px;'>&#9200; Clock-Out Reminder</h2>
                    </div>
                    <div style='padding:28px 32px;'>
                        <p style='margin:0 0 12px;color:#374151;font-size:15px;'>Hi <strong>{fullName}</strong>,</p>
                        <p style='margin:0 0 20px;color:#6b7280;font-size:14px;'>Your shift is ending soon. Please remember to clock out.</p>
                        <div style='background:#fef3c7;border-radius:8px;padding:16px 20px;margin-bottom:24px;border-left:4px solid #f59e0b;'>
                            <div style='color:#374151;font-size:14px;margin-bottom:6px;'><strong>Date:</strong> {dateStr}</div>
                            <div style='color:#374151;font-size:14px;'><strong>Shift ends:</strong> {endStr}</div>
                        </div>
                        <p style='margin:0;color:#9ca3af;font-size:12px;'>This is an automated notification from APM-StaffZen.</p>
                    </div>
                </div>
                </body></html>";

            return await SendViaBrevoAsync(toEmail, fullName, $"Clock-Out Reminder - Shift ends at {endStr}", html);
        }

        public async Task<bool> SendForgotClockOutEmail(string toEmail, string fullName, DateTime shiftEnd)
        {
            var endStr  = shiftEnd.ToString("hh:mm tt");
            var dateStr = shiftEnd.ToString("dddd, MMMM d, yyyy");

            var html = $@"
                <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                    <div style='background:#ef4444;padding:28px 32px;'>
                        <h2 style='margin:0;color:#fff;font-size:20px;'>&#9888; Missed Clock-Out</h2>
                    </div>
                    <div style='padding:28px 32px;'>
                        <p style='margin:0 0 12px;color:#374151;font-size:15px;'>Hi <strong>{fullName}</strong>,</p>
                        <p style='margin:0 0 20px;color:#6b7280;font-size:14px;'>
                            Your shift ended at <strong>{endStr}</strong> but we did not detect a clock-out.
                            Your attendance has been marked as <strong>Unmarked</strong>.
                        </p>
                        <div style='background:#fee2e2;border-radius:8px;padding:16px 20px;margin-bottom:24px;border-left:4px solid #ef4444;'>
                            <div style='color:#374151;font-size:14px;margin-bottom:6px;'><strong>Date:</strong> {dateStr}</div>
                            <div style='color:#374151;font-size:14px;'><strong>Shift ended:</strong> {endStr}</div>
                        </div>
                        <p style='color:#6b7280;font-size:14px;'>
                            Please submit a <strong>Correction Request</strong> from your attendance dashboard to fix this.
                        </p>
                        <p style='margin:0;color:#9ca3af;font-size:12px;'>This is an automated notification from APM-StaffZen.</p>
                    </div>
                </div>
                </body></html>";

            return await SendViaBrevoAsync(toEmail, fullName, $"You Forgot to Clock Out - {dateStr}", html);
        }

        public async Task<bool> SendGenericAsync(string toEmail, string fullName, string subject, string htmlBody)
        {
            var html = $@"
                <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                    <div style='background:#ff7a00;padding:24px 32px;'>
                        <h2 style='margin:0;color:#fff;font-size:18px;'>APM-StaffZen Notification</h2>
                    </div>
                    <div style='padding:24px 32px;'>
                        <p style='margin:0 0 12px;color:#374151;font-size:15px;'>Hi <strong>{fullName}</strong>,</p>
                        <div style='color:#374151;font-size:14px;line-height:1.6;'>{htmlBody}</div>
                    </div>
                    <div style='background:#f9fafb;padding:12px 32px;border-top:1px solid #e5e7eb;'>
                        <p style='margin:0;color:#9ca3af;font-size:12px;'>This is an automated notification from APM-StaffZen.</p>
                    </div>
                </div>
                </body></html>";

            return await SendViaBrevoAsync(toEmail, fullName, subject, html);
        }
    }
}
