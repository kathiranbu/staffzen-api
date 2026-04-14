using APM.StaffZen.API.Models;

namespace APM.StaffZen.API.Services
{
    /// <summary>
    /// Shared logic for computing an employee's attendance status
    /// (Present / Late) based on their work schedule and the actual clock-in time.
    /// Used by:
    ///   • AttendanceController  – at clock-out time
    ///   • AttendanceCorrectionController – when admin approves a correction
    ///   • AbsentMarkingService  – when checking whether a missed clock-in is a rest day
    /// </summary>
    public static class AttendanceStatusHelper
    {
        private static readonly string[] DayKeys = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        /// <summary>
        /// Resolves the employee's effective work schedule from the full list.
        /// Priority: employee's named schedule → default schedule → first available.
        /// </summary>
        public static WorkSchedule? ResolveSchedule(string? empScheduleName, List<WorkSchedule> allSchedules)
        {
            return (!string.IsNullOrWhiteSpace(empScheduleName)
                        ? allSchedules.FirstOrDefault(s => s.Name == empScheduleName)
                        : null)
                   ?? allSchedules.FirstOrDefault(s => s.IsDefault)
                   ?? allSchedules.FirstOrDefault();
        }

        /// <summary>
        /// Returns "Present" or "Late" by comparing the actual clock-in time
        /// against the scheduled start + grace period.
        ///
        /// Returns null when the schedule is unknown or the day has no slot,
        /// which means the caller should fall back to "Present".
        /// </summary>
        public static string ComputeClockInStatus(DateTime clockIn, WorkSchedule? schedule)
        {
            if (schedule == null || schedule.Arrangement != "Fixed"
                || string.IsNullOrWhiteSpace(schedule.DaySlotsJson)
                || schedule.DaySlotsJson == "{}")
                return "Present";

            string dayKey = DayKeys[(int)clockIn.DayOfWeek];

            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(schedule.DaySlotsJson).RootElement;
                if (doc.TryGetProperty(dayKey, out var slot)
                    && slot.TryGetProperty("start", out var startProp)
                    && TimeSpan.TryParse(startProp.GetString(), out var scheduledStart))
                {
                    var scheduledStartDt = clockIn.Date + scheduledStart;
                    int grace = schedule.GraceMinutes > 0 ? schedule.GraceMinutes : 5;
                    return clockIn > scheduledStartDt.AddMinutes(grace) ? "Late" : "Present";
                }
            }
            catch { /* parse failure → default Present */ }

            return "Present";
        }

        /// <summary>
        /// Returns the scheduled start time for a given date, or null when not applicable.
        /// </summary>
        public static DateTime? GetScheduledStart(DateTime date, WorkSchedule? schedule)
        {
            if (schedule == null || schedule.Arrangement != "Fixed"
                || string.IsNullOrWhiteSpace(schedule.DaySlotsJson)
                || schedule.DaySlotsJson == "{}")
                return null;

            string dayKey = DayKeys[(int)date.DayOfWeek];
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(schedule.DaySlotsJson).RootElement;
                if (doc.TryGetProperty(dayKey, out var slot)
                    && slot.TryGetProperty("start", out var startProp)
                    && TimeSpan.TryParse(startProp.GetString(), out var ts))
                    return date.Date + ts;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Returns true when the given date is a scheduled working day.
        /// If no schedule is configured, every day is treated as a working day.
        /// </summary>
        public static bool IsWorkingDay(DateTime date, WorkSchedule? schedule)
        {
            if (schedule == null) return true;

            string dayKey = DayKeys[(int)date.DayOfWeek];

            if (!string.IsNullOrWhiteSpace(schedule.WorkingDays))
            {
                var workDays = schedule.WorkingDays.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return workDays.Contains(dayKey);
            }

            return true;
        }
    }
}
