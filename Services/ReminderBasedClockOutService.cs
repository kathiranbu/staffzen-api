using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Services
{
    /// <summary>
    /// Background service for the ReminderBased attendance method.
    /// Runs every minute and for each employee in a ReminderBased department:
    ///   1. ReminderBeforeEndMins BEFORE shift end → send reminder notification
    ///   2. WarningAfterEndMins   AFTER shift end  → send warning notification
    ///   3. After warning window passes and still no clock-out → AttendanceStatus = "Unmarked"
    /// </summary>
    public class ReminderBasedClockOutService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ReminderBasedClockOutService> _logger;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

        public ReminderBasedClockOutService(IServiceProvider services, ILogger<ReminderBasedClockOutService> logger)
        {
            _services = services;
            _logger   = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReminderBasedClockOutService started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try   { await RunCheckAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogError(ex, "ReminderBasedClockOutService error."); }
                await Task.Delay(CheckInterval, stoppingToken);
            }
            _logger.LogInformation("ReminderBasedClockOutService stopped.");
        }

        private async Task RunCheckAsync(CancellationToken ct)
        {
            using var scope  = _services.CreateScope();
            var db           = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
            var firebase     = scope.ServiceProvider.GetRequiredService<FirebaseService>();

            var nowIst = DateTime.Now; // server runs IST

            // ── 1. Load ReminderBased departments ──────────────────────────
            List<Group> reminderGroups;
            try
            {
                reminderGroups = await db.Groups
                    .Where(g => g.AttendancePolicyType == "ReminderBased")
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReminderBasedClockOutService: could not read Groups.");
                return;
            }
            if (!reminderGroups.Any()) return;

            var groupIds = reminderGroups.Select(g => g.Id).ToHashSet();

            // ── 2. Find employees in those departments that are clocked in ─
            var clockedInEntries = await db.TimeEntries
                .Include(t => t.Employee)
                .Where(t => t.ClockOut     == null
                         && !t.IsManual
                         && !t.IsHourEntry
                         && !t.IsBreakEntry
                         && t.Employee     != null
                         && t.Employee.GroupId != null
                         && groupIds.Contains(t.Employee.GroupId.Value))
                .ToListAsync(ct);

            if (!clockedInEntries.Any()) return;

            // ── 3. Load work schedules ──────────────────────────────────────
            List<WorkSchedule> allSchedules;
            WorkSchedule?      defaultSchedule;
            try
            {
                allSchedules    = await db.WorkSchedules.ToListAsync(ct);
                defaultSchedule = allSchedules.FirstOrDefault(s => s.IsDefault) ?? allSchedules.FirstOrDefault();
            }
            catch
            {
                allSchedules    = new();
                defaultSchedule = null;
            }

            // ── 4. Load notification settings ──────────────────────────────
            var empIds        = clockedInEntries.Select(t => t.EmployeeId).Distinct().ToList();
            var notifSettings = await db.EmployeeNotificationSettings
                .Where(s => empIds.Contains(s.EmployeeId))
                .ToDictionaryAsync(s => s.EmployeeId, ct);

            // ── 5. Process each open entry ──────────────────────────────────
            bool anyChanges = false;
            foreach (var entry in clockedInEntries)
            {
                var emp   = entry.Employee!;
                var group = reminderGroups.First(g => g.Id == emp.GroupId!.Value);

                var empSchedule = (!string.IsNullOrWhiteSpace(emp.WorkSchedule)
                                    ? allSchedules.FirstOrDefault(s => s.Name == emp.WorkSchedule)
                                    : null) ?? defaultSchedule;

                TimeSpan shiftEndTime = GetShiftEndTime(empSchedule, nowIst.DayOfWeek);
                if (shiftEndTime == TimeSpan.Zero) continue;

                var shiftEndDateTime = nowIst.Date + shiftEndTime;
                var reminderTime     = shiftEndDateTime - TimeSpan.FromMinutes(group.ReminderBeforeEndMins);
                var warningTime      = shiftEndDateTime + TimeSpan.FromMinutes(group.WarningAfterEndMins);

                notifSettings.TryGetValue(emp.Id, out var settings);

                // ── Reminder: X mins before shift end ──────────────────────
                if (IsWithinCurrentMinute(nowIst, reminderTime))
                {
                    _logger.LogInformation(
                        "Sending clock-out reminder to emp {Id} ({Name}) — shift ends at {End}.",
                        emp.Id, emp.FullName, shiftEndDateTime.ToString("hh:mm tt"));

                    var capturedEmp      = emp;
                    var capturedShiftEnd = shiftEndDateTime;
                    var capturedSettings = settings;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Email reminder (before shift end)
                            if (capturedSettings == null || capturedSettings.RemindersChannelEmail == true)
                                await emailService.SendClockOutReminderEmail(
                                    capturedEmp.Email, capturedEmp.FullName, capturedShiftEnd);

                            // Push reminder (before shift end)
                            // Message: "Your shift ends at 5:00 PM. Please remember to clock out."
                            if (capturedSettings?.RemindersChannelPush == true && !string.IsNullOrEmpty(capturedEmp.FcmToken))
                                await firebase.SendPushAsync(
                                    capturedEmp.FcmToken,
                                    "⏰ Clock-Out Reminder",
                                    $"Your shift ends at {capturedShiftEnd:hh:mm tt}. Please remember to clock out.");
                        }
                        catch { /* non-fatal */ }
                    }, ct);
                }

                // ── Warning: X mins after shift end ────────────────────────
                if (IsWithinCurrentMinute(nowIst, warningTime))
                {
                    _logger.LogInformation(
                        "Sending missed clock-out warning to emp {Id} ({Name}).", emp.Id, emp.FullName);

                    var capturedEmp      = emp;
                    var capturedShiftEnd = shiftEndDateTime;
                    var capturedSettings = settings;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Email warning (after shift end — missed clock-out)
                            if (capturedSettings == null || capturedSettings.RemindersChannelEmail == true)
                                await emailService.SendForgotClockOutEmail(
                                    capturedEmp.Email, capturedEmp.FullName, capturedShiftEnd);

                            // Push warning (after shift end)
                            // Message: "Your shift ended at 5:00 PM. Please submit a correction request to fix your attendance."
                            if (capturedSettings?.RemindersChannelPush == true && !string.IsNullOrEmpty(capturedEmp.FcmToken))
                                await firebase.SendPushAsync(
                                    capturedEmp.FcmToken,
                                    "⚠️ You Forgot to Clock Out",
                                    $"Your shift ended at {capturedShiftEnd:hh:mm tt}. Please submit a correction request to fix your attendance.");
                        }
                        catch { /* non-fatal */ }
                    }, ct);
                }

                // ── Mark Unmarked: after warning window passes ──────────────
                // Once the warning mail has been sent and the employee still hasn't clocked out,
                // mark the entry as Unmarked. This fires at warningTime + 1 min to ensure the
                // warning email is dispatched first, then the status is updated.
                // We check ClockOut == null (still active) regardless of existing status
                // so entries already marked Present or Late are also updated to Unmarked.
                if (nowIst > warningTime.AddMinutes(1) && entry.ClockOut == null &&
                    entry.AttendanceStatus != "Unmarked")
                {
                    entry.AttendanceStatus = "Unmarked";
                    entry.AttendanceDate   = entry.ClockIn.Date;
                    anyChanges             = true;
                    _logger.LogInformation(
                        "Marked emp {Id} ({Name}) as Unmarked — no clock-out after shift end + warning window.",
                        emp.Id, emp.FullName);
                }
            }

            if (anyChanges)
                await db.SaveChangesAsync(ct);
        }

        private static bool IsWithinCurrentMinute(DateTime now, DateTime target)
        {
            var diff = (now - target).TotalSeconds;
            return diff >= 0 && diff < 60;
        }

        private static TimeSpan GetShiftEndTime(WorkSchedule? schedule, DayOfWeek dow)
        {
            if (schedule == null || schedule.Arrangement != "Fixed") return TimeSpan.Zero;

            string dayKey = dow switch
            {
                DayOfWeek.Monday    => "Mon",
                DayOfWeek.Tuesday   => "Tue",
                DayOfWeek.Wednesday => "Wed",
                DayOfWeek.Thursday  => "Thu",
                DayOfWeek.Friday    => "Fri",
                DayOfWeek.Saturday  => "Sat",
                DayOfWeek.Sunday    => "Sun",
                _                   => ""
            };

            if (!string.IsNullOrWhiteSpace(schedule.WorkingDays))
            {
                var workDays = schedule.WorkingDays.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!workDays.Contains(dayKey)) return TimeSpan.Zero;
            }

            if (!string.IsNullOrWhiteSpace(schedule.DaySlotsJson) && schedule.DaySlotsJson != "{}")
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(schedule.DaySlotsJson).RootElement;
                    if (doc.TryGetProperty(dayKey, out var slot) &&
                        slot.TryGetProperty("end", out var endProp) &&
                        TimeSpan.TryParse(endProp.GetString(), out var ts))
                        return ts;
                }
                catch { }
            }

            return new TimeSpan(17, 0, 0); // default 5:00 PM
        }
    }
}
