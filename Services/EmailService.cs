using APM.StaffZen.API.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace APM.StaffZen.API.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<bool> SendInviteEmail(string toEmail, string fullName, string inviteLink, string? organizationName = null)
        {
            try
            {
                _logger.LogInformation("Attempting to send email to: {Email}", toEmail);

                // Validate email settings
                if (string.IsNullOrEmpty(_settings.SenderEmail) ||
                    string.IsNullOrEmpty(_settings.Password) ||
                    string.IsNullOrEmpty(_settings.SmtpServer))
                {
                    _logger.LogError("Email settings are not properly configured");
                    return false;
                }

                var orgDisplay = !string.IsNullOrWhiteSpace(organizationName) ? organizationName : "StaffZen";

                var mail = new MailMessage
                {
                    From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject = $"You're invited to join {orgDisplay} on ApmStaffZen",
                    Body = $@"
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
                        </html>",
                    IsBodyHtml = true
                };

                mail.To.Add(toEmail);

                var smtpUsername = !string.IsNullOrEmpty(_settings.Login)
                    ? _settings.Login : _settings.SenderEmail;

                // Brevo port 587 uses STARTTLS — EnableSsl=false lets .NET negotiate STARTTLS correctly.
                // Use EnableSsl=true only for port 465 (implicit SSL).
                var useSsl = _settings.Port == 465;

                using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials = new NetworkCredential(smtpUsername, _settings.Password),
                    EnableSsl = useSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                await client.SendMailAsync(mail);

                _logger.LogInformation("Email sent successfully to {Email}", toEmail);
                return true;
            }
            catch (SmtpFailedRecipientsException ex)
            {
                _logger.LogError(ex, "Failed to send email to recipient {Email}. Invalid recipient.", toEmail);
                return false;
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP error while sending email to {Email}. StatusCode={StatusCode} Message={Message}", toEmail, ex.StatusCode, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending email to {Email}. Error: {Message}", toEmail, ex.Message);
                return false;
            }
        }


        public async Task<bool> SendPasswordResetEmail(string toEmail, string fullName, string resetLink)
        {
            try
            {
                _logger.LogInformation("Sending password reset email to: {Email}", toEmail);

                if (string.IsNullOrEmpty(_settings.SenderEmail) ||
                    string.IsNullOrEmpty(_settings.Password) ||
                    string.IsNullOrEmpty(_settings.SmtpServer))
                {
                    _logger.LogError("Email settings are not properly configured");
                    return false;
                }

                var mail = new MailMessage
                {
                    From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject = "Reset Your StaffZen Password",
                    Body = $@"
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
                        </html>",
                    IsBodyHtml = true
                };

                mail.To.Add(toEmail);

                var smtpUsername = !string.IsNullOrEmpty(_settings.Login)
                    ? _settings.Login : _settings.SenderEmail;
                var useSsl = _settings.Port == 465;
                using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials = new NetworkCredential(smtpUsername, _settings.Password),
                    EnableSsl = useSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                await client.SendMailAsync(mail);
                _logger.LogInformation("Password reset email sent to {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
                return false;
            }
        }

        public async Task<bool> SendClockInEmail(string toEmail, string fullName, DateTime clockInTime)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SenderEmail) || string.IsNullOrEmpty(_settings.Password) || string.IsNullOrEmpty(_settings.SmtpServer))
                    return false;

                var timeStr = clockInTime.ToString("hh:mm tt");
                var dateStr = clockInTime.ToString("dddd, MMMM d, yyyy");

                var mail = new MailMessage
                {
                    From    = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject = $"\u2705 Clocked In - {timeStr}",
                    IsBodyHtml = true,
                    Body = $@"
                        <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                        <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                            <div style='background:#ff6b35;padding:28px 32px;'>
                                <h2 style='margin:0;color:#fff;font-size:20px;'>\u23F0 You just clocked in!</h2>
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
                        </body></html>"
                };
                mail.To.Add(toEmail);

                var smtpUser = !string.IsNullOrEmpty(_settings.Login) ? _settings.Login : _settings.SenderEmail;
                using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials = new NetworkCredential(smtpUser, _settings.Password),
                    EnableSsl   = _settings.Port == 465,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout     = 30000
                };
                await client.SendMailAsync(mail);
                _logger.LogInformation("Clock-in email sent to {Email}", toEmail);
                return true;
            }
            catch (Exception ex) { _logger.LogError(ex, "SendClockInEmail failed for {Email}", toEmail); return false; }
        }

        /// <summary>
        /// Generic HTML email used for admin/employee notifications (correction requests, approvals, etc.).
        /// </summary>
        public async Task<bool> SendGenericAsync(string toEmail, string fullName, string subject, string htmlBody)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SenderEmail) || string.IsNullOrEmpty(_settings.Password) || string.IsNullOrEmpty(_settings.SmtpServer))
                    return false;

                var mail = new MailMessage
                {
                    From       = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject    = subject,
                    IsBodyHtml = true,
                    Body       = $@"
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
                        </body></html>"
                };
                mail.To.Add(toEmail);

                var smtpUser = !string.IsNullOrEmpty(_settings.Login) ? _settings.Login : _settings.SenderEmail;
                using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials    = new NetworkCredential(smtpUser, _settings.Password),
                    EnableSsl      = _settings.Port == 465,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout        = 30000
                };
                await client.SendMailAsync(mail);
                _logger.LogInformation("Generic email sent to {Email}", toEmail);
                return true;
            }
            catch (Exception ex) { _logger.LogError(ex, "SendGenericAsync failed for {Email}", toEmail); return false; }
        }

        /// <summary>
        /// Reminder email sent to employees X minutes BEFORE their shift ends (ReminderBased policy).
        /// </summary>
        public async Task<bool> SendClockOutReminderEmail(string toEmail, string fullName, DateTime shiftEnd)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SenderEmail) || string.IsNullOrEmpty(_settings.Password) || string.IsNullOrEmpty(_settings.SmtpServer))
                    return false;

                var endStr  = shiftEnd.ToString("hh:mm tt");
                var dateStr = shiftEnd.ToString("dddd, MMMM d, yyyy");

                var mail = new MailMessage
                {
                    From       = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject    = $"⏰ Clock-Out Reminder — Shift ends at {endStr}",
                    IsBodyHtml = true,
                    Body       = $@"
                        <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                        <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                            <div style='background:#f59e0b;padding:28px 32px;'>
                                <h2 style='margin:0;color:#fff;font-size:20px;'>⏰ Clock-Out Reminder</h2>
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
                        </body></html>"
                };
                mail.To.Add(toEmail);

                var smtpUser = !string.IsNullOrEmpty(_settings.Login) ? _settings.Login : _settings.SenderEmail;
                using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials    = new NetworkCredential(smtpUser, _settings.Password),
                    EnableSsl      = _settings.Port == 465,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout        = 30000
                };
                await client.SendMailAsync(mail);
                _logger.LogInformation("Clock-out reminder email sent to {Email}", toEmail);
                return true;
            }
            catch (Exception ex) { _logger.LogError(ex, "SendClockOutReminderEmail failed for {Email}", toEmail); return false; }
        }

        /// <summary>
        /// Warning email sent to employees who forgot to clock out (sent after WarningAfterEndMins).
        /// </summary>
        public async Task<bool> SendForgotClockOutEmail(string toEmail, string fullName, DateTime shiftEnd)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SenderEmail) || string.IsNullOrEmpty(_settings.Password) || string.IsNullOrEmpty(_settings.SmtpServer))
                    return false;

                var endStr  = shiftEnd.ToString("hh:mm tt");
                var dateStr = shiftEnd.ToString("dddd, MMMM d, yyyy");

                var mail = new MailMessage
                {
                    From       = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject    = $"⚠️ You Forgot to Clock Out — {dateStr}",
                    IsBodyHtml = true,
                    Body       = $@"
                        <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                        <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                            <div style='background:#ef4444;padding:28px 32px;'>
                                <h2 style='margin:0;color:#fff;font-size:20px;'>⚠️ Missed Clock-Out</h2>
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
                        </body></html>"
                };
                mail.To.Add(toEmail);

                var smtpUser = !string.IsNullOrEmpty(_settings.Login) ? _settings.Login : _settings.SenderEmail;
                using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials    = new NetworkCredential(smtpUser, _settings.Password),
                    EnableSsl      = _settings.Port == 465,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout        = 30000
                };
                await client.SendMailAsync(mail);
                _logger.LogInformation("Forgot-clock-out email sent to {Email}", toEmail);
                return true;
            }
            catch (Exception ex) { _logger.LogError(ex, "SendForgotClockOutEmail failed for {Email}", toEmail); return false; }
        }

        public async Task<bool> SendClockOutEmail(string toEmail, string fullName, DateTime clockInTime, DateTime clockOutTime, string workedHours)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SenderEmail) || string.IsNullOrEmpty(_settings.Password) || string.IsNullOrEmpty(_settings.SmtpServer))
                    return false;

                var inStr   = clockInTime.ToString("hh:mm tt");
                var outStr  = clockOutTime.ToString("hh:mm tt");
                var dateStr = clockOutTime.ToString("dddd, MMMM d, yyyy");

                var mail = new MailMessage
                {
                    From    = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject = $"\uD83D\uDFE2 Clocked Out - {outStr} ({workedHours})",
                    IsBodyHtml = true,
                    Body = $@"
                        <html><body style='font-family:Arial,sans-serif;background:#f5f6f8;margin:0;padding:20px;'>
                        <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);'>
                            <div style='background:#22c55e;padding:28px 32px;'>
                                <h2 style='margin:0;color:#fff;font-size:20px;'>\uD83D\uDFE2 You just clocked out!</h2>
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
                        </body></html>"
                };
                mail.To.Add(toEmail);

                var smtpUser = !string.IsNullOrEmpty(_settings.Login) ? _settings.Login : _settings.SenderEmail;
                using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials = new NetworkCredential(smtpUser, _settings.Password),
                    EnableSsl   = _settings.Port == 465,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout     = 30000
                };
                await client.SendMailAsync(mail);
                _logger.LogInformation("Clock-out email sent to {Email}", toEmail);
                return true;
            }
            catch (Exception ex) { _logger.LogError(ex, "SendClockOutEmail failed for {Email}", toEmail); return false; }
        }

    }
}