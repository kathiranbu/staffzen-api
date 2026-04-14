using APM.StaffZen.API.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace APM.StaffZen.API.Services
{
    /// <summary>
    /// Background service that automatically clocks out employees who are still
    /// clocked in when the configured auto clock-out rule fires.
    ///
    /// Two independent modes (both can be active at once):
    ///
    ///   1. "After duration"  — each employee is clocked out N h/m AFTER the
    ///                          scheduled END TIME for that day.
    ///                          e.g. Schedule ends 18:30, After = 0h 10m
    ///                               → everyone on that schedule is clocked
    ///                                 out at 18:40 if still open.
    ///                          Each schedule (per-employee assignment) is
    ///                          handled independently, so employees on shorter
    ///                          shifts (e.g. Thu 09:00–15:00 + 10 min = 15:10)
    ///                          are clocked out at the right time for THEIR day.
    ///                          Falls back to the org default schedule when no
    ///                          specific schedule is assigned to an employee.
    ///
    ///   2. "At time"         — everyone clocked out at a fixed wall-clock time
    ///                          (e.g. 23:00).
    ///
    /// Runs every 30 seconds — fine-grained enough to never miss a minute.
    /// ClockOut == null is the idempotency guard; re-runs are harmless.
    ///
    /// BUG FIXES vs original:
    ///   FIX-1  Zero after-duration (0h 0m) is now allowed — it fires exactly AT
    ///           the schedule end time (instant buffer). Previously it was skipped
    ///           silently with a warning.
    ///   FIX-2  WorkSchedule.OrganizationId is int? — we use an explicit int local
    ///           so EF Core emits "WHERE OrganizationId = @p" (non-null equality)
    ///           instead of a nullable comparison that silently matches nothing.
    ///   FIX-3  Rest-day guard: if today's DaySlotsJson slot has no "end" key (rest
    ///           day / day off) we skip gracefully with an informational log instead
    ///           of a warning.  Previously null returned without explanation.
    ///   FIX-4  The target-past-midnight guard now logs clearly and skips, just as
    ///           before, but also works when afterDuration is 0 (endTime == midnight).
    ///   FIX-5  Schedule name matching trims both sides and uses OrdinalIgnoreCase,
    ///           preventing "Morning Shift " vs "Morning Shift" mismatches (kept).
    /// </summary>
    public class AutoClockOutService : BackgroundService
    {
        private readonly IServiceProvider              _services;
        private readonly ILogger<AutoClockOutService> _logger;

        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

        private static readonly Dictionary<DayOfWeek, string> DayKeys = new()
        {
            { DayOfWeek.Monday,    "Mon" },
            { DayOfWeek.Tuesday,   "Tue" },
            { DayOfWeek.Wednesday, "Wed" },
            { DayOfWeek.Thursday,  "Thu" },
            { DayOfWeek.Friday,    "Fri" },
            { DayOfWeek.Saturday,  "Sat" },
            { DayOfWeek.Sunday,    "Sun" },
        };

        public AutoClockOutService(IServiceProvider services,
                                   ILogger<AutoClockOutService> logger)
        {
            _services = services;
            _logger   = logger;
        }

        // ── Entry point ───────────────────────────────────────────────────────
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AutoClockOutService started — checking every 30 s.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try   { await RunCheckAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoClockOutService error — will retry in 30 s.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }

            _logger.LogInformation("AutoClockOutService stopped.");
        }

        // ── Main check ────────────────────────────────────────────────────────
        private async Task RunCheckAsync(CancellationToken ct)
        {
            var now = DateTime.Now; // IST local time

            using var scope = _services.CreateScope();
            var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Only fetch orgs with the master toggle ON and at least one mode enabled.
            List<APM.StaffZen.API.Models.TimeTrackingPolicy> policies;
            try
            {
                policies = await db.TimeTrackingPolicies
                    .Where(p => p.AutoClockOutEnabled &&
                                (p.AutoClockOutAtTime || p.AutoClockOutAfterDuration))
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AutoClockOutService: could not read TimeTrackingPolicies " +
                    "(table may not exist yet).");
                return;
            }

            foreach (var policy in policies)
            {
                // MODE 1: fixed wall-clock time
                if (policy.AutoClockOutAtTime)
                    await RunAtTimeCheckAsync(db, policy, now, ct);

                // MODE 2: schedule end time + after-duration
                if (policy.AutoClockOutAfterDuration)
                    await RunAfterDurationCheckAsync(db, policy, now, ct);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // MODE 1 — clock everyone out at a fixed wall-clock time
        // ─────────────────────────────────────────────────────────────────────
        private async Task RunAtTimeCheckAsync(
            ApplicationDbContext db,
            APM.StaffZen.API.Models.TimeTrackingPolicy policy,
            DateTime now,
            CancellationToken ct)
        {
            var configuredTime = (policy.AutoClockOutTime ?? "").Trim();
            if (!TimeOnly.TryParse(configuredTime, out var atTime))
            {
                _logger.LogWarning(
                    "AutoClockOut (AtTime) org {OrgId}: cannot parse '{T}' — skipping.",
                    policy.OrganizationId, configuredTime);
                return;
            }

            var target = now.Date.Add(atTime.ToTimeSpan());
            if (now < target || now >= target.AddMinutes(1)) return;

            _logger.LogInformation(
                "AutoClockOut (AtTime) org {OrgId}: matched {Time}.",
                policy.OrganizationId, configuredTime);

            var entries = await GetOpenEntriesForOrgAsync(db, policy.OrganizationId, ct);
            if (!entries.Any()) return;

            await ClockOutEntriesAsync(db, entries, now, ct);

            _logger.LogInformation(
                "AutoClockOut (AtTime) org {OrgId}: clocked out {Count} session(s).",
                policy.OrganizationId, entries.Count);
        }

        // ─────────────────────────────────────────────────────────────────────
        // MODE 2 — schedule end time + after-duration buffer
        //
        // Logic:
        //   targetClockOut = today's date + schedule end time for today + afterDuration
        //
        // Example (your setup):
        //   Thursday schedule: 09:00 – 15:00
        //   After = 0h 40m
        //   targetClockOut = 15:00 + 00:40 = 15:40
        //   → At 15:40 any employee still open gets auto clocked out.
        //
        // Employees on different named schedules are handled independently:
        //   Mon–Wed, Fri–Sat schedule: 09:00 – 18:30
        //   Thursday schedule: 09:00 – 15:00
        //   → Mon–Wed, Fri–Sat target = 18:30 + 0h40m = 19:10
        //   → Thursday target        = 15:00 + 0h40m = 15:40
        //
        // FIX-1: afterDuration of 0h 0m is now ALLOWED — it fires exactly AT
        //         the schedule end time (no buffer). The original code refused 0h 0m.
        // ─────────────────────────────────────────────────────────────────────
        private async Task RunAfterDurationCheckAsync(
            ApplicationDbContext db,
            APM.StaffZen.API.Models.TimeTrackingPolicy policy,
            DateTime now,
            CancellationToken ct)
        {
            var afterDuration = TimeSpan.FromHours(policy.AutoClockOutAfterHours)
                              + TimeSpan.FromMinutes(policy.AutoClockOutAfterMins);

            // FIX-1: removed the old "skip if 0h 0m" guard — 0 buffer is valid.

            var todayKey = DayKeys.TryGetValue(now.DayOfWeek, out var k) ? k : null;
            if (todayKey == null) return;

            // FIX-2: use explicit int local so EF Core emits non-null equality.
            int orgId = policy.OrganizationId;

            // Include schedules saved with OrganizationId = NULL (legacy rows created
            // before the org-stamping fix) so existing data still works.
            var orgSchedules = await db.WorkSchedules
                .Where(s => s.OrganizationId == orgId || s.OrganizationId == null)
                .ToListAsync(ct);

            if (!orgSchedules.Any())
            {
                _logger.LogInformation(
                    "AutoClockOut (After) org {OrgId}: no schedules found — skipping.",
                    orgId);
                return;
            }

            var defaultSchedule = orgSchedules.FirstOrDefault(s => s.IsDefault)
                                ?? orgSchedules.First();

            // FIX-5: trim schedule names for reliable matching.
            var scheduleByName = orgSchedules.ToDictionary(
                s => s.Name.Trim(),
                s => s,
                StringComparer.OrdinalIgnoreCase);

            // Load active members with their assigned schedule names.
            var members = (await db.OrganizationMembers
                .Where(m => m.OrganizationId == orgId && m.IsActive)
                .Join(db.Employees,
                      om => om.EmployeeId,
                      e  => e.Id,
                      (om, e) => new { e.Id, e.WorkSchedule })
                .ToListAsync(ct))
                .Select(m => new { m.Id, WorkSchedule = (m.WorkSchedule ?? "").Trim() })
                .ToList();

            if (!members.Any())
            {
                _logger.LogInformation(
                    "AutoClockOut (After) org {OrgId}: no active members — skipping.", orgId);
                return;
            }

            // Group members by their resolved schedule.
            var scheduleGroups = members.GroupBy(m =>
            {
                if (!string.IsNullOrEmpty(m.WorkSchedule) &&
                    scheduleByName.TryGetValue(m.WorkSchedule, out var named))
                    return named;
                return defaultSchedule;
            }, new ScheduleEqualityComparer());

            var today    = now.Date;
            var tomorrow = today.AddDays(1);

            foreach (var group in scheduleGroups)
            {
                var schedule  = group.Key;
                var memberIds = group.Select(m => m.Id).ToList();
                if (!memberIds.Any()) continue;

                // Parse today's end time from DaySlotsJson.
                var endTimeStr = GetDayEndTime(schedule.DaySlotsJson, todayKey);
                if (endTimeStr == null)
                {
                    // FIX-3: rest day or missing slot — info only, not a warning.
                    _logger.LogInformation(
                        "AutoClockOut (After) org {OrgId} schedule '{Sched}': " +
                        "no end time for '{Day}' (rest day?) — skipping.",
                        orgId, schedule.Name, todayKey);
                    continue;
                }

                if (!TimeSpan.TryParse(endTimeStr, out var endTimeOfDay))
                {
                    _logger.LogWarning(
                        "AutoClockOut (After) org {OrgId} schedule '{Sched}': " +
                        "cannot parse end time '{End}' — skipping.",
                        orgId, schedule.Name, endTimeStr);
                    continue;
                }

                // TARGET = today + schedule end time + buffer
                var targetClockOut = today + endTimeOfDay + afterDuration;

                // FIX-4: if target rolls past midnight skip with a clear log.
                if (targetClockOut.Date != today)
                {
                    _logger.LogInformation(
                        "AutoClockOut (After) org {OrgId} schedule '{Sched}': " +
                        "target {Target} is past midnight — skipping for today.",
                        orgId, schedule.Name, targetClockOut.ToString("HH:mm dd-MMM"));
                    continue;
                }

                _logger.LogInformation(
                    "AutoClockOut (After) org {OrgId} schedule '{Sched}': " +
                    "end={End} + buffer={Buffer} → target={Target} | now={Now}",
                    orgId, schedule.Name,
                    endTimeStr, afterDuration.ToString(@"h\:mm"),
                    targetClockOut.ToString("HH:mm"), now.ToString("HH:mm:ss"));

                // Fire window: target <= now < target + 1 min
                // (service runs every 30 s so we never miss the minute)
                if (now < targetClockOut || now >= targetClockOut.AddMinutes(1)) continue;

                // Find all still-open entries clocked IN today for this schedule's members.
                var openEntries = await db.TimeEntries
                    .Where(t => t.ClockOut   == null
                             && !t.IsManual
                             && !t.IsHourEntry
                             && t.ClockIn    >= today
                             && t.ClockIn    <  tomorrow
                             && (   (t.OrganizationId == orgId     && memberIds.Contains(t.EmployeeId))
                                 || (t.OrganizationId == null      && memberIds.Contains(t.EmployeeId))))
                    .ToListAsync(ct);

                if (!openEntries.Any())
                {
                    _logger.LogInformation(
                        "AutoClockOut (After) org {OrgId} schedule '{Sched}': " +
                        "no open sessions at {Target}.",
                        orgId, schedule.Name, targetClockOut.ToString("HH:mm"));
                    continue;
                }

                await ClockOutEntriesAsync(db, openEntries, targetClockOut, ct);

                _logger.LogInformation(
                    "AutoClockOut (After) org {OrgId} schedule '{Sched}': " +
                    "clocked out {Count} session(s) at {Target}.",
                    orgId, schedule.Name, openEntries.Count, targetClockOut.ToString("HH:mm"));
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<List<APM.StaffZen.API.Models.TimeEntry>> GetOpenEntriesForOrgAsync(
            ApplicationDbContext db, int orgId, CancellationToken ct)
        {
            var memberIds = await db.OrganizationMembers
                .Where(m => m.OrganizationId == orgId && m.IsActive)
                .Select(m => m.EmployeeId)
                .ToListAsync(ct);

            if (!memberIds.Any()) return new();

            var today    = DateTime.Today;
            var tomorrow = today.AddDays(1);

            return await db.TimeEntries
                .Where(t => t.ClockOut   == null
                         && !t.IsManual
                         && !t.IsHourEntry
                         && t.ClockIn    >= today
                         && t.ClockIn    <  tomorrow
                         && (   t.OrganizationId == orgId
                             || (t.OrganizationId == null && memberIds.Contains(t.EmployeeId))))
                .ToListAsync(ct);
        }

        /// <summary>
        /// Sets ClockOut (to the exact targetTime) and recalculates WorkedHours,
        /// then persists all changes in one SaveChanges call.
        /// </summary>
        private static async Task ClockOutEntriesAsync(
            ApplicationDbContext db,
            List<APM.StaffZen.API.Models.TimeEntry> entries,
            DateTime targetTime,
            CancellationToken ct)
        {
            foreach (var entry in entries)
            {
                entry.ClockOut = targetTime;
                var duration   = entry.ClockOut.Value - entry.ClockIn;
                var h          = (int)duration.TotalHours;
                var m          = duration.Minutes;
                entry.WorkedHours = h > 0 ? $"{h}h {m}m" : $"{m}m";
            }
            await db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Parses DaySlotsJson → { "Mon": { "start": "09:00", "end": "18:30" }, ... }
        /// Returns the "end" string for dayKey, or null if the day is a rest day / missing.
        /// </summary>
        private static string? GetDayEndTime(string daySlotsJson, string dayKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(daySlotsJson) || daySlotsJson == "{}") return null;
                var doc = JsonDocument.Parse(daySlotsJson);
                if (!doc.RootElement.TryGetProperty(dayKey, out var slot)) return null;
                if (!slot.TryGetProperty("end", out var endProp)) return null;
                var val = endProp.GetString()?.Trim();
                return string.IsNullOrEmpty(val) ? null : val;
            }
            catch { return null; }
        }

        private class ScheduleEqualityComparer
            : IEqualityComparer<APM.StaffZen.API.Models.WorkSchedule>
        {
            public bool Equals(APM.StaffZen.API.Models.WorkSchedule? x,
                               APM.StaffZen.API.Models.WorkSchedule? y)
                => x?.Id == y?.Id;
            public int GetHashCode(APM.StaffZen.API.Models.WorkSchedule obj)
                => obj.Id.GetHashCode();
        }
    }
}
