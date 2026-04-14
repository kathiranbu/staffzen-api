using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/employees/{employeeId}/notification-settings")]
    public class NotificationSettingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NotificationSettingsController> _logger;

        public NotificationSettingsController(ApplicationDbContext context, ILogger<NotificationSettingsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get(int employeeId)
        {
            try
            {
                var s = await _context.EmployeeNotificationSettings
                    .FirstOrDefaultAsync(x => x.EmployeeId == employeeId)
                    ?? new EmployeeNotificationSettings { EmployeeId = employeeId };
                return Ok(ToDto(s));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetNotificationSettings failed for employee {Id}", employeeId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut]
        public async Task<IActionResult> Save(int employeeId, [FromBody] NotificationSettingsDto dto)
        {
            try
            {
                var s = await _context.EmployeeNotificationSettings
                    .FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
                if (s == null)
                {
                    s = new EmployeeNotificationSettings { EmployeeId = employeeId };
                    _context.EmployeeNotificationSettings.Add(s);
                }

                // Channels
                s.ReportsChannelEmail       = dto.ReportsChannelEmail;
                s.ReportsChannelWhatsApp    = dto.ReportsChannelWhatsApp;
                s.ReportsChannelSms         = dto.ReportsChannelSms;
                s.RemindersChannelEmail     = dto.RemindersChannelEmail;
                s.RemindersChannelWhatsApp  = dto.RemindersChannelWhatsApp;
                s.RemindersChannelSms       = dto.RemindersChannelSms;
                s.ReportsChannelPush        = dto.ReportsChannelPush;
                s.RemindersChannelPush      = dto.RemindersChannelPush;

                // Reports
                s.NotifDailyAttendance = dto.NotifDailyAttendance;
                s.DailyAttendanceTime  = dto.DailyAttendanceTime ?? "9:00 am";
                s.DailyAttendanceFreq  = dto.DailyAttendanceFreq ?? "everyday";
                s.NotifWeeklyActivity  = dto.NotifWeeklyActivity;
                s.WeeklyActivityDay    = dto.WeeklyActivityDay ?? "Monday";

                // Reminders
                s.NotifClockIn    = dto.NotifClockIn;
                s.ClockInMinutes  = dto.ClockInMinutes;
                s.NotifClockOut   = dto.NotifClockOut;
                s.ClockOutMinutes = dto.ClockOutMinutes;
                s.NotifEndBreak   = dto.NotifEndBreak;
                s.EndBreakMinutes = dto.EndBreakMinutes;

                // Alerts
                s.NotifTimeClockStarts = dto.NotifTimeClockStarts;
                s.NotifTimeOffRequests = dto.NotifTimeOffRequests;

                // Subscriptions
                s.SubProductUpdates = dto.SubProductUpdates;
                s.SubPromotions     = dto.SubPromotions;
                s.SubUsageTracking  = dto.SubUsageTracking;

                await _context.SaveChangesAsync();
                return Ok(ToDto(s));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveNotificationSettings failed for employee {Id}", employeeId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private static NotificationSettingsDto ToDto(EmployeeNotificationSettings s) => new()
        {
            ReportsChannelEmail      = s.ReportsChannelEmail,
            ReportsChannelWhatsApp   = s.ReportsChannelWhatsApp,
            ReportsChannelSms        = s.ReportsChannelSms,
            RemindersChannelEmail    = s.RemindersChannelEmail,
            RemindersChannelWhatsApp = s.RemindersChannelWhatsApp,
            RemindersChannelSms      = s.RemindersChannelSms,
            ReportsChannelPush       = s.ReportsChannelPush,
            RemindersChannelPush     = s.RemindersChannelPush,
            NotifDailyAttendance     = s.NotifDailyAttendance,
            DailyAttendanceTime      = s.DailyAttendanceTime,
            DailyAttendanceFreq      = s.DailyAttendanceFreq,
            NotifWeeklyActivity      = s.NotifWeeklyActivity,
            WeeklyActivityDay        = s.WeeklyActivityDay,
            NotifClockIn             = s.NotifClockIn,
            ClockInMinutes           = s.ClockInMinutes,
            NotifClockOut            = s.NotifClockOut,
            ClockOutMinutes          = s.ClockOutMinutes,
            NotifEndBreak            = s.NotifEndBreak,
            EndBreakMinutes          = s.EndBreakMinutes,
            NotifTimeClockStarts     = s.NotifTimeClockStarts,
            NotifTimeOffRequests     = s.NotifTimeOffRequests,
            SubProductUpdates        = s.SubProductUpdates,
            SubPromotions            = s.SubPromotions,
            SubUsageTracking         = s.SubUsageTracking,
        };
    }

    public class NotificationSettingsDto
    {
        // Channels
        public bool ReportsChannelEmail      { get; set; } = true;
        public bool ReportsChannelWhatsApp   { get; set; } = false;
        public bool ReportsChannelSms        { get; set; } = false;
        public bool ReportsChannelPush       { get; set; } = false;
        public bool RemindersChannelEmail    { get; set; } = true;
        public bool RemindersChannelWhatsApp { get; set; } = false;
        public bool RemindersChannelSms      { get; set; } = false;
        public bool RemindersChannelPush     { get; set; } = false;

        // Reports
        public bool   NotifDailyAttendance { get; set; }
        public string DailyAttendanceTime  { get; set; } = "9:00 am";
        public string DailyAttendanceFreq  { get; set; } = "everyday";
        public bool   NotifWeeklyActivity  { get; set; }
        public string WeeklyActivityDay    { get; set; } = "Monday";

        // Reminders
        public bool NotifClockIn    { get; set; }
        public int  ClockInMinutes  { get; set; } = 5;
        public bool NotifClockOut   { get; set; }
        public int  ClockOutMinutes { get; set; } = 5;
        public bool NotifEndBreak   { get; set; }
        public int  EndBreakMinutes { get; set; } = 5;

        // Alerts
        public bool NotifTimeClockStarts { get; set; }
        public bool NotifTimeOffRequests { get; set; }

        // Subscriptions
        public bool SubProductUpdates { get; set; }
        public bool SubPromotions     { get; set; }
        public bool SubUsageTracking  { get; set; }
    }
}
