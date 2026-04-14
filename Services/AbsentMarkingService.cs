using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Services
{
    /// <summary>
    /// Background service that runs once per day (shortly after midnight) and
    /// creates Absent TimeEntry records for every active employee who did not
    /// clock in at all on the previous working day.
    ///
    /// How it works:
    ///   1. Waits until 00:05 IST each day (5 minutes after day roll-over).
    ///   2. For each active org member, checks whether the PREVIOUS calendar day
    ///      was a working day per their schedule.
    ///   3. If it was a working day and there are zero time entries for that day
    ///      → inserts a TimeEntry with AttendanceStatus = "Absent" and no ClockIn/Out
    ///        (ClockIn is set to midnight of that day as a placeholder).
    ///   4. Employees on approved leave are skipped (LeaveRequest.Status == "Approved").
    ///   5. Employees who clocked in but their status is already set (Present/Late/Unmarked)
    ///      are skipped — we only fill in the genuinely missing days.
    /// </summary>
    public class AbsentMarkingService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<AbsentMarkingService> _logger;

        // IST = UTC+5:30
        private static readonly TimeSpan IstOffset = new(5, 30, 0);

        public AbsentMarkingService(IServiceProvider services, ILogger<AbsentMarkingService> logger)
        {
            _services = services;
            _logger   = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AbsentMarkingService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var nowIst     = DateTime.UtcNow + IstOffset;
                    var runTarget  = nowIst.Date.AddHours(0).AddMinutes(5); // 00:05 IST today

                    // If we've already passed 00:05 today, schedule for tomorrow
                    if (nowIst >= runTarget)
                        runTarget = runTarget.AddDays(1);

                    var delay = runTarget - nowIst;
                    _logger.LogInformation("AbsentMarkingService: next run in {Mins:F0} min (at {Target} IST).",
                        delay.TotalMinutes, runTarget.ToString("yyyy-MM-dd HH:mm"));

                    await Task.Delay(delay, stoppingToken);

                    await RunMarkingAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AbsentMarkingService: unexpected error.");
                    // Back-off 5 minutes then retry so we don't spin-loop on DB errors
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("AbsentMarkingService stopped.");
        }

        private async Task RunMarkingAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Yesterday in IST
            var nowIst    = DateTime.UtcNow + IstOffset;
            var yesterday = nowIst.Date.AddDays(-1);

            _logger.LogInformation("AbsentMarkingService: marking absent for {Date}.", yesterday.ToString("yyyy-MM-dd"));

            // ── Load all work schedules once ─────────────────────────────
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

            // ── Load all active org members ──────────────────────────────
            var members = await db.OrganizationMembers
                .Include(m => m.Employee)
                .Where(m => m.IsActive && m.Employee != null)
                .Select(m => new
                {
                    m.OrganizationId,
                    m.Employee!.Id,
                    m.Employee.WorkSchedule,
                    m.Employee.Email,
                    m.Employee.FullName
                })
                .ToListAsync(ct);

            if (!members.Any()) return;

            var empIds = members.Select(m => m.Id).Distinct().ToList();

            // ── Fetch all time entries for yesterday in one query ────────
            var dayStart = yesterday;
            var dayEnd   = yesterday.AddDays(1);

            var existingEntries = await db.TimeEntries
                .Where(t => empIds.Contains(t.EmployeeId)
                         && !t.IsManual
                         && !t.IsHourEntry
                         && !t.IsBreakEntry
                         && t.ClockIn >= dayStart
                         && t.ClockIn < dayEnd)
                .Select(t => new { t.EmployeeId, t.OrganizationId })
                .ToListAsync(ct);

            var clockedInSet = existingEntries
                .Select(e => (e.EmployeeId, e.OrganizationId))
                .ToHashSet();

            // ── Fetch approved leaves for yesterday ──────────────────────
            HashSet<int> onLeaveEmpIds = new();
            try
            {
                var leaves = await db.LeaveRequests
                    .Where(l => l.Status == "Approved"
                             && l.StartDate <= yesterday
                             && l.EndDate   >= yesterday)
                    .Select(l => l.EmployeeId)
                    .ToListAsync(ct);
                onLeaveEmpIds = leaves.ToHashSet();
            }
            catch { /* leave table may not exist yet */ }

            // ── Mark absent for each member who didn't clock in ──────────
            int marked = 0;
            var newEntries = new List<TimeEntry>();

            foreach (var m in members)
            {
                // Skip if on approved leave
                if (onLeaveEmpIds.Contains(m.Id)) continue;

                // Skip if already clocked in yesterday (for this org)
                if (clockedInSet.Contains((m.Id, m.OrganizationId))) continue;

                // Resolve this employee's schedule
                var schedule = AttendanceStatusHelper.ResolveSchedule(m.WorkSchedule, allSchedules)
                               ?? defaultSchedule;

                // Skip if yesterday was not a working day
                if (!AttendanceStatusHelper.IsWorkingDay(yesterday, schedule)) continue;

                // Create an Absent placeholder entry
                // ClockIn = midnight of that day (placeholder; no actual clock-in occurred)
                newEntries.Add(new TimeEntry
                {
                    EmployeeId       = m.Id,
                    OrganizationId   = m.OrganizationId,
                    ClockIn          = yesterday,          // midnight placeholder
                    ClockOut         = null,
                    WorkedHours      = null,
                    IsManual         = true,               // flag so it's excluded from live-session queries
                    AttendanceStatus = "Absent",
                    AttendanceDate   = yesterday
                });
                marked++;
            }

            if (newEntries.Any())
            {
                db.TimeEntries.AddRange(newEntries);
                await db.SaveChangesAsync(ct);
            }

            _logger.LogInformation(
                "AbsentMarkingService: marked {Count} employees as Absent for {Date}.",
                marked, yesterday.ToString("yyyy-MM-dd"));
        }
    }
}
