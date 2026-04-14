using APM.StaffZen.API.Services;
using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AttendanceController> _logger;
        private readonly EmailService _emailService;
        private readonly FirebaseService _firebaseService;
        private readonly IServiceScopeFactory _scopeFactory;

        // IST = UTC+5:30, no DST — fixed offset, no TimeZoneInfo dependency.
        private static readonly TimeSpan _istOffset = new TimeSpan(5, 30, 0);

        /// <summary>Convert a UTC DateTime (EF returns Kind=Unspecified) to IST.</summary>
        private static DateTime ToIst(DateTime utc) => utc + _istOffset;

        /// <summary>Convert an IST DateTime to UTC.</summary>
        private static DateTime IstToUtc(DateTime ist) => ist - _istOffset;

        public AttendanceController(ApplicationDbContext context, ILogger<AttendanceController> logger,
            EmailService emailService, FirebaseService firebaseService, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
            _firebaseService = firebaseService;
            _scopeFactory = scopeFactory;
        }

        [HttpGet("status/{employeeId}")]
        public async Task<IActionResult> GetStatus(int employeeId, [FromQuery] int? organizationId = null)
        {
            try
            {
                // Find an open (no ClockOut) session for this employee.
                // Priority 1: a real live session (!IsManual) — the standard clock-in.
                // Priority 2: an admin-back-dated session (IsManual=true, still open) — created
                //             when an admin overrides the start time of an active session.
                var baseQuery = _context.TimeEntries
                    .Where(t => t.EmployeeId == employeeId &&
                                t.ClockOut   == null       &&
                                !t.IsHourEntry);

                if (organizationId.HasValue && organizationId.Value > 0)
                    baseQuery = baseQuery.Where(t =>
                        t.OrganizationId == organizationId.Value ||
                        t.OrganizationId == null);

                // Prefer non-manual (real live session); fall back to manual open entry
                var open = await baseQuery
                    .OrderBy(t => t.IsManual)          // false (0) sorts before true (1)
                    .ThenByDescending(t => t.ClockIn)
                    .FirstOrDefaultAsync();

                if (open == null) return Ok(new { isClockedIn = false });
                return Ok(new { isClockedIn = true, entryId = open.Id, clockIn = open.ClockIn });
            }
            catch (Exception ex) { _logger.LogError(ex, "GetStatus failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpGet("history/{employeeId}")]
        public async Task<IActionResult> GetHistory(int employeeId, [FromQuery] int? organizationId = null)
        {
            try
            {
                var todayStart = DateTime.Now.Date;
                var orgFilter  = BuildOrgFilter(organizationId);
                var results = await ReadTimeEntriesRawAsync(
                    $"SELECT Id, ClockIn, ClockOut, WorkedHours, IsManual, IsHourEntry, IsBreakEntry, ClockInSelfieUrl, ClockOutSelfieUrl FROM TimeEntries WHERE EmployeeId = @empId AND ClockIn >= @start{orgFilter} ORDER BY ClockIn",
                    employeeId, todayStart, null, organizationId);
                return Ok(results);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetHistory failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpGet("history/{employeeId}/bydate")]
        public async Task<IActionResult> GetHistoryByDate(int employeeId, [FromQuery] string date, [FromQuery] int? organizationId = null)
        {
            try
            {
                if (!DateTime.TryParse(date, out var parsedDate)) return BadRequest(new { error = "Invalid date." });

                var dayStart  = parsedDate.Date;
                var dayEnd    = dayStart.AddDays(1);
                var lookback  = dayStart.AddDays(-2);
                var orgFilter = BuildOrgFilter(organizationId);

                // ── A: entries that STARTED today ──────────────────────────────────────
                var todayEntries = await ReadTimeEntriesRawAsync(
                    "SELECT Id, ClockIn, ClockOut, WorkedHours, IsManual, IsHourEntry, IsBreakEntry, ClockInSelfieUrl, ClockOutSelfieUrl " +
                    "FROM TimeEntries " +
                    $"WHERE EmployeeId = @empId AND ClockIn >= @start AND ClockIn < @end{orgFilter} " +
                    "ORDER BY ClockIn",
                    employeeId, dayStart, dayEnd, organizationId);

                // ── B: entries that STARTED before today but CLOCK OUT falls today ─────
                var overnightRaw = await ReadTimeEntriesRawAsync(
                    "SELECT Id, ClockIn, ClockOut, WorkedHours, IsManual, IsHourEntry, IsBreakEntry, ClockInSelfieUrl, ClockOutSelfieUrl " +
                    "FROM TimeEntries " +
                    $"WHERE EmployeeId = @empId AND ClockIn >= @start AND ClockIn < @end AND ClockOut >= @end AND ClockOut IS NOT NULL{orgFilter}",
                    employeeId, lookback, dayStart, organizationId);

                // Keep only entries whose ClockOut actually falls within today (< dayEnd).
                // Entries where ClockOut is tomorrow or beyond are excluded — they'll appear
                // as overnight carry-overs on their respective future dates.
                var overnightEntries = overnightRaw
                    .Cast<dynamic>()
                    .Where(r => ((DateTime?)r.clockOut).HasValue && ((DateTime)r.clockOut) < dayEnd)
                    .ToList();

                // For group B (overnight carry-overs), cap clockIn at dayStart (midnight)
                // and compute worked minutes as (clockOut - dayStart), i.e. the portion in today.
                // We also add isOvernightCarryOver = true so the UI can display them at the
                // top of the day's list with a visual indicator.
                var overnightMapped = new List<object>();
                foreach (dynamic raw in overnightEntries)
                {
                    DateTime clockOut  = raw.clockOut;
                    // Only the slice from midnight → actual clockOut counts for today.
                    var todayMins      = (int)(clockOut - dayStart).TotalMinutes;
                    int h = todayMins / 60, m = todayMins % 60;
                    string wh          = h > 0 ? $"{h}h {m}m" : $"{m}m";

                    overnightMapped.Add(new {
                        id                = (int)raw.id,
                        clockIn           = dayStart,          // display as midnight carry-over
                        clockOut          = clockOut,
                        workedHours       = wh,
                        isManual          = (bool)raw.isManual,
                        isHourEntry       = (bool)raw.isHourEntry,
                        isBreakEntry      = (bool)raw.isBreakEntry,
                        isOvernightCarryOver = true,           // flag for UI
                        clockInSelfieUrl  = (string?)raw.clockInSelfieUrl,
                        clockOutSelfieUrl = (string?)raw.clockOutSelfieUrl
                    });
                }

                // For group A entries: if a ClockOut spills into tomorrow, cap it at dayEnd
                // and recompute workedHours to show only today's portion (the remainder will
                // appear as an overnight carry-over on tomorrow's Time Entries page).
                var todayMapped = new List<object>();
                foreach (dynamic raw in todayEntries)
                {
                    DateTime  clockIn   = raw.clockIn;
                    DateTime? clockOut  = raw.clockOut;
                    string?   wh        = raw.workedHours;
                    bool      isHour    = raw.isHourEntry;
                    bool      isManual  = raw.isManual;
                    bool      isBreak   = raw.isBreakEntry;

                    // Overnight: ClockIn today, ClockOut tomorrow (or still open past midnight)
                    bool crossesMidnight = clockOut.HasValue && clockOut.Value >= dayEnd;
                    if (!isHour && crossesMidnight)
                    {
                        // Today's portion = ClockIn → midnight
                        var todayMins = (int)(dayEnd - clockIn).TotalMinutes;
                        int h = todayMins / 60, m = todayMins % 60;
                        wh       = h > 0 ? $"{h}h {m}m" : $"{m}m";
                        clockOut = null;    // treat as "open / continuing" for today's display
                    }

                    todayMapped.Add(new {
                        id                   = (int)raw.id,
                        clockIn,
                        clockOut,
                        workedHours          = wh,
                        isManual,
                        isHourEntry          = isHour,
                        isBreakEntry         = isBreak,
                        isOvernightCarryOver = false,
                        clockInSelfieUrl     = (string?)raw.clockInSelfieUrl,
                        clockOutSelfieUrl    = (string?)raw.clockOutSelfieUrl
                    });
                }

                // Return: overnight carry-overs first (shown at top), then today's entries in order.
                var combined = overnightMapped.Concat(todayMapped).ToList();
                return Ok(combined);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetHistoryByDate failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpGet("history/{employeeId}/week")]
        public async Task<IActionResult> GetHistoryByWeek(int employeeId, [FromQuery] string weekStart, [FromQuery] int? organizationId = null)
        {
            try
            {
                if (!DateTime.TryParse(weekStart, out var parsedStart)) return BadRequest(new { error = "Invalid date." });
                var start     = parsedStart.Date;
                var end       = start.AddDays(7);
                var orgFilter = BuildOrgFilter(organizationId);
                var results = await ReadTimeEntriesRawAsync(
                    $"SELECT Id, ClockIn, ClockOut, WorkedHours, IsManual, IsHourEntry, IsBreakEntry, ClockInSelfieUrl, ClockOutSelfieUrl FROM TimeEntries WHERE EmployeeId = @empId AND ClockIn >= @start AND ClockIn < @end{orgFilter} ORDER BY ClockIn",
                    employeeId, start, end, organizationId);
                return Ok(results);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetHistoryByWeek failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpGet("allmembers")]
        public async Task<IActionResult> GetAllMembers([FromQuery] int? organizationId = null)
        {
            try
            {
                // Build a base query of active employees, then scope to org members when an
                // organizationId is supplied.  This is the core of org-based user visibility:
                // Varun selecting "ABC" sees only Arun/Varun/Karthik; selecting "XYZ" sees
                // only Chandru/Varun/Mukesh/Rakesh — even though he belongs to both orgs.
                IQueryable<Employee> baseQuery = _context.Employees.Where(e => e.IsActive);

                if (organizationId.HasValue && organizationId.Value > 0)
                {
                    var orgMemberIds = await _context.OrganizationMembers
                        .Where(m => m.OrganizationId == organizationId.Value && m.IsActive)
                        .Select(m => m.EmployeeId)
                        .ToListAsync();

                    baseQuery = baseQuery.Where(e => orgMemberIds.Contains(e.Id));
                }

                var members = await baseQuery
                    .Select(e => new
                    {
                        id = e.Id,
                        fullName = e.FullName,
                        email = e.Email,
                        groupId = e.GroupId,
                        groupName = e.GroupId != null ? _context.Groups.Where(g => g.Id == e.GroupId).Select(g => g.Name).FirstOrDefault() : null,
                        isClockedIn = _context.TimeEntries.Any(t => t.EmployeeId == e.Id && t.ClockOut == null && !t.IsManual && !t.IsHourEntry),
                        clockIn = _context.TimeEntries
                                        .Where(t => t.EmployeeId == e.Id && t.ClockOut == null)
                                        .OrderByDescending(t => t.ClockIn)
                                        .Select(t => (DateTime?)t.ClockIn)
                                        .FirstOrDefault()
                    })
                    .OrderBy(e => e.fullName)
                    .ToListAsync();
                return Ok(members);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetAllMembers failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // GET api/Attendance/daily?date=2026-03-11
        // Calculates per-day tracked/regular hours with full work-schedule awareness:
        //
        //  SPLIT TIME
        //    The day boundary is defined by WorkSchedule.SplitAt (default "00:00" = midnight).
        //    An entry "belongs" to a day when its ClockIn falls within [splitStart, splitEnd).
        //    Sessions that cross the split point are capped at splitEnd for the starting day
        //    and the overflow is credited to the next day's sheet.
        //
        //  IncludeBeforeStart  (Fixed arrangement only)
        //    When unchecked: time tracked before the scheduled start time for that day
        //    is excluded from payroll/regular hours.  The effective ClockIn used for
        //    calculation is max(actualClockIn, scheduledStart).
        //    When checked (or for Flexible/Weekly): full raw time is used.
        //
        //  AUTO-DEDUCTIONS
        //    If total tracked minutes for the day exceed the threshold, deduct the
        //    configured minutes from the payroll/regular total.
        //
        //  Break entries (IsBreakEntry=true) are EXCLUDED from time calculation.
        //  Hour entries (IsHourEntry=true) bypass all schedule rules — they contribute
        //    their WorkedHours directly and are never clipped.
        //  A real live session (no ClockOut, !IsManual, !IsHourEntry) = "ongoing" on TODAY only.
        //  Open manual clock-in with no clock-out on a PAST day = skip (incomplete, not live).
        //  LastOut = only shown when the actual ClockOut falls on this day (per split boundary).
        [HttpGet("daily")]
        public async Task<IActionResult> GetDailyTimesheet([FromQuery] string date,
                                                            [FromQuery] int? organizationId = null)
        {
            try
            {
                if (!DateTime.TryParse(date, out var parsedDate))
                    return BadRequest(new { error = "Invalid date." });

                // ── Load all work schedules once ────────────────────────────────────
                // Each employee may have their own schedule (Employee.WorkSchedule = schedule name).
                // We load all schedules and look up per-employee by name below.
                // Falls back to: (1) the default schedule, (2) any schedule, (3) null = no schedule.
                List<WorkSchedule> allSchedules = new();
                WorkSchedule? defaultSchedule = null;
                try
                {
                    allSchedules = await _context.WorkSchedules.ToListAsync();
                    defaultSchedule = allSchedules.FirstOrDefault(s => s.IsDefault)
                                   ?? allSchedules.FirstOrDefault();
                }
                catch { /* table may not exist yet — treat as no schedule */ }

                // ── Resolve the split point (org-wide, from default schedule) ─────────
                // SplitAt is "HH:mm" (default "00:00" = midnight).
                // IMPORTANT: "12:00 am" and "12:00" both mean midnight in 12-hour clock,
                // but TimeSpan.TryParse("12:00") returns 12 hours (noon), which would
                // shift dayStart to noon and hide all morning clock-outs. We normalise
                // any "12:xx am" or bare "12:00" value to zero (midnight) before parsing.
                TimeSpan splitOffset = TimeSpan.Zero;
                if (defaultSchedule != null && !string.IsNullOrWhiteSpace(defaultSchedule.SplitAt))
                {
                    var rawSplit = defaultSchedule.SplitAt.Trim();
                    // Treat explicit "00:00", "0:00", any "12:…am/AM" variant as midnight
                    bool isMidnight =
                        rawSplit == "00:00" ||
                        rawSplit == "0:00"  ||
                        (rawSplit.StartsWith("12:", StringComparison.Ordinal) &&
                         rawSplit.EndsWith("am", StringComparison.OrdinalIgnoreCase));

                    if (!isMidnight && TimeSpan.TryParse(rawSplit, out var parsed) && parsed > TimeSpan.Zero)
                        splitOffset = parsed;
                }

                var dayStart = parsedDate.Date + splitOffset;
                var dayEnd   = dayStart.AddDays(1);
                // Use IST "now" throughout so that live-session durations are correct
                // regardless of the server's OS timezone (which may be UTC).
                // The DB stores ClockIn/ClockOut as IST local (DateTimeKind.Unspecified),
                // so we must compare against IST-local time, not UTC DateTime.Now.
                var istNow  = DateTime.UtcNow.Add(_istOffset);
                var isToday  = parsedDate.Date == istNow.Date;

                // NOTE: isFixed, includeBeforeStart, daySlots, autoDeductions
                // are all resolved PER EMPLOYEE inside the employees.Select lambda below,
                // because each employee may be assigned a different work schedule.

                // ── Day-of-week key helper (matches DaySlotsJson keys) ────────────
                static string DayKey(DateTime d) => d.DayOfWeek switch
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

                var employees = await _context.Employees
                    .Where(e => e.IsActive)
                    .OrderBy(e => e.FullName)
                    .Select(e => new { e.Id, e.FullName, e.WorkSchedule })
                    .ToListAsync();

                // ── Org-scoped visibility ─────────────────────────────────────────
                // When organizationId is provided, restrict the timesheet to members
                // of that organization only.
                if (organizationId.HasValue && organizationId.Value > 0)
                {
                    var orgMemberIds = await _context.OrganizationMembers
                        .Where(m => m.OrganizationId == organizationId.Value && m.IsActive)
                        .Select(m => m.EmployeeId)
                        .ToListAsync();

                    employees = employees.Where(e => orgMemberIds.Contains(e.Id)).ToList();
                }

                // ── Approved time-off requests that cover today ───────────────────
                // A null EndDate means a single-day leave (EndDate == StartDate).
                // Previously, null EndDate matched every day >= StartDate — causing
                // the leave label to appear on ALL future days indefinitely.
                var approvedLeaves = await _context.TimeOffRequests
                    .Include(r => r.Policy)
                    .Where(r => r.Status == "Approved"
                             && r.StartDate.Date <= parsedDate.Date
                             && (r.EndDate != null
                                    ? r.EndDate.Value.Date >= parsedDate.Date
                                    : r.StartDate.Date == parsedDate.Date))
                    .ToListAsync();

                // ── Day-of-week string for rest-day check ─────────────────────────
                string todayDayKey = DayKey(parsedDate);

                // ── Load entries in two groups ────────────────────────────────────
                //
                // Group A: entries whose ClockIn falls within this day's split window.
                // Group B: non-manual real sessions that started BEFORE this split-day
                //          and whose ClockOut spills into this split-day.
                //          We look back up to 30 days to handle multi-day sessions
                //          (e.g. clocked in Saturday evening, clocked out Monday morning
                //          spans 2 nights — a 1-day lookback would miss it entirely).
                //
                // Exclude break entries — they never contribute to tracked time.
                var farLookback = dayStart.AddDays(-30);
                var overlapping = await _context.TimeEntries
                    .Where(t => !t.IsBreakEntry &&
                                t.ClockIn >= dayStart &&
                                t.ClockIn <  dayEnd)
                    .ToListAsync();

                var prevDayCrossovers = await _context.TimeEntries
                    .Where(t => !t.IsBreakEntry  &&
                                !t.IsManual      &&
                                !t.IsHourEntry   &&
                                t.ClockIn >= farLookback &&
                                t.ClockIn <  dayStart    &&
                                t.ClockOut != null       &&
                                t.ClockOut >= dayStart)
                    .ToListAsync();

                string FormatSpan(TimeSpan ts) =>
                    ts <= TimeSpan.Zero ? "—"
                    : ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
                    : $"{ts.Minutes}m";

                // ── Pre-load full-week entries for Weekly OT calculation ──────────
                // Find Monday of the week containing parsedDate so we can sum prior days.
                int parsedDow     = (int)parsedDate.DayOfWeek;
                var weekMonday    = parsedDate.Date.AddDays(-(parsedDow == 0 ? 6 : parsedDow - 1));
                var weekMondayStart = weekMonday + splitOffset;
                // Load all non-break entries from Monday → dayStart (exclusive) for all employees.
                // We use these to compute how many minutes each employee accumulated BEFORE today.
                var weekPriorEntries = await _context.TimeEntries
                    .Where(t => !t.IsBreakEntry
                             && t.ClockIn >= weekMondayStart
                             && t.ClockIn <  dayStart
                             && t.ClockOut != null)
                    .ToListAsync();

                // ── Pre-load public holidays for parsedDate ──────────────────────
                // HolidayCalendar stores holidays as JSON array: [{name, date}, ...]
                // We parse them all in one shot here and check per-employee below.
                var isPublicHoliday = false;
                try
                {
                    var calendars = await _context.HolidayCalendars.ToListAsync();
                    foreach (var cal in calendars)
                    {
                        if (string.IsNullOrWhiteSpace(cal.HolidaysJson) || cal.HolidaysJson == "[]")
                            continue;
                        var doc = System.Text.Json.JsonDocument.Parse(cal.HolidaysJson);
                        foreach (var h in doc.RootElement.EnumerateArray())
                        {
                            if (h.TryGetProperty("date", out var dp) &&
                                DateTime.TryParse(dp.GetString(), out var hd) &&
                                hd.Date == parsedDate.Date)
                            {
                                isPublicHoliday = true;
                                break;
                            }
                        }
                        if (isPublicHoliday) break;
                    }
                }
                catch { /* HolidayCalendars table missing — ignore */ }

                var result = employees.Select(emp =>
                {
                    var empEntries    = overlapping.Where(e => e.EmployeeId == emp.Id).OrderBy(e => e.ClockIn).ToList();
                    var empCrossovers = prevDayCrossovers.Where(e => e.EmployeeId == emp.Id).OrderBy(e => e.ClockIn).ToList();

                    // Always produce a row — rest-day / holiday / time-off rows have no entries
                    var empLeave = approvedLeaves.FirstOrDefault(r => r.EmployeeId == emp.Id);
                    string? timeOffLabel = empLeave?.Policy?.Name ?? (empLeave != null ? "Time Off" : null);

                    // ── Resolve this employee's work schedule ────────────────────────
                    var empSchedule = (!string.IsNullOrWhiteSpace(emp.WorkSchedule)
                                        ? allSchedules.FirstOrDefault(s => s.Name == emp.WorkSchedule)
                                        : null)
                                      ?? defaultSchedule;

                    // ── Rule 1: No schedule in DB → include ALL time (no clipping).
                    // ── Rule 2: Schedule exists  → use exactly what the schedule says.
                    bool isFixed            = empSchedule?.Arrangement == "Fixed";
                    bool includeBeforeStart = empSchedule == null ? true : empSchedule.IncludeBeforeStart;

                    // Rebuild daySlots — only needed when clipping is active
                    // (isFixed=true AND includeBeforeStart=false)
                    var daySlots = new System.Collections.Generic.Dictionary<string, (TimeSpan start, TimeSpan end)>();
                    if (isFixed && !includeBeforeStart)
                    {
                        // Try to parse DaySlotsJson from the schedule
                        if (empSchedule != null && !string.IsNullOrWhiteSpace(empSchedule.DaySlotsJson)
                            && empSchedule.DaySlotsJson != "{}")
                        {
                            try
                            {
                                var slotsDoc = System.Text.Json.JsonDocument.Parse(empSchedule.DaySlotsJson).RootElement;
                                foreach (var prop in slotsDoc.EnumerateObject())
                                {
                                    var sv = prop.Value.TryGetProperty("start", out var s2) ? s2.GetString() ?? "09:00" : "09:00";
                                    var ev = prop.Value.TryGetProperty("end",   out var e2) ? e2.GetString() ?? "17:00" : "17:00";
                                    if (TimeSpan.TryParse(sv, out var ts) && TimeSpan.TryParse(ev, out var te))
                                        daySlots[prop.Name] = (ts, te);
                                }
                            }
                            catch { /* malformed JSON — fall back to seeded defaults */ }
                        }

                        // If DaySlotsJson was empty/missing, seed working days with 09:00–17:00
                        if (daySlots.Count == 0)
                        {
                            var defaultStart = new TimeSpan(9, 0, 0);
                            var defaultEnd   = new TimeSpan(17, 0, 0);
                            var wdList = empSchedule != null && !string.IsNullOrWhiteSpace(empSchedule.WorkingDays)
                                ? empSchedule.WorkingDays.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                                : new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
                            foreach (var wd in wdList)
                                daySlots[wd] = (defaultStart, defaultEnd);
                        }
                    }

                    // AutoDeductions for this employee's schedule
                    var autoDeductions = new System.Collections.Generic.List<(double afterHours, int deductMins)>();
                    if (empSchedule != null && !string.IsNullOrWhiteSpace(empSchedule.AutoDeductionsJson)
                        && empSchedule.AutoDeductionsJson != "[]")
                    {
                        try
                        {
                            var dedArr = System.Text.Json.JsonDocument.Parse(empSchedule.AutoDeductionsJson).RootElement;
                            foreach (var item in dedArr.EnumerateArray())
                            {
                                double ah = item.TryGetProperty("afterHours",    out var ahp) ? ahp.GetDouble() : 0;
                                int    dm = item.TryGetProperty("deductMinutes", out var dmp) ? dmp.GetInt32()  : 0;
                                if (dm > 0) autoDeductions.Add((ah, dm));
                            }
                        }
                        catch { /* ignore malformed */ }
                    }

                    bool      isOngoing   = false;
                    DateTime? firstIn     = null;
                    DateTime? lastOut     = null;
                    int       trackedMins = 0;  // schedule-aware; feeds both Tracked and Regular columns

                    // ── Group B: sessions that started BEFORE today and are still running through it ──
                    //
                    // There are TWO sub-cases (both verified against Jibble's reference behaviour):
                    //
                    // Case 1 — session ENDS today (ClockOut < dayEnd):
                    //   Count time from dayStart → ClockOut.
                    //   firstIn = "—" (no clock-in press today).
                    //   lastOut = ClockOut (the real clock-out time, shown in the column).
                    //   Example: Sat clock-in, Mon clock-out at 9:29 am → Mon shows "— / 9:29 am / 9h 29m".
                    //
                    // Case 2 — session PASSES THROUGH today (ClockOut >= dayEnd):
                    //   Count full 24 hours of today (dayStart → dayEnd).
                    //   firstIn = "—", lastOut = "—" (clocks were not pressed today).
                    //   Example: Sat clock-in, Mon clock-out → Sun shows "— / — / 24h 0m" (Jibble-verified).
                    foreach (var crossEntry in empCrossovers)
                    {
                        // Cap the effective clock-out at dayEnd so we never count beyond today.
                        // Case 1: ClockOut is today  → crossOverClockOut = real ClockOut (< dayEnd).
                        // Case 2: ClockOut is later  → crossOverClockOut = dayEnd (full day counted).
                        bool endsToday        = crossEntry.ClockOut!.Value < dayEnd;
                        var  crossOverClockOut = endsToday ? crossEntry.ClockOut!.Value : dayEnd;

                        // Effective start = dayStart, then clip to scheduledStart if IncludeBeforeStart is off.
                        DateTime crossEffectiveStart = dayStart;
                        if (isFixed && !includeBeforeStart)
                        {
                            var crossDayKey = DayKey(parsedDate);
                            if (daySlots.TryGetValue(crossDayKey, out var crossSlot))
                            {
                                var crossScheduledStart = parsedDate.Date + crossSlot.start;
                                if (crossEffectiveStart < crossScheduledStart)
                                    crossEffectiveStart = crossScheduledStart;
                            }
                        }

                        var crossMins = (int)(crossOverClockOut - crossEffectiveStart).TotalMinutes;
                        if (crossMins <= 0) continue;

                        trackedMins += crossMins;

                        // firstIn always stays null — no clock-in press happened today.
                        // lastOut: only set when the session actually ENDS today (Case 1).
                        // For pass-through days (Case 2), lastOut stays null → shows "—".
                        if (endsToday)
                            if (lastOut == null || crossOverClockOut > lastOut.Value)
                                lastOut = crossOverClockOut;
                    }

                    // ── Group A: entries starting within this split-day ───────────────
                    foreach (var entry in empEntries)
                    {
                        bool open = !entry.ClockOut.HasValue;

                        if (open)
                        {
                            bool isRealLiveSession = !entry.IsManual && !entry.IsHourEntry;
                            // FIX: include open sessions even when querying a past day.
                            // If the employee clocked in on Thu and has not clocked out yet,
                            // querying Thu's sheet (from Fri) must still count Thu's hours.
                            // For today: use current IST time. For a past day: cap at dayEnd
                            // (midnight) so only that day's portion is credited.
                            if (isRealLiveSession)
                            {
                                // Determine the effective clock-in for this live session,
                                // applying IncludeBeforeStart clip if needed.
                                DateTime liveStart = entry.ClockIn < dayStart ? dayStart : entry.ClockIn;

                                if (isFixed && !includeBeforeStart)
                                {
                                    var dayKey = DayKey(parsedDate);
                                    if (daySlots.TryGetValue(dayKey, out var slot))
                                    {
                                        var scheduledStart = parsedDate.Date + slot.start;
                                        if (liveStart < scheduledStart)
                                            liveStart = scheduledStart;
                                    }
                                }

                                // Today: use current IST time. Past day: cap at end of that day.
                                var effectiveEnd = isToday ? istNow : dayEnd;
                                var liveMins = (int)(effectiveEnd - liveStart).TotalMinutes;
                                if (liveMins < 0)     liveMins = 0;
                                if (liveMins > 24*60) liveMins = 24 * 60;
                                trackedMins += liveMins;
                                isOngoing    = isToday; // only mark "ongoing" badge for today

                                if (entry.ClockIn >= dayStart && entry.ClockIn < dayEnd)
                                    if (firstIn == null || entry.ClockIn < firstIn.Value)
                                        firstIn = entry.ClockIn;
                            }
                            continue; // open manual / hour → skip
                        }

                        // ── Closed entry ──────────────────────────────────────────────
                        // Hour entries bypass all schedule rules.
                        // NOTE: Hour entries store ClockIn = midnight (00:00) as a fake timestamp —
                        // they do NOT represent a real clock-in button press.
                        // Therefore we ONLY add their minutes to trackedMins and do NOT
                        // set firstIn/lastOut — those columns must show "-" unless the employee
                        // actually pressed Clock In / Clock Out on that day.
                        if (entry.IsHourEntry)
                        {
                            var hourMins = (int)(entry.ClockOut!.Value - entry.ClockIn).TotalMinutes;
                            if (hourMins > 0) trackedMins += hourMins;
                            continue;
                        }

                        // Real closed session — cap at split boundary.
                        DateTime effectiveClockOut = entry.ClockOut!.Value;
                        if (!entry.IsManual && effectiveClockOut > dayEnd)
                            effectiveClockOut = dayEnd;

                        // Determine effective clock-in (IncludeBeforeStart clip).
                        // IMPORTANT: FirstIn/LastOut are always set from the REAL clock-in/out
                        // (unclipped) so the display columns are always correct regardless of
                        // whether time is included in the payroll calculation.
                        DateTime effectiveClockIn = entry.ClockIn;
                        if (isFixed && !includeBeforeStart && !entry.IsManual)
                        {
                            var dayKey = DayKey(parsedDate);
                            if (daySlots.TryGetValue(dayKey, out var slot))
                            {
                                var scheduledStart = parsedDate.Date + slot.start;
                                if (effectiveClockIn < scheduledStart)
                                    effectiveClockIn = scheduledStart;
                            }
                        }

                        var mins = (int)(effectiveClockOut - effectiveClockIn).TotalMinutes;
                        if (mins > 0) trackedMins += mins;
                        // Note: mins <= 0 means entire session was before scheduled start
                        // (IncludeBeforeStart=false) — the employee still appears in the list
                        // with firstIn/lastOut shown, just 0 payroll minutes for this entry.

                        // FirstIn: real (unclipped) clock-in for display
                        if (entry.ClockIn >= dayStart && entry.ClockIn < dayEnd)
                            if (firstIn == null || entry.ClockIn < firstIn.Value)
                                firstIn = entry.ClockIn;

                        // LastOut: only show if clock-out is within today's window (inclusive of dayEnd).
                        // If ClockOut > dayEnd the session crossed into tomorrow — show "-" for today.
                        // Tomorrow's sheet shows the real clock-out via the Group B (crossover) path.
                        if (entry.ClockOut!.Value > dayStart && entry.ClockOut!.Value <= dayEnd)
                            if (lastOut == null || entry.ClockOut!.Value > lastOut.Value)
                                lastOut = entry.ClockOut!.Value;
                    }

                    // ── Apply auto-deductions ────────────────────────────────────────
                    // Deductions affect the payroll/regular total (same value as tracked here).
                    int payrollMins = trackedMins;
                    foreach (var (afterHours, deductMins) in autoDeductions)
                    {
                        int thresholdMins = (int)(afterHours * 60);
                        if (payrollMins > thresholdMins)
                            payrollMins = Math.Max(0, payrollMins - deductMins);
                    }

                    // ── Overtime breakdown ───────────────────────────────────────────
                    // TWO modes driven by DailyOvertimeIsTime flag:
                    //
                    // HOURS mode (IsTime=false):
                    //   Regular  = payroll time up to DailyOvertimeAfterHours:Mins worked.
                    //   Overtime = payroll time above that threshold.
                    //
                    // TIME-OF-DAY mode (IsTime=true):
                    //   DailyOvertimeAfterHours:Mins is treated as a wall-clock time (e.g.
                    //   Hours=18,Mins=0 → 6:00 PM).  Regular = minutes worked BEFORE that
                    //   time; Overtime = minutes worked AT or AFTER that time.
                    //
                    // Daily double OT applies on top of either mode.
                    // Flags from the org's default schedule drive which columns appear.
                    bool hasDailyOT    = empSchedule?.DailyOvertime       == true;
                    bool hasDailyDblOT = empSchedule?.DailyDoubleOvertime == true;

                    int regularMins   = payrollMins;
                    int overtimeMins  = 0;
                    int dblOTMins     = 0;

                    if (hasDailyOT && empSchedule != null)
                    {
                        if (empSchedule.DailyOvertimeIsTime)
                        {
                            // ── TIME-OF-DAY overtime ─────────────────────────────────
                            // Compute minutes each session contributes at or after the OT
                            // start time on this calendar day.
                            var otStartTime     = new TimeSpan(empSchedule.DailyOvertimeAfterHours,
                                                               empSchedule.DailyOvertimeAfterMins, 0);
                            var otStartDateTime = parsedDate.Date + otStartTime;

                            int rawOvertimeMins = 0;

                            // Group-A entries (started within today's split window)
                            foreach (var entry in empEntries)
                            {
                                if (entry.IsBreakEntry) continue;

                                DateTime effectiveEnd;
                                if (!entry.ClockOut.HasValue)
                                {
                                    // Skip manual or hour entries — they are never live sessions.
                                    if (entry.IsManual || entry.IsHourEntry) continue;
                                    // FIX: include open (no clock-out) sessions even for past days.
                                    // Today → use current IST time. Past day → cap at dayEnd (midnight)
                                    // so only that day's overtime portion is counted.
                                    effectiveEnd = isToday ? istNow : dayEnd;
                                }
                                else
                                {
                                    effectiveEnd = entry.ClockOut.Value;
                                    // Cap at day boundary
                                    if (effectiveEnd > dayEnd) effectiveEnd = dayEnd;
                                }

                                var overlapStart = entry.ClockIn > otStartDateTime ? entry.ClockIn : otStartDateTime;
                                if (effectiveEnd > overlapStart)
                                    rawOvertimeMins += (int)(effectiveEnd - overlapStart).TotalMinutes;
                            }

                            // Group-B crossover entries (started before today, running into today)
                            foreach (var entry in empCrossovers)
                            {
                                if (entry.IsBreakEntry) continue;
                                var crossEnd     = entry.ClockOut!.Value < dayEnd ? entry.ClockOut!.Value : dayEnd;
                                var overlapStart = dayStart > otStartDateTime ? dayStart : otStartDateTime;
                                if (crossEnd > overlapStart)
                                    rawOvertimeMins += (int)(crossEnd - overlapStart).TotalMinutes;
                            }

                            // Clamp so overtime never exceeds total payroll time
                            overtimeMins = Math.Min(rawOvertimeMins, payrollMins);
                            regularMins  = payrollMins - overtimeMins;
                        }
                        else
                        {
                            // ── HOURS-WORKED overtime (original logic) ────────────────
                            int otThreshMins = empSchedule.DailyOvertimeAfterHours * 60
                                             + empSchedule.DailyOvertimeAfterMins;
                            if (payrollMins > otThreshMins)
                            {
                                overtimeMins = payrollMins - otThreshMins;
                                regularMins  = otThreshMins;
                            }
                        }

                        // ── Daily double overtime ─────────────────────────────────────
                        if (hasDailyDblOT)
                        {
                            if (empSchedule.DailyOvertimeIsTime)
                            {
                                // TIME-OF-DAY mode: double OT = minutes worked at or after
                                // the double-OT wall-clock time (e.g. 6:00 PM).
                                var dblStartTime     = new TimeSpan(empSchedule.DailyDoubleOTAfterHours,
                                                                    empSchedule.DailyDoubleOTAfterMins, 0);
                                var dblStartDateTime = parsedDate.Date + dblStartTime;

                                int rawDblOTMins = 0;

                                foreach (var entry in empEntries)
                                {
                                    if (entry.IsBreakEntry) continue;
                                    DateTime effectiveEnd;
                                    if (!entry.ClockOut.HasValue)
                                    {
                                        if (entry.IsManual || entry.IsHourEntry) continue;
                                        effectiveEnd = isToday ? istNow : dayEnd;
                                    }
                                    else
                                    {
                                        effectiveEnd = entry.ClockOut.Value;
                                        if (effectiveEnd > dayEnd) effectiveEnd = dayEnd;
                                    }
                                    var overlapStart = entry.ClockIn > dblStartDateTime ? entry.ClockIn : dblStartDateTime;
                                    if (effectiveEnd > overlapStart)
                                        rawDblOTMins += (int)(effectiveEnd - overlapStart).TotalMinutes;
                                }

                                foreach (var entry in empCrossovers)
                                {
                                    if (entry.IsBreakEntry) continue;
                                    var crossEnd     = entry.ClockOut!.Value < dayEnd ? entry.ClockOut!.Value : dayEnd;
                                    var overlapStart = dayStart > dblStartDateTime ? dayStart : dblStartDateTime;
                                    if (crossEnd > overlapStart)
                                        rawDblOTMins += (int)(crossEnd - overlapStart).TotalMinutes;
                                }

                                // Clamp double OT to total overtime, then subtract from OT
                                dblOTMins    = Math.Min(rawDblOTMins, overtimeMins);
                                overtimeMins = overtimeMins - dblOTMins;
                            }
                            else
                            {
                                // HOURS-WORKED mode: double OT kicks in when total worked
                                // exceeds the double-OT hours threshold.
                                if (overtimeMins > 0)
                                {
                                    int otThreshMins  = empSchedule.DailyOvertimeAfterHours * 60
                                                      + empSchedule.DailyOvertimeAfterMins;
                                    int dblThreshMins = empSchedule.DailyDoubleOTAfterHours * 60
                                                      + empSchedule.DailyDoubleOTAfterMins;
                                    int otBeforeDbl   = Math.Max(0, dblThreshMins - otThreshMins);
                                    if (overtimeMins > otBeforeDbl)
                                    {
                                        dblOTMins    = overtimeMins - otBeforeDbl;
                                        overtimeMins = otBeforeDbl;
                                    }
                                }
                            }
                        }
                    }

                    var tracked   = TimeSpan.FromMinutes(trackedMins);
                    var payroll   = TimeSpan.FromMinutes(payrollMins);
                    var regular   = TimeSpan.FromMinutes(regularMins);
                    var overtime  = TimeSpan.FromMinutes(overtimeMins);
                    var dblOT     = TimeSpan.FromMinutes(dblOTMins);

                    // ── Resolve rest-day for this employee's schedule ────────────
                    bool isRestDay = false;
                    if (empSchedule != null && !string.IsNullOrWhiteSpace(empSchedule.WorkingDays))
                        isRestDay = !empSchedule.WorkingDays
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Contains(todayDayKey);

                    bool hasEntries = empEntries.Any() || empCrossovers.Any();

                    // ── OT flags from schedule ───────────────────────────────────
                    bool hasWeeklyOT    = empSchedule?.WeeklyOvertime         == true;
                    bool hasRestDayOT   = empSchedule?.RestDayOvertime        == true;
                    bool hasPublicHolOT = empSchedule?.PublicHolidayOvertime  == true;

                    int weeklyOTMins   = 0;
                    int restDayOTMins  = 0;
                    int pubHolOTMins   = 0;

                    // ── Public Holiday Overtime ──────────────────────────────────
                    // If today is a public holiday and the flag is on:
                    //   - ALL payroll minutes → public holiday OT
                    //   - Regular hours = 0 (public holiday OT replaces regular)
                    if (hasPublicHolOT && isPublicHoliday && payrollMins > 0)
                    {
                        pubHolOTMins = payrollMins;
                        regularMins  = 0;
                        regular      = TimeSpan.Zero;
                    }

                    // ── Rest Day Overtime ────────────────────────────────────────
                    // If today is a rest day (e.g. Sunday) and the flag is on:
                    //   - ALL payroll minutes → rest day OT
                    //   - Regular hours = 0
                    // Public holiday OT takes precedence if both flags are active.
                    else if (hasRestDayOT && isRestDay && payrollMins > 0)
                    {
                        restDayOTMins = payrollMins;
                        regularMins   = 0;
                        regular       = TimeSpan.Zero;
                    }

                    // ── Weekly Overtime ──────────────────────────────────────────
                    // Only apply on normal working days (not rest day, not holiday).
                    // Algorithm:
                    //   1. Sum payroll minutes from Monday (inclusive) to yesterday (inclusive)
                    //      using weekPriorEntries pre-loaded above — skipping rest days.
                    //   2. Add today's payrollMins to get total weekly minutes.
                    //   3. Weekly OT for today = portion of today's work that pushes
                    //      the weekly total above the configured threshold.
                    //      weeklyOTToday = max(0, totalWeekMins-threshold) - max(0, priorMins-threshold)
                    //   4. Subtract weekly OT from regular hours.
                    else if (hasWeeklyOT && empSchedule != null && !isRestDay && !isPublicHoliday)
                    {
                        int weeklyThreshMins = empSchedule.WeeklyOvertimeAfterHours * 60
                                             + empSchedule.WeeklyOvertimeAfterMins;

                        // Compute prior days' payroll minutes (Mon → yesterday) from pre-loaded data.
                        int priorDaysMins = 0;
                        for (var d = weekMonday; d < parsedDate.Date; d = d.AddDays(1))
                        {
                            // Skip rest days — they don't count toward weekly OT threshold
                            string priorDayKey = DayKey(d);
                            bool priorIsRest = empSchedule != null
                                && !string.IsNullOrWhiteSpace(empSchedule.WorkingDays)
                                && !empSchedule.WorkingDays
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                    .Contains(priorDayKey);
                            if (priorIsRest) continue;

                            var dStart = d + splitOffset;
                            var dEnd   = dStart.AddDays(1);

                            var priorEnts = weekPriorEntries
                                .Where(e => e.EmployeeId == emp.Id
                                         && e.ClockIn   >= dStart
                                         && e.ClockIn   <  dEnd)
                                .ToList();

                            foreach (var pe in priorEnts)
                            {
                                if (pe.IsHourEntry)
                                {
                                    var hm = (int)(pe.ClockOut!.Value - pe.ClockIn).TotalMinutes;
                                    if (hm > 0) priorDaysMins += hm;
                                    continue;
                                }
                                var effOut = pe.ClockOut!.Value > dEnd ? dEnd : pe.ClockOut!.Value;
                                var effIn  = pe.ClockIn;
                                if (!pe.IsManual && isFixed && !includeBeforeStart)
                                {
                                    if (daySlots.TryGetValue(priorDayKey, out var ps2))
                                    {
                                        var schedStart2 = d + ps2.start;
                                        if (effIn < schedStart2) effIn = schedStart2;
                                    }
                                }
                                var mins2 = (int)(effOut - effIn).TotalMinutes;
                                if (mins2 > 0) priorDaysMins += mins2;
                            }
                        }

                        // Apply auto-deductions to prior days (same rule as today)
                        foreach (var (afterHours, deductMins) in autoDeductions)
                        {
                            int thr2 = (int)(afterHours * 60);
                            if (priorDaysMins > thr2)
                                priorDaysMins = Math.Max(0, priorDaysMins - deductMins);
                        }

                        int totalWeekMins   = priorDaysMins + payrollMins;
                        int otFromTotal     = Math.Max(0, totalWeekMins   - weeklyThreshMins);
                        int otFromPrior     = Math.Max(0, priorDaysMins   - weeklyThreshMins);
                        weeklyOTMins        = Math.Max(0, otFromTotal - otFromPrior);
                        weeklyOTMins        = Math.Min(weeklyOTMins, payrollMins); // clamp to today's payroll

                        // Reduce regular hours by the weekly OT portion
                        if (weeklyOTMins > 0)
                        {
                            regularMins = Math.Max(0, regularMins - weeklyOTMins);
                            regular     = TimeSpan.FromMinutes(regularMins);
                        }
                    }

                    return new
                    {
                        employeeId             = emp.Id,
                        fullName               = emp.FullName,
                        firstIn,
                        lastOut,
                        isOngoing,
                        isRestDay,
                        isPublicHoliday,
                        timeOffLabel,
                        hasEntries,
                        trackedHours           = FormatSpan(tracked),
                        regularHours           = FormatSpan(regular),
                        overtimeHours          = hasDailyOT    ? FormatSpan(overtime)                                   : (string?)null,
                        dailyDoubleOTHours     = hasDailyDblOT ? FormatSpan(dblOT)                                      : (string?)null,
                        weeklyOvertimeHours    = hasWeeklyOT   ? FormatSpan(TimeSpan.FromMinutes(weeklyOTMins))         : (string?)null,
                        restDayOvertimeHours   = hasRestDayOT  ? FormatSpan(TimeSpan.FromMinutes(restDayOTMins))        : (string?)null,
                        publicHolOvertimeHours = hasPublicHolOT? FormatSpan(TimeSpan.FromMinutes(pubHolOTMins))         : (string?)null,
                        hasDailyOT,
                        hasDailyDblOT,
                        hasWeeklyOT,
                        hasRestDayOT,
                        hasPublicHolOT,
                    };
                })
                .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDailyTimesheet failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("range")]
        public async Task<IActionResult> GetRangeTimesheet([FromQuery] string from, [FromQuery] string to,
                                                            [FromQuery] int? organizationId = null)
        {
            try
            {
                if (!DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
                    return BadRequest(new { error = "Invalid date range." });

                // DB stores ClockIn as local (IST) time — use direct date window, no UTC conversion.
                var rangeStart = fromDate.Date;
                var rangeEnd   = toDate.Date.AddDays(1);

                var employees = await _context.Employees
                    .Where(e => e.IsActive)
                    .OrderBy(e => e.FullName)
                    .Select(e => new { e.Id, e.FullName, e.WorkSchedule })
                    .ToListAsync();

                // ── Org-scoped visibility ─────────────────────────────────────────
                if (organizationId.HasValue && organizationId.Value > 0)
                {
                    var orgMemberIds = await _context.OrganizationMembers
                        .Where(m => m.OrganizationId == organizationId.Value && m.IsActive)
                        .Select(m => m.EmployeeId)
                        .ToListAsync();

                    employees = employees.Where(e => orgMemberIds.Contains(e.Id)).ToList();
                }

                // Load entries directly using local date range
                var rangeEntries = await _context.TimeEntries
                    .Where(t => t.ClockIn >= rangeStart && t.ClockIn < rangeEnd)
                    .ToListAsync();

                // ── Load all work schedules (same as daily endpoint) ─────────────────
                List<WorkSchedule> allSchedules = new();
                WorkSchedule? defaultSchedule   = null;
                try
                {
                    allSchedules    = await _context.WorkSchedules.ToListAsync();
                    defaultSchedule = allSchedules.FirstOrDefault(s => s.IsDefault)
                                   ?? allSchedules.FirstOrDefault();
                }
                catch { /* table may not exist yet — treat as no schedule */ }

                string FormatSpan(TimeSpan ts) =>
                    ts == TimeSpan.Zero ? null!
                    : ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
                    : $"{ts.Minutes}m";

                var result = employees.Select(emp =>
                {
                    var empEntries = rangeEntries.Where(e => e.EmployeeId == emp.Id).ToList();

                    // ── Resolve this employee's work schedule ────────────────────────
                    var empSchedule = (!string.IsNullOrWhiteSpace(emp.WorkSchedule)
                                        ? allSchedules.FirstOrDefault(s => s.Name == emp.WorkSchedule)
                                        : null)
                                      ?? defaultSchedule;

                    // ── Rule 1: No schedule in DB → include ALL time (no clipping).
                    // ── Rule 2: Schedule exists  → use exactly what the schedule says.
                    bool isFixed            = empSchedule?.Arrangement == "Fixed";
                    bool includeBeforeStart = empSchedule == null ? true : empSchedule.IncludeBeforeStart;

                    // Build daySlots — only needed when clipping is active
                    var daySlots = new Dictionary<string, (TimeSpan start, TimeSpan end)>();
                    if (isFixed && !includeBeforeStart)
                    {
                        if (empSchedule != null && !string.IsNullOrWhiteSpace(empSchedule.DaySlotsJson)
                            && empSchedule.DaySlotsJson != "{}")
                        {
                            try
                            {
                                var slotsDoc = System.Text.Json.JsonDocument.Parse(empSchedule.DaySlotsJson).RootElement;
                                foreach (var prop in slotsDoc.EnumerateObject())
                                {
                                    var sv = prop.Value.TryGetProperty("start", out var s2) ? s2.GetString() ?? "09:00" : "09:00";
                                    var ev = prop.Value.TryGetProperty("end",   out var e2) ? e2.GetString() ?? "17:00" : "17:00";
                                    if (TimeSpan.TryParse(sv, out var ts) && TimeSpan.TryParse(ev, out var te))
                                        daySlots[prop.Name] = (ts, te);
                                }
                            }
                            catch { /* malformed JSON — fall back to seeded defaults */ }
                        }

                        if (daySlots.Count == 0)
                        {
                            var defaultStart = new TimeSpan(9, 0, 0);
                            var defaultEnd   = new TimeSpan(17, 0, 0);
                            var wdList = empSchedule != null && !string.IsNullOrWhiteSpace(empSchedule.WorkingDays)
                                ? empSchedule.WorkingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                : new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
                            foreach (var wd in wdList)
                                daySlots[wd] = (defaultStart, defaultEnd);
                        }
                    }

                    // Auto-deductions for this employee's schedule
                    var autoDeductions = new List<(double afterHours, int deductMins)>();
                    if (empSchedule != null && !string.IsNullOrWhiteSpace(empSchedule.AutoDeductionsJson)
                        && empSchedule.AutoDeductionsJson != "[]")
                    {
                        try
                        {
                            var dedArr = System.Text.Json.JsonDocument.Parse(empSchedule.AutoDeductionsJson).RootElement;
                            foreach (var item in dedArr.EnumerateArray())
                            {
                                double ah  = item.TryGetProperty("afterHours",  out var ahe)  ? ahe.GetDouble()  : 0;
                                int    dm  = item.TryGetProperty("deductMinutes",  out var dme)  ? dme.GetInt32()   : 0;
                                if (ah > 0 && dm > 0) autoDeductions.Add((ah, dm));
                            }
                        }
                        catch { /* ignore malformed JSON */ }
                    }

                    // Build per-day data, bucketing by local date (DB stores local IST time)
                    var today = DateTime.Today;
                    var days = empEntries
                        .GroupBy(e => DateOnly.FromDateTime(e.ClockIn))
                        .Select(g =>
                        {
                            var dayEnts   = g.OrderBy(e => e.ClockIn).ToList();
                            var parsedDate = g.Key.ToDateTime(TimeOnly.MinValue); // midnight of this day
                            var dayEnd     = parsedDate.AddDays(1);              // midnight of next day (cap point)
                            var isThisDay  = parsedDate.Date == today;

                            // Determine the 3-letter day-of-week key matching DaySlotsJson keys
                            var dowKey = parsedDate.DayOfWeek switch
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

                            // firstIn/lastOut must only reflect REAL clock-in/out button presses.
                            var realEnts = dayEnts.Where(e => !e.IsHourEntry).ToList();
                            var firstIn  = realEnts.Any() ? (DateTime?)realEnts.First().ClockIn : null;
                            var lastOut  = realEnts.LastOrDefault(e => e.ClockOut != null) is { ClockOut: not null } lo
                                           ? lo.ClockOut!.Value : (DateTime?)null;

                            var openEntry = dayEnts.FirstOrDefault(e => e.ClockOut == null && !e.IsManual && !e.IsHourEntry);
                            var isOpen    = openEntry != null && isThisDay;

                            int trackedMins = 0;

                            // ── Closed (clock-out present) sessions ─────────────────────────
                            foreach (var entry in dayEnts.Where(e => e.ClockOut != null && !e.IsBreakEntry))
                            {
                                if (entry.IsHourEntry)
                                {
                                    // Hour entries bypass schedule rules
                                    var hourMins = (int)(entry.ClockOut!.Value - entry.ClockIn).TotalMinutes;
                                    if (hourMins > 0) trackedMins += hourMins;
                                    continue;
                                }

                                var effOut = (!entry.IsManual && entry.ClockOut!.Value > dayEnd)
                                    ? dayEnd : entry.ClockOut!.Value;

                                var effectiveClockIn = entry.ClockIn;

                                // Apply IncludeBeforeStart clipping
                                if (isFixed && !includeBeforeStart && !entry.IsManual)
                                {
                                    if (daySlots.TryGetValue(dowKey, out var slot))
                                    {
                                        var scheduledStart = parsedDate.Date + slot.start;
                                        if (effectiveClockIn < scheduledStart)
                                            effectiveClockIn = scheduledStart;
                                    }
                                }

                                var mins = (int)(effOut - effectiveClockIn).TotalMinutes;
                                if (mins > 0) trackedMins += mins;
                            }

                            // ── Live / ongoing session ──────────────────────────────────────
                            if (isOpen && openEntry != null)
                            {
                                var liveStart = openEntry.ClockIn < parsedDate ? parsedDate : openEntry.ClockIn;

                                // Apply IncludeBeforeStart clipping to the live session too
                                if (isFixed && !includeBeforeStart && !openEntry.IsManual)
                                {
                                    if (daySlots.TryGetValue(dowKey, out var slot))
                                    {
                                        var scheduledStart = parsedDate.Date + slot.start;
                                        if (liveStart < scheduledStart)
                                            liveStart = scheduledStart;
                                    }
                                }

                                var liveElapsed = DateTime.UtcNow.Add(_istOffset) - liveStart;
                                if (liveElapsed > TimeSpan.Zero)
                                {
                                    if (liveElapsed.TotalMinutes > 24 * 60) liveElapsed = TimeSpan.FromHours(24);
                                    trackedMins += (int)liveElapsed.TotalMinutes;
                                }
                            }

                            // Raw tracked (before deductions) — used for the Tracked column
                            var trackedSpan = TimeSpan.FromMinutes(trackedMins);

                            return new
                            {
                                date         = g.Key.ToString("yyyy-MM-dd"),
                                firstIn      = (DateTime?)firstIn,
                                lastOut      = lastOut,
                                isOngoing    = isOpen,
                                trackedMins  = trackedMins,
                                trackedHours = FormatSpan(trackedSpan)
                            };
                        }).ToList();

                    // ── Totals ──────────────────────────────────────────────────────────
                    var totalTrackedMins = days.Sum(d => d.trackedMins);
                    var totalTrackedSpan = TimeSpan.FromMinutes(totalTrackedMins);

                    // Apply auto-deductions to arrive at regularMins (payroll total)
                    int regularMins = totalTrackedMins;
                    foreach (var (afterHours, deductMins) in autoDeductions)
                    {
                        int thresholdMins = (int)(afterHours * 60);
                        if (regularMins > thresholdMins)
                            regularMins = Math.Max(0, regularMins - deductMins);
                    }
                    var regularSpan = TimeSpan.FromMinutes(regularMins);

                    return new
                    {
                        employeeId   = emp.Id,
                        fullName     = emp.FullName,
                        days         = days,
                        totalMins    = totalTrackedMins,
                        totalHours   = FormatSpan(totalTrackedSpan),
                        regularMins  = regularMins,
                        regularHours = FormatSpan(regularSpan)
                    };
                })
                .Where(r => r != null)
                .ToList();

                return Ok(result);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetRangeTimesheet failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpGet("groupmembers/{employeeId}")]
        public async Task<IActionResult> GetGroupMembers(int employeeId)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(employeeId);
                if (employee == null || employee.GroupId == null) return Ok(new List<object>());
                var members = await _context.Employees
                    .Where(e => e.GroupId == employee.GroupId && e.Id != employeeId && e.IsActive)
                    .Select(e => new { id = e.Id, fullName = e.FullName, email = e.Email, isClockedIn = _context.TimeEntries.Any(t => t.EmployeeId == e.Id && t.ClockOut == null) })
                    .ToListAsync();
                return Ok(members);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetGroupMembers failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("clockin")]
        public async Task<IActionResult> ClockIn([FromBody] ClockInRequest req)
        {
            try
            {
                // Only count a LIVE (non-manual, non-hour) open session as "already clocked in".
                // Scope the check to the specific organization so clocking into Org A does NOT
                // block or appear as clocked-in in Org B.
                var existingQuery = _context.TimeEntries.Where(t =>
                    t.EmployeeId == req.EmployeeId &&
                    t.ClockOut   == null           &&
                    !t.IsManual                    &&
                    !t.IsHourEntry);

                if (req.OrganizationId.HasValue && req.OrganizationId.Value > 0)
                    existingQuery = existingQuery.Where(t => t.OrganizationId == req.OrganizationId.Value);

                var existing = await existingQuery.AnyAsync();
                if (existing) return BadRequest(new { error = "Already clocked in." });

                // ── Selfie / face-verification policy check ───────────────────
                // Resolve the active work schedule to read the verification policy.
                // If RequireSelfie is on and no selfie was provided, reject the request.
                // If RequireFaceVerification is on the client-side face-matching kiosk
                // flow (ClockInWithFace) is the correct endpoint; this endpoint still
                // accepts a selfie snapshot for audit purposes even without face-rec.
                string? clockInSelfieUrl = null;
                // Policy is enforced by the frontend (BasePage.razor opens the selfie/face modal
                // before allowing clock-in). The backend just stores the selfie if one is provided.
                // We no longer reject here — removing the backend gate prevents the
                // "A selfie photo is required" error when the frontend hasn't yet sent one.

                // ── Save selfie photo if provided ─────────────────────────────
                if (!string.IsNullOrEmpty(req.SelfieBase64))
                {
                    try
                    {
                        var base64 = req.SelfieBase64.Contains(',')
                            ? req.SelfieBase64.Split(',')[1]
                            : req.SelfieBase64;
                        var bytes    = Convert.FromBase64String(base64);
                        var fileName = $"selfie_in_{req.EmployeeId}_{DateTime.Now:yyyyMMddHHmmss}.jpg";
                        var folder   = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        Directory.CreateDirectory(folder);
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
                        clockInSelfieUrl = $"/uploads/{fileName}";
                    }
                    catch (Exception photoEx)
                    {
                        _logger.LogWarning(photoEx, "Failed to save clock-in selfie for employee {Id}", req.EmployeeId);
                        // Non-fatal — continue with clock-in
                    }
                }

                // DB stores local time — use DateTime.Now so ClockIn is IST local.
                // OrganizationId scopes the entry to the org the user clocked in from,
                // so each org maintains its own independent clock-in/out records.
                var entry = new TimeEntry
                {
                    EmployeeId       = req.EmployeeId,
                    OrganizationId   = (req.OrganizationId.HasValue && req.OrganizationId.Value > 0)
                                        ? req.OrganizationId : null,
                    ClockIn          = DateTime.Now,
                    ClockInSelfieUrl = clockInSelfieUrl,
                };
                _context.TimeEntries.Add(entry);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception saveEx) when (saveEx.ToString().Contains("ClockInSelfieUrl") ||
                                               saveEx.ToString().Contains("OrganizationId")   ||
                                               saveEx.ToString().Contains("Invalid column name"))
                {
                    // A migration has not been run yet — retry with only safe columns.
                    _logger.LogWarning(saveEx, "Column missing — ensure migrations are applied. Retrying clock-in with minimal fields.");
                    _context.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    var entrySafe = new TimeEntry
                    {
                        EmployeeId = req.EmployeeId,
                        ClockIn    = entry.ClockIn,
                    };
                    _context.TimeEntries.Add(entrySafe);
                    await _context.SaveChangesAsync();
                    entry = entrySafe;
                    clockInSelfieUrl = null;
                }

                // Fire-and-forget notifications — use a fresh scope so the disposed
                // request-scoped DbContext is never accessed from the background thread.
                var capturedClockInEmployeeId = req.EmployeeId;
                var capturedClockIn = entry.ClockIn;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var emp = await db.Employees.FindAsync(capturedClockInEmployeeId);
                        var settings = await db.EmployeeNotificationSettings
                            .FirstOrDefaultAsync(s => s.EmployeeId == capturedClockInEmployeeId);
                        if (emp == null) return;
                        var localTime = capturedClockIn.ToLocalTime();

                        // Only fire if the employee has Clock-In reminder enabled
                        if (settings?.NotifClockIn == true)
                        {
                            if (settings.RemindersChannelEmail == true)
                                await _emailService.SendClockInEmail(emp.Email, emp.FullName, localTime);

                            if (settings.RemindersChannelPush == true && !string.IsNullOrEmpty(emp.FcmToken))
                                await _firebaseService.SendPushAsync(emp.FcmToken,
                                    "Clocked In ✅",
                                    $"You clocked in at {localTime:hh:mm tt}. Have a great shift!");
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Clock-in notification failed for emp {Id}", capturedClockInEmployeeId); }
                });

                return Ok(new { entryId = entry.Id, clockIn = entry.ClockIn, clockInSelfieUrl = entry.ClockInSelfieUrl });
            }
            catch (Exception ex) { _logger.LogError(ex, "ClockIn failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("clockout")]
        public async Task<IActionResult> ClockOut([FromBody] ClockOutRequest req)
        {
            try
            {
                // Scope the open-entry lookup to the specific org so that clocking out
                // of Org A only closes an Org A entry, not one from Org B.
                var entryQuery = _context.TimeEntries
                    .Where(t => t.EmployeeId == req.EmployeeId && t.ClockOut == null);

                if (req.OrganizationId.HasValue && req.OrganizationId.Value > 0)
                    entryQuery = entryQuery.Where(t =>
                        t.OrganizationId == req.OrganizationId.Value ||
                        t.OrganizationId == null);   // include legacy rows with no org

                var entry = await entryQuery
                    .OrderByDescending(t => t.ClockIn)
                    .FirstOrDefaultAsync();

                if (entry == null) return BadRequest(new { error = "No active clock-in found." });

                // ── Selfie / face-verification policy check ───────────────────
                string? clockOutSelfieUrl = null;
                // Policy is enforced by the frontend — backend just stores the selfie if provided.

                // ── Save selfie photo if provided ─────────────────────────────
                if (!string.IsNullOrEmpty(req.SelfieBase64))
                {
                    try
                    {
                        var base64 = req.SelfieBase64.Contains(',')
                            ? req.SelfieBase64.Split(',')[1]
                            : req.SelfieBase64;
                        var bytes    = Convert.FromBase64String(base64);
                        var fileName = $"selfie_out_{req.EmployeeId}_{DateTime.Now:yyyyMMddHHmmss}.jpg";
                        var folder   = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        Directory.CreateDirectory(folder);
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
                        clockOutSelfieUrl = $"/uploads/{fileName}";
                    }
                    catch (Exception photoEx)
                    {
                        _logger.LogWarning(photoEx, "Failed to save clock-out selfie for employee {Id}", req.EmployeeId);
                        // Non-fatal — continue with clock-out
                    }
                }

                // DB stores local time — use DateTime.Now
                entry.ClockOut          = DateTime.Now;
                entry.ClockOutSelfieUrl = clockOutSelfieUrl;
                var d = entry.ClockOut.Value - entry.ClockIn;
                var h = (int)d.TotalHours; var m = d.Minutes;
                entry.WorkedHours = h > 0 ? $"{h}h {m}m" : $"{m}m";
                await _context.SaveChangesAsync();

                // Recalculate all entries for this day so summary hours are immediately correct.
                await RecalcDayEntries(entry.EmployeeId, entry.ClockIn.Date, entry.OrganizationId);

                // Fire-and-forget notifications
                var capturedEntry = entry;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var emp = await db.Employees.FindAsync(req.EmployeeId);
                        var settings = await db.EmployeeNotificationSettings
                            .FirstOrDefaultAsync(s => s.EmployeeId == req.EmployeeId);
                        if (emp == null) return;
                        var localIn  = capturedEntry.ClockIn.ToLocalTime();
                        var localOut = capturedEntry.ClockOut!.Value.ToLocalTime();

                        // Break clock-out uses NotifEndBreak; regular clock-out uses NotifClockOut
                        bool isBreak   = capturedEntry.IsBreakEntry;
                        bool shouldNotify = isBreak
                            ? settings?.NotifEndBreak  == true
                            : settings?.NotifClockOut  == true;

                        if (shouldNotify)
                        {
                            if (settings!.RemindersChannelEmail == true)
                                await _emailService.SendClockOutEmail(emp.Email, emp.FullName, localIn, localOut, capturedEntry.WorkedHours ?? "");

                            if (settings.RemindersChannelPush == true && !string.IsNullOrEmpty(emp.FcmToken))
                                await _firebaseService.SendPushAsync(emp.FcmToken,
                                    isBreak ? "Break Ended 🔔" : "Clocked Out 🟢",
                                    isBreak
                                        ? $"Your break ended at {localOut:hh:mm tt}."
                                        : $"You clocked out at {localOut:hh:mm tt}. Total: {capturedEntry.WorkedHours}");
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Clock-out notification failed for emp {Id}", req.EmployeeId); }
                });

                return Ok(new { entryId = entry.Id, clockIn = entry.ClockIn, clockOut = entry.ClockOut, workedHours = entry.WorkedHours, clockInSelfieUrl = entry.ClockInSelfieUrl, clockOutSelfieUrl = entry.ClockOutSelfieUrl });
            }
            catch (Exception ex) { _logger.LogError(ex, "ClockOut failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // PUT api/Attendance/entry/{entryId}
        [HttpPut("entry/{entryId}")]
        public async Task<IActionResult> UpdateEntry(int entryId, [FromBody] UpdateEntryRequest req)
        {
            try
            {
                // Use AsNoTracking to fetch snapshot, then update via fresh tracked entity
                var entry = await _context.TimeEntries.FindAsync(entryId);
                if (entry == null) return NotFound(new { error = "Entry not found." });

                // Snapshot old values BEFORE changing
                var oldClockIn = entry.ClockIn;
                var oldClockOut = entry.ClockOut;

                // Parse new clock-in — try multiple strategies to handle any format
                // sent by different browser/OS/locale combinations.
                if (string.IsNullOrWhiteSpace(req.NewClockIn))
                    return BadRequest(new { error = "ClockIn value is empty." });

                DateTime newClockIn;
                bool parsed =
                    // Strategy 1: strict ISO-8601 with Z (what we send)
                    DateTime.TryParse(req.NewClockIn, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out newClockIn)
                    ||
                    // Strategy 2: RoundtripKind fallback
                    DateTime.TryParse(req.NewClockIn, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out newClockIn)
                    ||
                    // Strategy 3: any format, invariant culture
                    DateTime.TryParse(req.NewClockIn, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out newClockIn);

                if (!parsed)
                    return BadRequest(new { error = $"Invalid ClockIn format. Received: '{req.NewClockIn}'" });

                // Store as Unspecified to match how ClockIn is saved by DateTime.Now (local, unspecified kind).
                // Using DateTimeKind.Utc here would corrupt the date-window queries that compare
                // against dayStart/dayEnd computed from local DateTime.Today.
                entry.ClockIn = DateTime.SpecifyKind(newClockIn, DateTimeKind.Unspecified);

                // Parse new clock-out (if provided)
                if (!string.IsNullOrWhiteSpace(req.NewClockOut))
                {
                    // Parse new clock-out — same multi-strategy parsing as clock-in
                    DateTime newClockOut;
                    bool parsedOut =
                        DateTime.TryParse(req.NewClockOut, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out newClockOut)
                        ||
                        DateTime.TryParse(req.NewClockOut, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out newClockOut)
                        ||
                        DateTime.TryParse(req.NewClockOut, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out newClockOut);

                    if (!parsedOut)
                        return BadRequest(new { error = $"Invalid ClockOut format. Received: '{req.NewClockOut}'" });

                    entry.ClockOut = DateTime.SpecifyKind(newClockOut, DateTimeKind.Unspecified);

                    var dur = entry.ClockOut.Value - entry.ClockIn;
                    entry.WorkedHours = $"{(int)dur.TotalHours}h {dur.Minutes}m";
                }
                else
                {
                    entry.ClockOut = null;
                    entry.WorkedHours = null;
                }

                // Write change-log — only store the field that actually changed.
                // ChangedField tells us which field the user edited ("in" or "out").
                // This keeps change history rows clean: one row shows only the changed time.
                bool clockInChanged = entry.ClockIn != oldClockIn;
                bool clockOutChanged = entry.ClockOut != oldClockOut;

                // Determine which field changed based on ChangedField hint + actual diff
                // If user edited clock-in: log OldClockIn/NewClockIn, leave out fields null
                // If user edited clock-out: log OldClockOut/NewClockOut, leave in fields null
                DateTime? logOldClockIn = null;
                DateTime? logNewClockIn = null;
                DateTime? logOldClockOut = null;
                DateTime? logNewClockOut = null;

                if (req.ChangedField == "out" && (clockOutChanged || !clockInChanged))
                {
                    // User edited clock-out
                    logOldClockOut = oldClockOut;
                    logNewClockOut = entry.ClockOut;
                    // Also capture clock-in as context (OldClockIn) so the date filter works
                    logOldClockIn = oldClockIn;
                    logNewClockIn = null;   // not changed — don't show in history
                }
                else
                {
                    // User edited clock-in (default)
                    logOldClockIn = oldClockIn;
                    logNewClockIn = entry.ClockIn;
                    // Don't log clock-out — it wasn't changed
                    logOldClockOut = null;
                    logNewClockOut = null;
                }

                // Save the TimeEntry update FIRST, then detach it so EF relationship
                // fixup doesn't try to wire the already-tracked entry to the new log row.
                await _context.SaveChangesAsync();
                _context.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;

                // Insert changelog using a new detached DbContext scope via ADO.NET directly
                // to avoid EF type-mapping issues with nullable DateTime parameters.
                var conn = _context.Database.GetDbConnection();
                bool wasOpen = conn.State == System.Data.ConnectionState.Open;
                if (!wasOpen) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO TimeEntryChangeLogs
                            (TimeEntryId, EmployeeId, Action,
                             OldClockIn, OldClockOut, NewClockIn, NewClockOut,
                             ReasonForChange, ChangedAt)
                        VALUES
                            (@TimeEntryId, @EmployeeId, @Action,
                             @OldClockIn, @OldClockOut, @NewClockIn, @NewClockOut,
                             @ReasonForChange, @ChangedAt)";

                    void AddParam(string name, object? value, System.Data.DbType dbType)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = name;
                        p.DbType = dbType;
                        p.Value = value ?? (object)System.DBNull.Value;
                        cmd.Parameters.Add(p);
                    }

                    AddParam("@TimeEntryId", entryId, System.Data.DbType.Int32);
                    // EmployeeId stores the "changed by" employee (column was renamed from ChangedByEmpId)
                    AddParam("@EmployeeId", req.ChangedByEmpId, System.Data.DbType.Int32);
                    AddParam("@Action", "Edited", System.Data.DbType.String);
                    AddParam("@OldClockIn", logOldClockIn, System.Data.DbType.DateTime2);
                    AddParam("@OldClockOut", logOldClockOut, System.Data.DbType.DateTime2);
                    AddParam("@NewClockIn", logNewClockIn, System.Data.DbType.DateTime2);
                    AddParam("@NewClockOut", logNewClockOut, System.Data.DbType.DateTime2);
                    AddParam("@ReasonForChange", req.ReasonForChange, System.Data.DbType.String);
                    AddParam("@ChangedAt", DateTime.UtcNow, System.Data.DbType.DateTime2);

                    await cmd.ExecuteNonQueryAsync();
                }
                finally
                {
                    if (!wasOpen) await conn.CloseAsync();
                }

                // Recalculate WorkedHours for all entries on this day so tracked hours
                // reflect the edit immediately (e.g. moving clock-in from 5:30 PM → 9:30 AM).
                await RecalcDayEntries(entry.EmployeeId, entry.ClockIn.Date, entry.OrganizationId);

                return Ok(new
                {
                    id = entry.Id,
                    clockIn = entry.ClockIn,
                    clockOut = entry.ClockOut,
                    workedHours = entry.WorkedHours
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateEntry failed for entry {Id}: {Msg}", entryId, ex.ToString());
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // DELETE api/Attendance/entry/{entryId}?changedByEmpId=X
        [HttpDelete("entry/{entryId}")]
        public async Task<IActionResult> DeleteEntry(int entryId, [FromQuery] int changedByEmpId)
        {
            try
            {
                var entry = await _context.TimeEntries.FindAsync(entryId);
                if (entry == null) return NotFound(new { error = "Entry not found." });

                // Capture info needed for recalc BEFORE removing the entry
                var empId      = entry.EmployeeId;
                var entryDate  = entry.ClockIn.Date;
                var orgId      = entry.OrganizationId;

                // Change logs will cascade-delete (FK with Cascade in migration).
                // But EF may not know about the cascade, so explicitly remove them first.
                var logsToDelete = await _context.TimeEntryChangeLogs
                    .Where(l => l.TimeEntryId == entryId)
                    .ToListAsync();
                _context.TimeEntryChangeLogs.RemoveRange(logsToDelete);

                _context.TimeEntries.Remove(entry);
                await _context.SaveChangesAsync();

                // Recalculate WorkedHours for all remaining entries on this day
                // so tracked hours reflect the deletion immediately.
                await RecalcDayEntries(empId, entryDate, orgId);

                return Ok(new { deleted = true, id = entryId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteEntry failed for entry {Id}", entryId);
                return StatusCode(500, new { error = ex.Message });
            }
        }


        // POST api/Attendance/entry/manual
        // Adds a manual time entry: "in", "out", or "hour"
        [HttpPost("entry/manual")]
        public async Task<IActionResult> AddManualEntry([FromBody] ManualEntryRequest req)
        {
            try
            {
                if (req.EmployeeId <= 0)
                    return BadRequest(new { error = "EmployeeId is required." });

                // DB stores local time — parse directly as local, no UTC conversion needed
                DateTime ParseLocal(string date, string time)
                {
                    return DateTime.Parse($"{date} {time}");
                }

                // Helper: write a changelog row via ADO.NET (same pattern as UpdateEntry).
                async Task WriteLog(int entryId, int empId, string action, DateTime? oldCi, DateTime? newCi,
                                    DateTime? oldCo, DateTime? newCo, string? reason)
                {
                    var conn = _context.Database.GetDbConnection();
                    bool wasOpen = conn.State == System.Data.ConnectionState.Open;
                    if (!wasOpen) await conn.OpenAsync();
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = @"
                            INSERT INTO TimeEntryChangeLogs
                                (TimeEntryId, EmployeeId, Action,
                                 OldClockIn, OldClockOut, NewClockIn, NewClockOut,
                                 ReasonForChange, ChangedAt)
                            VALUES
                                (@TimeEntryId, @EmployeeId, @Action,
                                 @OldClockIn, @OldClockOut, @NewClockIn, @NewClockOut,
                                 @ReasonForChange, @ChangedAt)";

                        void P(string name, object? value, System.Data.DbType dbType)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = name; p.DbType = dbType;
                            p.Value = value ?? (object)System.DBNull.Value;
                            cmd.Parameters.Add(p);
                        }
                        P("@TimeEntryId",     entryId,          System.Data.DbType.Int32);
                        // EmployeeId = who made the change (column was renamed from ChangedByEmpId)
                        P("@EmployeeId",      req.EmployeeId,   System.Data.DbType.Int32);
                        P("@Action",          action,           System.Data.DbType.String);
                        P("@OldClockIn",      oldCi,            System.Data.DbType.DateTime2);
                        P("@OldClockOut",     oldCo,            System.Data.DbType.DateTime2);
                        P("@NewClockIn",      newCi,            System.Data.DbType.DateTime2);
                        P("@NewClockOut",     newCo,            System.Data.DbType.DateTime2);
                        P("@ReasonForChange", reason,           System.Data.DbType.String);
                        P("@ChangedAt",       DateTime.UtcNow,  System.Data.DbType.DateTime2);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    finally { if (!wasOpen) await conn.CloseAsync(); }
                }

                if (req.EntryType == "hour")
                {
                    // Hour entry: store as a single TimeEntry with ClockIn = start of day,
                    // ClockOut = ClockIn + duration, WorkedHours = "Xh Ym", marked manual
                    var dayStart = DateTime.Parse(req.Date + " 00:00:00");

                    // Block future dates
                    if (dayStart.Date > DateTime.Now.Date)
                        return BadRequest(new { error = "Cannot add hour entries for future dates." });
                    int totalMins = (req.HourH ?? 0) * 60 + (req.HourM ?? 0);
                    var workedHoursStr = totalMins >= 60
                        ? $"{totalMins / 60}h {totalMins % 60}m"
                        : $"{totalMins}m";
                    var entry = new TimeEntry
                    {
                        EmployeeId  = req.EmployeeId,
                        ClockIn     = dayStart,
                        ClockOut    = dayStart.AddMinutes(totalMins),
                        WorkedHours = workedHoursStr,
                        IsManual    = true,
                        IsHourEntry = true
                    };
                    _context.TimeEntries.Add(entry);
                    await _context.SaveChangesAsync();
                    // Log: Action="AddedHour", ReasonForChange = worked hours string so UI can display it
                    await WriteLog(entry.Id, entry.EmployeeId, "AddedHour", dayStart, null, null, null, workedHoursStr);
                    await RecalcDayEntries(req.EmployeeId, dayStart.Date, req.OrganizationId);
                    return Ok(new { entryId = entry.Id, clockIn = entry.ClockIn, clockOut = entry.ClockOut, workedHours = entry.WorkedHours, isManual = entry.IsManual, isHourEntry = entry.IsHourEntry });
                }

                if (req.EntryType == "in")
                {
                    if (string.IsNullOrEmpty(req.Time))
                        return BadRequest(new { error = "Time is required for In entry." });

                    var clockIn    = ParseLocal(req.Date, req.Time);
                    var targetDate = DateTime.Parse(req.Date).Date;
                    var todayLocal = DateTime.Now.Date;

                    // Block future times only when the entry date is today
                    if (targetDate == todayLocal && clockIn > DateTime.Now.AddMinutes(2))
                        return BadRequest(new { error = "Clock-in time cannot be in the future." });

                    // ── Admin Override: back-date a live session ────────────────────
                    // When an admin adds a clock-in for today while a live session already
                    // exists, instead of blocking, we:
                    //   1. Find the active live session (ClockOut == null, !IsManual).
                    //   2. If the requested time is EARLIER than the live session's ClockIn,
                    //      update the live session's ClockIn to the earlier time.
                    //   3. Remove any manual clock-in entries between the new time and the
                    //      live session's original start (they are now superseded).
                    //   4. Log the override in change history.
                    //   5. Recalculate the day so tracked hours immediately reflect the change.
                    if (targetDate == todayLocal && req.IsAdminOverride)
                    {
                        var liveSession = await _context.TimeEntries
                            .Where(e => e.EmployeeId == req.EmployeeId &&
                                        e.ClockOut   == null           &&
                                        !e.IsManual                    &&
                                        !e.IsHourEntry                 &&
                                        (req.OrganizationId == null || e.OrganizationId == req.OrganizationId || e.OrganizationId == null))
                            .OrderByDescending(e => e.ClockIn)
                            .FirstOrDefaultAsync();

                        if (liveSession != null)
                        {
                            var originalClockIn = liveSession.ClockIn;

                            if (clockIn < originalClockIn)
                            {
                                // Back-date the live session to the admin-specified time.
                                // IMPORTANT: keep IsManual = false so GetStatus, ComputeTracked,
                                // and the live-session timer all still recognise this as the
                                // active (live) session. Only the ClockIn timestamp changes.
                                liveSession.ClockIn = clockIn;
                                // IsManual stays false — do NOT change it.

                                // Remove any manual clock-in entries between new time and original start
                                // that would create duplicate open sessions.
                                var conflicting = await _context.TimeEntries
                                    .Where(e => e.EmployeeId  == req.EmployeeId  &&
                                                e.ClockIn     >= clockIn          &&
                                                e.ClockIn     <  originalClockIn  &&
                                                e.ClockOut    == null             &&
                                                e.IsManual                        &&
                                                !e.IsHourEntry                    &&
                                                e.Id          != liveSession.Id)
                                    .ToListAsync();

                                if (conflicting.Any())
                                {
                                    // Remove their change logs first (cascade may not fire via EF)
                                    var conflictIds = conflicting.Select(c => c.Id).ToList();
                                    var logsToRemove = await _context.TimeEntryChangeLogs
                                        .Where(l => conflictIds.Contains(l.TimeEntryId))
                                        .ToListAsync();
                                    _context.TimeEntryChangeLogs.RemoveRange(logsToRemove);
                                    _context.TimeEntries.RemoveRange(conflicting);
                                }

                                await _context.SaveChangesAsync();

                                // Log the admin override in change history
                                var requesterId = req.RequesterId ?? req.EmployeeId;
                                await WriteLog(liveSession.Id, liveSession.EmployeeId,
                                    "AdminOverride",
                                    originalClockIn,   // OldClockIn = original live session start
                                    clockIn,           // NewClockIn = admin-set earlier time
                                    null, null,
                                    $"Admin override: session back-dated from {originalClockIn:HH:mm} to {clockIn:HH:mm} by employee #{requesterId}");

                                await RecalcDayEntries(req.EmployeeId, targetDate, req.OrganizationId);

                                return Ok(new
                                {
                                    entryId        = liveSession.Id,
                                    clockIn        = liveSession.ClockIn,
                                    isManual       = liveSession.IsManual,
                                    isHourEntry    = liveSession.IsHourEntry,
                                    adminOverride  = true,
                                    originalClockIn = originalClockIn
                                });
                            }
                            else
                            {
                                // Requested time is NOT earlier than live session start —
                                // just add it as a regular manual entry (clock-in after live start
                                // means it's a separate session segment, e.g. after a break).
                                var entry2 = new TimeEntry
                                {
                                    EmployeeId     = req.EmployeeId,
                                    ClockIn        = clockIn,
                                    IsManual       = true,
                                    OrganizationId = req.OrganizationId
                                };
                                _context.TimeEntries.Add(entry2);
                                await _context.SaveChangesAsync();
                                await WriteLog(entry2.Id, entry2.EmployeeId, "Added", clockIn, null, null, null, null);
                                await RecalcDayEntries(req.EmployeeId, targetDate, req.OrganizationId);
                                return Ok(new { entryId = entry2.Id, clockIn = entry2.ClockIn, isManual = entry2.IsManual, isHourEntry = entry2.IsHourEntry });
                            }
                        }
                        // No live session found — fall through to normal add below.
                    }

                    // ── Normal (non-admin) path ─────────────────────────────────────
                    // Only block open manual clock-in when adding for TODAY and a live session exists.
                    // Past-date manual entries are always allowed regardless of live session.
                    if (targetDate == todayLocal && !req.IsAdminOverride)
                    {
                        var hasLiveSession = await _context.TimeEntries.AnyAsync(e =>
                            e.EmployeeId == req.EmployeeId &&
                            e.ClockOut   == null           &&
                            !e.IsManual                    &&
                            !e.IsHourEntry);

                        if (hasLiveSession)
                            return BadRequest(new { error = "Cannot add an open clock-in while a live session is running. Add a clock-out time instead." });
                    }

                    // Admin adding a clock-in for today with a custom past time:
                    // create as IsManual=false so GetStatus / live-timer recognise it as
                    // an active session. For past-date entries, always mark as manual.
                    bool createAsLive = req.IsAdminOverride && targetDate == todayLocal;
                    var entry = new TimeEntry
                    {
                        EmployeeId     = req.EmployeeId,
                        ClockIn        = clockIn,
                        IsManual       = !createAsLive,
                        OrganizationId = req.OrganizationId
                    };
                    _context.TimeEntries.Add(entry);
                    await _context.SaveChangesAsync();
                    var logAction = req.IsAdminOverride ? "AdminOverride" : "Added";
                    var logReason = req.IsAdminOverride
                        ? $"Admin override: clock-in set to {clockIn:HH:mm} by employee #{req.RequesterId ?? req.EmployeeId}"
                        : null;
                    await WriteLog(entry.Id, entry.EmployeeId, logAction, clockIn, clockIn, null, null, logReason);
                    await RecalcDayEntries(req.EmployeeId, targetDate, req.OrganizationId);
                    return Ok(new { entryId = entry.Id, clockIn = entry.ClockIn, isManual = entry.IsManual, isHourEntry = entry.IsHourEntry });
                }

                if (req.EntryType == "out")
                {
                    if (string.IsNullOrEmpty(req.Time))
                        return BadRequest(new { error = "Time is required for Out entry." });

                    var targetDate        = DateTime.Parse(req.Date).Date;
                    var windowStart       = targetDate;
                    var windowEnd         = targetDate.AddDays(1);
                    var requestedClockOut = ParseLocal(req.Date, req.Time);

                    // Block future times (allow 2-min tolerance for clock skew)
                    if (requestedClockOut > DateTime.Now.AddMinutes(2))
                        return BadRequest(new { error = "Clock-out time cannot be in the future." });

                    // Find the most recent open (no clock-out) MANUAL entry on targetDate
                    // whose ClockIn is before the requested clock-out time.
                    // IsManual = true ensures we NEVER touch the live/active session
                    // (real clock-ins have IsManual = false).
                    var open = await _context.TimeEntries
                        .Where(e => e.EmployeeId == req.EmployeeId
                                 && e.ClockOut   == null
                                 && !e.IsHourEntry
                                 && e.IsManual                           // ← never touch the live session
                                 && e.ClockIn    >= windowStart
                                 && e.ClockIn    <  windowEnd
                                 && e.ClockIn    <  requestedClockOut)
                        .OrderByDescending(e => e.ClockIn)
                        .FirstOrDefaultAsync();

                    if (open != null)
                    {
                        var oldClockIn  = open.ClockIn;
                        open.ClockOut   = ParseLocal(req.Date, req.Time);
                        var dur         = open.ClockOut.Value - open.ClockIn;
                        open.WorkedHours = $"{(int)dur.TotalHours}h {dur.Minutes}m";
                        open.IsManual   = true;
                        await _context.SaveChangesAsync();
                        await WriteLog(open.Id, open.EmployeeId, "Added", oldClockIn, null, open.ClockOut, null, null);
                        await RecalcDayEntries(req.EmployeeId, targetDate, req.OrganizationId);
                        return Ok(new { entryId = open.Id, clockIn = open.ClockIn, clockOut = open.ClockOut, workedHours = open.WorkedHours, isManual = open.IsManual, isHourEntry = open.IsHourEntry });
                    }
                    else
                    {
                        // No open entry on that date — create a standalone clock-out record.
                        var clockOut = ParseLocal(req.Date, req.Time);
                        var entry = new TimeEntry { EmployeeId = req.EmployeeId, ClockIn = clockOut, ClockOut = clockOut, IsManual = true, OrganizationId = req.OrganizationId };
                        _context.TimeEntries.Add(entry);
                        await _context.SaveChangesAsync();
                        await WriteLog(entry.Id, entry.EmployeeId, "Added", clockOut, null, clockOut, null, null);
                        await RecalcDayEntries(req.EmployeeId, targetDate, req.OrganizationId);
                        return Ok(new { entryId = entry.Id, clockOut = entry.ClockOut, isManual = entry.IsManual, isHourEntry = entry.IsHourEntry });
                    }
                }

                if (req.EntryType == "break")
                {
                    if (string.IsNullOrEmpty(req.Time))
                        return BadRequest(new { error = "Time is required for Break entry." });

                    var clockIn    = ParseLocal(req.Date, req.Time);
                    var entry = new TimeEntry
                    {
                        EmployeeId  = req.EmployeeId,
                        ClockIn     = clockIn,
                        IsManual    = true,
                        IsBreakEntry = true
                    };
                    _context.TimeEntries.Add(entry);
                    await _context.SaveChangesAsync();
                    // Log only the break start (manual action) — break-end clock-in is auto, not logged
                    await WriteLog(entry.Id, entry.EmployeeId, "AddedBreak", clockIn, null, null, null, null);
                    await RecalcDayEntries(req.EmployeeId, clockIn.Date, req.OrganizationId);
                    return Ok(new { entryId = entry.Id, clockIn = entry.ClockIn, isManual = entry.IsManual, isBreakEntry = entry.IsBreakEntry });
                }

                return BadRequest(new { error = $"Unknown entry type: {req.EntryType}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddManualEntry failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET api/Attendance/export?from=2026-03-12&to=2026-03-12
        // Returns an XLS file matching Jibble's timesheet export format exactly
        [HttpGet("export")]
        public async Task<IActionResult> ExportTimesheet([FromQuery] string from, [FromQuery] string to,
                                                          [FromQuery] int? organizationId = null)
        {
            try
            {
                if (!DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
                    return BadRequest(new { error = "Invalid date range." });

                // DB stores local time — simple direct date range
                var start = fromDate.Date;
                var end   = toDate.Date.AddDays(1);

                var employees = await _context.Employees
                    .Where(e => e.IsActive)
                    .OrderBy(e => e.FullName)
                    .Select(e => new { e.Id, e.FullName, e.MemberCode, e.WorkSchedule })
                    .ToListAsync();

                // ── Org-scoped visibility ─────────────────────────────────────────
                if (organizationId.HasValue && organizationId.Value > 0)
                {
                    var orgMemberIds = await _context.OrganizationMembers
                        .Where(m => m.OrganizationId == organizationId.Value && m.IsActive)
                        .Select(m => m.EmployeeId)
                        .ToListAsync();

                    employees = employees.Where(e => orgMemberIds.Contains(e.Id)).ToList();
                }

                var rangeEntries = await _context.TimeEntries
                    .Where(t => t.ClockIn >= start && t.ClockIn < end)
                    .ToListAsync();

                var fromDateOnly = DateOnly.FromDateTime(fromDate.Date);
                var toDateOnly   = DateOnly.FromDateTime(toDate.Date);

                string FormatSpan(TimeSpan ts) =>
                    ts == TimeSpan.Zero ? "—"
                    : ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
                    : $"{ts.Minutes}m";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<html xmlns:x=\"urn:schemas-microsoft-com:office:excel\">");
                sb.AppendLine("<head><meta charset=\"UTF-8\"></head><body><table>");
                sb.AppendLine("<tr><td>Day</td><td>Date</td><td>Full Name</td><td>Member Code</td>" +
                              "<td>Work Schedule</td><td>Tracked Hours</td><td>Worked Hours</td>" +
                              "<td>Payroll Hours</td><td>Regular Hours</td><td>First In</td><td>Last Out</td></tr>");

                var allDates = Enumerable.Range(0, (toDateOnly.DayNumber - fromDateOnly.DayNumber) + 1)
                    .Select(i => fromDateOnly.AddDays(i)).ToList();

                foreach (var emp in employees)
                {
                    foreach (var date in allDates)
                    {
                        var dayEntries = rangeEntries
                            .Where(e => e.EmployeeId == emp.Id &&
                                        DateOnly.FromDateTime(e.ClockIn) == date)
                            .OrderBy(e => e.ClockIn).ToList();

                        if (!dayEntries.Any()) continue;

                        // firstIn/lastOut must only reflect REAL clock-in/out button presses.
                        // Hour entries store ClockIn = midnight (fake) — exclude them.
                        var realDayEnts  = dayEntries.Where(e => !e.IsHourEntry).ToList();
                        var firstIn      = realDayEnts.Any() ? realDayEnts.First().ClockIn : (DateTime?)null;
                        var lastOutEntry = realDayEnts.LastOrDefault(e => e.ClockOut != null);
                        var isOngoing    = dayEntries.Any(e => e.ClockOut == null);

                        var tracked = dayEntries
                            .Where(e => e.ClockOut != null)
                            .Aggregate(TimeSpan.Zero, (a, e) => a + (e.ClockOut!.Value - e.ClockIn));
                        if (isOngoing)
                        { var el = DateTime.Now - dayEntries.First(e => e.ClockOut == null).ClockIn; if (el > TimeSpan.Zero) tracked += el; }

                        var firstInStr  = firstIn.HasValue ? firstIn.Value.ToString("hh:mm tt") : "-";
                        var lastOutStr  = isOngoing ? "-"
                            : lastOutEntry?.ClockOut?.ToString("hh:mm tt") ?? "-";

                        sb.AppendLine($"<tr><td>{date.DayOfWeek}</td><td>{date:M/d/yyyy}</td>" +
                                      $"<td>{emp.FullName}</td><td>{emp.MemberCode ?? ""}</td>" +
                                      $"<td>{emp.WorkSchedule ?? "Default Work Schedule"}</td>" +
                                      $"<td>{FormatSpan(tracked)}</td><td>{FormatSpan(tracked)}</td>" +
                                      $"<td>{FormatSpan(tracked)}</td><td>{FormatSpan(tracked)}</td>" +
                                      $"<td>{firstInStr}</td><td>{lastOutStr}</td></tr>");
                    }
                }

                sb.AppendLine("</table></body></html>");

                var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                var filename = $"Timesheet_{from}_to_{to}.xls";
                return File(bytes, "application/vnd.ms-excel", filename);
            }
            catch (Exception ex) { _logger.LogError(ex, "ExportTimesheet failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // GET api/Attendance/changelog/{employeeId}/bydate?date=2026-02-21
        [HttpGet("changelog/{employeeId}/bydate")]
        public async Task<IActionResult> GetChangeLogByDate(int employeeId, [FromQuery] string date)
        {
            try
            {
                if (!DateTime.TryParse(date, out var parsedDate))
                    return BadRequest(new { error = "Invalid date format." });

                // DB stores local time — direct date window
                var windowStart = parsedDate.Date;
                var windowEnd   = parsedDate.Date.AddDays(1);
                var targetDate  = DateOnly.FromDateTime(parsedDate.Date);

                // Pre-load employee names to avoid subquery issues
                var empNames = await _context.Employees
                    .ToDictionaryAsync(e => e.Id, e => e.FullName);

                // JOIN TimeEntryChangeLogs with TimeEntries to filter by entry owner
                var rawLogs = await (
                    from log in _context.TimeEntryChangeLogs
                    join te in _context.TimeEntries on log.TimeEntryId equals te.Id
                    where te.EmployeeId == employeeId
                       && log.OldClockIn >= windowStart
                       && log.OldClockIn < windowEnd
                    orderby log.ChangedAt descending
                    select new
                    {
                        id = log.Id,
                        timeEntryId = (int?)log.TimeEntryId,
                        changedByEmpId = log.EmployeeId,  // EmployeeId holds the "changed by" value
                        action = log.Action,
                        oldClockIn = log.OldClockIn,
                        oldClockOut = log.OldClockOut,
                        newClockIn = log.NewClockIn,
                        newClockOut = log.NewClockOut,
                        reasonForChange = log.ReasonForChange,
                        changedAt = log.ChangedAt
                    }
                ).ToListAsync();

                var result = rawLogs
                    .Where(l => l.oldClockIn.HasValue &&
                                DateOnly.FromDateTime(l.oldClockIn.Value) == targetDate)
                    .Select(l => new
                    {
                        id = l.id,
                        timeEntryId = l.timeEntryId,
                        changedByName = empNames.TryGetValue(l.changedByEmpId, out var n) ? n : "Unknown",
                        action = l.action,
                        oldClockIn = l.oldClockIn,
                        oldClockOut = l.oldClockOut,
                        newClockIn = l.newClockIn,
                        newClockOut = l.newClockOut,
                        reasonForChange = l.reasonForChange,
                        changedAt = l.changedAt
                    })
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChangeLogByDate failed for employee {Id} date {Date}", employeeId, date);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // GET api/Attendance/export/xlsx
        //   ?from=2026-03-12&to=2026-03-12
        //   &includeRawTimesheets=true        → "Timesheets list in single tabs"
        //   &includeRawTimeEntries=true       → "Time entries list in single tabs"
        //   &includePerMemberSummary=true     → "Timesheets per member" - Summary
        //   &includePerMemberDetailed=true    → "Timesheets per member" - Detailed
        // "Team Summary" sheet is ALWAYS included.
        // ══════════════════════════════════════════════════════════════════════
        [HttpGet("export/xlsx")]
        public async Task<IActionResult> ExportTimesheetXlsx(
            [FromQuery] string from,
            [FromQuery] string to,
            [FromQuery] bool includeRawTimesheets     = false,
            [FromQuery] bool includeRawTimeEntries    = false,
            [FromQuery] bool includePerMemberSummary  = false,
            [FromQuery] bool includePerMemberDetailed = false,
            [FromQuery] int? organizationId           = null)
        {
            if (!DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
                return BadRequest(new { error = "Invalid date range." });

            // ── Load data ────────────────────────────────────────────────────
            // DB stores local time — direct date range
            var start = fromDate.Date;
            var end   = toDate.Date.AddDays(1);

            var employees = await _context.Employees
                .Where(e => e.IsActive)
                .OrderBy(e => e.FullName)
                .Select(e => new { e.Id, e.FullName, e.MemberCode, e.WorkSchedule, e.GroupId })
                .ToListAsync();

            // ── Org-scoped visibility ─────────────────────────────────────────
            if (organizationId.HasValue && organizationId.Value > 0)
            {
                var orgMemberIds = await _context.OrganizationMembers
                    .Where(m => m.OrganizationId == organizationId.Value && m.IsActive)
                    .Select(m => m.EmployeeId)
                    .ToListAsync();

                employees = employees.Where(e => orgMemberIds.Contains(e.Id)).ToList();
            }

            var allEntries = await _context.TimeEntries
                .Where(t => t.ClockIn >= start && t.ClockIn < end)
                .OrderBy(t => t.ClockIn)
                .ToListAsync();

            var fromDO = DateOnly.FromDateTime(fromDate.Date);
            var toDO   = DateOnly.FromDateTime(toDate.Date);

            var rangeEntries = allEntries.Where(e => {
                var d = DateOnly.FromDateTime(e.ClockIn);
                return d >= fromDO && d <= toDO;
            }).ToList();

            bool   isSingleDay = fromDate.Date == toDate.Date;
            string dateLabel   = isSingleDay
                ? fromDate.ToString("d MMMM yyyy")
                : $"{fromDate:d MMMM yyyy} - {toDate:d MMMM yyyy}";
            string dayAbbr     = isSingleDay ? fromDate.ToString("ddd").ToUpper() : "";
            string groupName   = "ASK GROUPS";
            string exportedOn  = $"Exported on {DateTime.Now:d MMMM yyyy}";

            // ── Format TimeSpan → "12h 20m" or "-" ──────────────────────────
            string Fmt(TimeSpan ts) =>
                ts <= TimeSpan.Zero ? "-"
                : ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
                : $"{ts.Minutes}m";

            // ── Per-employee per-day aggregate ───────────────────────────────
            (TimeSpan tracked, TimeSpan payroll, TimeSpan regular,
             DateTime? firstIn, DateTime? lastOut, bool ongoing)
            DayData(int empId, DateOnly date)
            {
                var dayEnts = rangeEntries
                    .Where(e => e.EmployeeId == empId &&
                                DateOnly.FromDateTime(e.ClockIn) == date)
                    .OrderBy(e => e.ClockIn).ToList();

                if (!dayEnts.Any()) return default;

                // firstIn/lastOut must only reflect REAL clock-in/out button presses.
                // Hour entries store ClockIn = midnight (fake) — exclude them.
                var realEnts  = dayEnts.Where(e => !e.IsHourEntry).ToList();
                var firstIn   = realEnts.Any() ? (DateTime?)realEnts.First().ClockIn : null;
                var lastOutE  = realEnts.LastOrDefault(e => e.ClockOut != null);
                var isOngoing = dayEnts.Any(e => e.ClockOut == null);

                var tracked = dayEnts.Where(e => e.ClockOut != null)
                    .Aggregate(TimeSpan.Zero, (a, e) => a + (e.ClockOut!.Value - e.ClockIn));
                if (isOngoing)
                {
                    var elXl2 = DateTime.Now - dayEnts.First(e => e.ClockOut == null).ClockIn;
                    if (elXl2 > TimeSpan.Zero) tracked += elXl2;
                }

                return (tracked, tracked, tracked, firstIn, lastOutE?.ClockOut, isOngoing);
            }

            var allDates = Enumerable.Range(0, (toDO.DayNumber - fromDO.DayNumber) + 1)
                .Select(i => fromDO.AddDays(i)).ToList();

            var scheduleRows = new[]
            {
                ("Monday",    "9h 00m", "0h 00m", "Daily OT",          "54h 00m"),
                ("Tuesday",   "9h 00m", "0h 00m", "Double OT",         "60h 00m"),
                ("Wednesday", "9h 00m", "0h 00m", "Weekly OT",         "-"),
                ("Thursday",  "9h 00m", "0h 00m", "Rest day OT",       "Disabled"),
                ("Friday",    "9h 00m", "0h 00m", "Public Holiday OT", "Disabled"),
                ("Saturday",  "9h 00m", "0h 00m", "",                  ""),
                ("Sunday",    "REST",   "REST",   "SPLIT TIMESHEET",   "00:00:00"),
            };

            // ── Helper: write Work Schedule + Filters + footer block ─────────
            void WriteScheduleBlock(IXLWorksheet ws, int startRow)
            {
                int r = startRow;
                ws.Cell(r, 1).Value = "Work Schedule"; r++;
                ws.Cell(r, 1).Value = "Default Work Schedule"; r += 2;
                ws.Cell(r, 1).Value = "DAY";
                ws.Cell(r, 2).Value = "DURATION";
                ws.Cell(r, 3).Value = "BREAK(S)";
                ws.Cell(r, 5).Value = "OVERTIME RULES";
                ws.Cell(r, 8).Value = "AUTOMATIC DEDUCTIONS";
                ws.Cell(r, 13).Value = "HOLIDAYS"; r++;
                foreach (var (day, dur, brk, otRule, otVal) in scheduleRows)
                {
                    ws.Cell(r, 1).Value = day;
                    ws.Cell(r, 2).Value = dur;
                    ws.Cell(r, 3).Value = brk;
                    if (!string.IsNullOrEmpty(otRule)) ws.Cell(r, 5).Value = otRule;
                    if (!string.IsNullOrEmpty(otVal))  ws.Cell(r, 6).Value = otVal;
                    r++;
                }
                r += 2;
                ws.Cell(r, 1).Value = "Filters"; r += 5;
                ws.Cell(r, 1).Value = "Note: Durations are displayed in time format (e.g. 1h 06m is 1 hour and 6 mins).";
                ws.Cell(r, 7).Value = exportedOn;
            }

            // ════════════════════════════════════════════════════════════════
            using var wb = new ClosedXML.Excel.XLWorkbook();

            // ══════════════════════════════════════════════════════════════════
            // SHEET 1 — Team Summary (always present)
            // ══════════════════════════════════════════════════════════════════
            var tsSht = wb.Worksheets.Add("Team Summary");
            tsSht.Style.Font.FontName = "Arial";
            tsSht.Style.Font.FontSize = 10;

            tsSht.Cell(1, 1).Value = "Daily Timesheets";
            tsSht.Cell(1, 7).Value = groupName;
            tsSht.Cell(3, 1).Value = isSingleDay ? "Day" : "Period";
            tsSht.Cell(3, 2).Value = dateLabel;
            tsSht.Cell(5, 1).Value = "Legend";
            tsSht.Cell(5, 2).Value = "Public holiday";
            tsSht.Cell(5, 4).Value = "Rest day";
            tsSht.Cell(5, 6).Value = "Time off";
            tsSht.Cell(8, 4).Value = dayAbbr;

            // Header row
            tsSht.Cell(9, 1).Value = "NAME";
            tsSht.Cell(9, 2).Value = "MEMBER CODE";
            tsSht.Cell(9, 3).Value = "TYPE";
            tsSht.Cell(9, 4).Value = isSingleDay ? fromDate.ToString("yyyy-MM-dd") : "TOTALS";
            tsSht.Cell(9, 5).Value = "TOTALS";
            tsSht.Cell(9, 6).Value = "RATES";
            tsSht.Cell(9, 7).Value = "TOTALS";

            int tsRow = 10;
            var teamPayroll = TimeSpan.Zero;
            var teamRegular = TimeSpan.Zero;

            foreach (var emp in employees)
            {
                var totalPayroll = TimeSpan.Zero;
                foreach (var date in allDates)
                {
                    var d = DayData(emp.Id, date);
                    if (d.tracked > TimeSpan.Zero) totalPayroll += d.payroll;
                }

                string dayVal = isSingleDay
                    ? (DayData(emp.Id, fromDO).tracked > TimeSpan.Zero
                        ? Fmt(DayData(emp.Id, fromDO).payroll) : "-")
                    : "-";

                tsSht.Cell(tsRow, 1).Value = emp.FullName;
                tsSht.Cell(tsRow, 2).Value = emp.MemberCode ?? "";
                tsSht.Cell(tsRow, 3).Value = "Payroll Hours";
                tsSht.Cell(tsRow, 4).Value = dayVal;
                tsSht.Cell(tsRow, 5).Value = Fmt(totalPayroll); tsRow++;

                tsSht.Cell(tsRow, 3).Value = "Regular Hours";
                tsSht.Cell(tsRow, 4).Value = dayVal;
                tsSht.Cell(tsRow, 5).Value = Fmt(totalPayroll); tsRow++;

                tsSht.Cell(tsRow, 3).Value = "Daily OT";
                tsSht.Cell(tsRow, 4).Value = "-";
                tsSht.Cell(tsRow, 5).Value = "-"; tsRow++;

                tsSht.Cell(tsRow, 3).Value = "Double OT";
                tsSht.Cell(tsRow, 4).Value = "-";
                tsSht.Cell(tsRow, 5).Value = "-"; tsRow++;

                teamPayroll += totalPayroll;
                teamRegular += totalPayroll;
            }

            tsRow += 2;
            tsSht.Cell(tsRow, 3).Value = "Total Hours"; tsRow++;
            tsSht.Cell(tsRow, 3).Value = "Payroll";
            tsSht.Cell(tsRow, 4).Value = Fmt(teamPayroll);
            tsSht.Cell(tsRow, 5).Value = Fmt(teamPayroll); tsRow++;
            tsSht.Cell(tsRow, 3).Value = "Regular";
            tsSht.Cell(tsRow, 4).Value = Fmt(teamRegular);
            tsSht.Cell(tsRow, 5).Value = Fmt(teamRegular); tsRow++;
            foreach (var lbl in new[] { "Daily OT", "Double OT", "Weekly OT", "Rest Day OT", "Public Holiday OT", "Paid Time Off" })
            {
                tsSht.Cell(tsRow, 3).Value = lbl;
                tsSht.Cell(tsRow, 4).Value = "-";
                tsSht.Cell(tsRow, 5).Value = "-"; tsRow++;
            }
            tsRow += 2;
            WriteScheduleBlock(tsSht, tsRow);

            // ══════════════════════════════════════════════════════════════════
            // SHEET — Raw Timesheets  (option: includeRawTimesheets)
            // ══════════════════════════════════════════════════════════════════
            if (includeRawTimesheets)
            {
                var ws = wb.Worksheets.Add("Raw Timesheets");
                ws.Style.Font.FontName = "Arial";
                ws.Style.Font.FontSize = 10;

                ws.Cell(1, 1).Value = "Custom period - Team RAW TIMESHEET";

                var hdrs = new[]
                {
                    "Date","Day","Full Name","Member Code","Position","Employment Type",
                    "Group","Manager(s)","Work Schedule","Tracked Hours","Worked Hours",
                    "Break Hours (paid)","Break Hours (unpaid)","Deductions",
                    "Payroll Hours","Regular Hours","Daily OT","Daily Double OT",
                    "Weekly OT","Rest Day OT","Public Holiday OT",
                    "Daily OT Rate","Daily Double OT Rate","Weekly OT Rate",
                    "Rest Day OT Rate","Public Holiday OT Rate",
                    "Paid Time Off","Time Off Name","Billable Rate","Billable Amount",
                    "First In","Last Out"
                };
                for (int c = 0; c < hdrs.Length; c++) ws.Cell(2, c + 1).Value = hdrs[c];

                int r = 3;
                foreach (var emp in employees)
                {
                    foreach (var date in allDates)
                    {
                        var d = DayData(emp.Id, date);
                        if (d.tracked <= TimeSpan.Zero) continue;

                        ws.Cell(r,  1).Value = date.ToString("yyyy-MM-dd");
                        ws.Cell(r,  2).Value = date.DayOfWeek.ToString();
                        ws.Cell(r,  3).Value = emp.FullName;
                        ws.Cell(r,  4).Value = emp.MemberCode ?? "";
                        ws.Cell(r,  5).Value = "";
                        ws.Cell(r,  6).Value = "";
                        ws.Cell(r,  7).Value = "";
                        ws.Cell(r,  8).Value = "";
                        ws.Cell(r,  9).Value = emp.WorkSchedule ?? "Default Work Schedule";
                        ws.Cell(r, 10).Value = Fmt(d.tracked);
                        ws.Cell(r, 11).Value = Fmt(d.tracked);
                        ws.Cell(r, 12).Value = "0h 00m";
                        ws.Cell(r, 13).Value = "0h 00m";
                        ws.Cell(r, 14).Value = "0h 00m";
                        ws.Cell(r, 15).Value = Fmt(d.payroll);
                        ws.Cell(r, 16).Value = Fmt(d.regular);
                        ws.Cell(r, 17).Value = "0h 00m";
                        ws.Cell(r, 18).Value = "0h 00m";
                        ws.Cell(r, 19).Value = "0h 00m";
                        ws.Cell(r, 20).Value = "0h 00m";
                        ws.Cell(r, 21).Value = "0h 00m";
                        ws.Cell(r, 22).Value = 0;
                        ws.Cell(r, 23).Value = 0;
                        ws.Cell(r, 24).Value = 0;
                        ws.Cell(r, 25).Value = 0;
                        ws.Cell(r, 26).Value = 0;
                        ws.Cell(r, 27).Value = "0h 00m";
                        ws.Cell(r, 28).Value = "";
                        ws.Cell(r, 29).Value = 0;
                        ws.Cell(r, 30).Value = 0;
                        ws.Cell(r, 31).Value = d.firstIn.HasValue
                            ? d.firstIn.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";
                        ws.Cell(r, 32).Value = (d.lastOut.HasValue && !d.ongoing)
                            ? d.lastOut.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";
                        r++;
                    }
                }
            }

            // ══════════════════════════════════════════════════════════════════
            // SHEET — Raw Time Entries  (option: includeRawTimeEntries)
            // ══════════════════════════════════════════════════════════════════
            if (includeRawTimeEntries)
            {
                var we = wb.Worksheets.Add("Raw Time Entries");
                we.Style.Font.FontName = "Arial";
                we.Style.Font.FontSize = 10;

                we.Cell(1, 1).Value = "Custom period - Team RAW TIME ENTRIES";

                var eHdrs = new[]
                {
                    "Date","Full Name","Member Code","Position","Employment Type",
                    "Group","Manager(s)","Entry Type","Time","Time (unrounded)",
                    "Duration","Break","Break Type","Project","Project code",
                    "Project location","Activity","Activity Code","Client","Client Code",
                    "Billable Amount","Notes","Clock In Device","Device Name",
                    "Clock In Location","Clock in Location Address","Author","Author Code",
                    "Created On","Last Edited By","Last Edited On","Role",
                    "Manager(s) Code(s)","Selfie Taken","Offline Mode","Flagged",
                    "Timezone","Automatic Jibble out","Mobile App Mode","Kiosk Name",
                    "Work Schedule Name"
                };
                for (int c = 0; c < eHdrs.Length; c++) we.Cell(2, c + 1).Value = eHdrs[c];

                int r = 3;
                foreach (var emp in employees)
                {
                    var empEntries = rangeEntries
                        .Where(e => e.EmployeeId == emp.Id)
                        .OrderBy(e => e.ClockIn).ToList();

                    foreach (var entry in empEntries)
                    {
                        string localDate = entry.ClockIn.ToString("yyyy-MM-dd");

                        // Clock-In row
                        we.Cell(r,  1).Value = localDate;
                        we.Cell(r,  2).Value = emp.FullName;
                        we.Cell(r,  3).Value = emp.MemberCode ?? "";
                        we.Cell(r,  8).Value = "In";
                        we.Cell(r,  9).Value = entry.ClockIn.ToString("yyyy-MM-dd HH:mm:ss");
                        we.Cell(r, 11).Value = entry.ClockOut.HasValue
                            ? Fmt(entry.ClockOut.Value - entry.ClockIn) : "-";
                        we.Cell(r, 21).Value = 0;
                        we.Cell(r, 23).Value = "Web";
                        we.Cell(r, 24).Value = "Chrome";
                        we.Cell(r, 27).Value = emp.FullName;
                        we.Cell(r, 28).Value = emp.MemberCode ?? "";
                        we.Cell(r, 29).Value = entry.ClockIn.ToString("M/d/yyyy h:mm:ss tt");
                        we.Cell(r, 32).Value = "Owner";
                        we.Cell(r, 34).Value = "No";
                        we.Cell(r, 35).Value = "No";
                        we.Cell(r, 36).Value = "No";
                        we.Cell(r, 37).Value = "Asia/Calcutta";
                        we.Cell(r, 38).Value = "No";
                        we.Cell(r, 41).Value = emp.WorkSchedule ?? "Default Work Schedule";
                        r++;

                        // Clock-Out row
                        if (entry.ClockOut.HasValue)
                        {
                            we.Cell(r,  1).Value = localDate;
                            we.Cell(r,  2).Value = emp.FullName;
                            we.Cell(r,  3).Value = emp.MemberCode ?? "";
                            we.Cell(r,  8).Value = "Out";
                            we.Cell(r,  9).Value = entry.ClockOut.Value.ToString("yyyy-MM-dd HH:mm:ss");
                            we.Cell(r, 21).Value = 0;
                            we.Cell(r, 23).Value = "Web";
                            we.Cell(r, 24).Value = "Chrome";
                            we.Cell(r, 27).Value = emp.FullName;
                            we.Cell(r, 28).Value = emp.MemberCode ?? "";
                            we.Cell(r, 29).Value = entry.ClockOut.Value.ToString("M/d/yyyy h:mm:ss tt");
                            we.Cell(r, 32).Value = "Owner";
                            we.Cell(r, 34).Value = "No";
                            we.Cell(r, 35).Value = "No";
                            we.Cell(r, 36).Value = "No";
                            we.Cell(r, 37).Value = "Asia/Calcutta";
                            we.Cell(r, 38).Value = "No";
                            we.Cell(r, 41).Value = emp.WorkSchedule ?? "Default Work Schedule";
                            r++;
                        }
                    }
                }
            }

            // ══════════════════════════════════════════════════════════════════
            // SHEETS — Per Member Summary or Detailed
            // ══════════════════════════════════════════════════════════════════
            if (includePerMemberSummary || includePerMemberDetailed)
            {
                foreach (var emp in employees)
                {
                    bool hasData = allDates.Any(date => DayData(emp.Id, date).tracked > TimeSpan.Zero);
                    if (!hasData) continue;

                    string rawName = $"{emp.FullName} ({emp.MemberCode ?? emp.Id.ToString()}) Summary";
                    string sheetName = rawName.Length > 31 ? rawName[..31] : rawName;

                    var ms = wb.Worksheets.Add(sheetName);
                    ms.Style.Font.FontName = "Arial";
                    ms.Style.Font.FontSize = 10;

                    // Header block
                    ms.Cell(1,  1).Value = includePerMemberDetailed ? "Daily Timesheet" : "Timesheets";
                    ms.Cell(1, 13).Value = groupName;
                    ms.Cell(3,  1).Value = isSingleDay ? "Day" : "Period";
                    ms.Cell(3,  2).Value = dateLabel;
                    ms.Cell(5,  1).Value = "Member";
                    ms.Cell(5,  2).Value = emp.FullName;
                    ms.Cell(6,  1).Value = "Code";
                    ms.Cell(6,  2).Value = emp.MemberCode ?? "";
                    ms.Cell(7,  1).Value = "Group";
                    ms.Cell(8,  1).Value = "Manager(s)";
                    ms.Cell(9,  1).Value = "Legend";
                    ms.Cell(9,  2).Value = "Public holiday";
                    ms.Cell(9,  4).Value = "Rest day";
                    ms.Cell(9,  7).Value = "Time off";

                    // Column headers — row 11
                    if (includePerMemberDetailed)
                    {
                        ms.Cell(11,  1).Value = "DATE";
                        ms.Cell(11,  2).Value = "DAY";
                        ms.Cell(11,  3).Value = "PAYROLL HRS";
                        ms.Cell(11,  4).Value = "REG. HRS";
                        ms.Cell(11,  5).Value = "DAILY OT";
                        ms.Cell(11,  6).Value = "DOUBLE OT";
                        ms.Cell(11,  7).Value = "REST DAY OT";
                        ms.Cell(11,  8).Value = "WEEKLY OT";
                        ms.Cell(11,  9).Value = "PUBLIC HOLIDAY OT";
                        ms.Cell(11, 10).Value = "PTO";
                        ms.Cell(11, 11).Value = "TIME OFF POLICY";
                        ms.Cell(11, 12).Value = "ENTRY TYPE";
                        ms.Cell(11, 13).Value = "TIME";
                        ms.Cell(11, 14).Value = "ACTIVITY";
                        ms.Cell(11, 15).Value = "PROJECT";
                        ms.Cell(11, 16).Value = "NOTES";
                        ms.Cell(11, 17).Value = "DURATION";
                        ms.Cell(11, 18).Value = "LOCATION";
                        ms.Cell(11, 19).Value = "ADDRESS";
                    }
                    else
                    {
                        ms.Cell(11,  1).Value = "DATE";
                        ms.Cell(11,  2).Value = "DAY";
                        ms.Cell(11,  3).Value = "FIRST IN";
                        ms.Cell(11,  4).Value = "LAST OUT";
                        ms.Cell(11,  5).Value = "PAYROLL HRS";
                        ms.Cell(11,  6).Value = "REG. HRS";
                        ms.Cell(11,  7).Value = "DAILY OT";
                        ms.Cell(11,  8).Value = "DOUBLE OT";
                        ms.Cell(11,  9).Value = "REST DAY OT";
                        ms.Cell(11, 10).Value = "WEEKLY OT";
                        ms.Cell(11, 11).Value = "PUBLIC HOLIDAY OT";
                        ms.Cell(11, 12).Value = "PTO";
                        ms.Cell(11, 13).Value = "TIME OFF POLICY";
                    }

                    // Data rows starting at row 12
                    int mr = 12;
                    var empTotalPayroll = TimeSpan.Zero;

                    foreach (var date in allDates)
                    {
                        var d = DayData(emp.Id, date);
                        if (d.tracked <= TimeSpan.Zero) continue;

                        empTotalPayroll += d.payroll;

                        string dateStr   = date.ToString("yyyy-MM-dd");
                        string dayStr    = date.DayOfWeek.ToString();
                        string firstInStr = d.firstIn.HasValue
                            ? d.firstIn.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";
                        string lastOutStr = (d.lastOut.HasValue && !d.ongoing)
                            ? d.lastOut.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";

                        if (includePerMemberDetailed)
                        {
                            // Date row with totals
                            ms.Cell(mr, 1).Value = dateStr;
                            ms.Cell(mr, 2).Value = dayStr;
                            ms.Cell(mr, 3).Value = Fmt(d.payroll);
                            ms.Cell(mr, 4).Value = Fmt(d.regular);

                            // Individual clock entry sub-rows
                            var dayEnts = rangeEntries
                                .Where(e => e.EmployeeId == emp.Id &&
                                            DateOnly.FromDateTime(e.ClockIn) == date)
                                .OrderBy(e => e.ClockIn).ToList();

                            foreach (var entry in dayEnts)
                            {
                                // Out event (clock-out of previous state, recorded at clockIn time by Jibble)
                                ms.Cell(mr, 12).Value = "Out";
                                ms.Cell(mr, 13).Value = entry.ClockIn.ToString("yyyy-MM-dd HH:mm:ss");
                                mr++;

                                // In event with duration
                                ms.Cell(mr, 12).Value = "In";
                                ms.Cell(mr, 13).Value = entry.ClockIn.AddSeconds(7).ToString("yyyy-MM-dd HH:mm:ss");
                                ms.Cell(mr, 17).Value = entry.ClockOut.HasValue
                                    ? Fmt(entry.ClockOut.Value - entry.ClockIn) : "-";
                                mr++;

                                // Final Out event
                                if (entry.ClockOut.HasValue)
                                {
                                    ms.Cell(mr, 12).Value = "Out";
                                    ms.Cell(mr, 13).Value = entry.ClockOut.Value.ToString("yyyy-MM-dd HH:mm:ss");
                                    mr++;
                                }
                            }

                            // Total Hours row
                            ms.Cell(mr, 2).Value = "Total Hours";
                            ms.Cell(mr, 3).Value = Fmt(empTotalPayroll);
                            ms.Cell(mr, 4).Value = Fmt(empTotalPayroll);
                            mr++;
                        }
                        else
                        {
                            // Summary: one row per day
                            ms.Cell(mr,  1).Value = dateStr;
                            ms.Cell(mr,  2).Value = dayStr;
                            ms.Cell(mr,  3).Value = firstInStr;
                            ms.Cell(mr,  4).Value = lastOutStr;
                            ms.Cell(mr,  5).Value = Fmt(d.payroll);
                            ms.Cell(mr,  6).Value = Fmt(d.regular);
                            ms.Cell(mr,  7).Value = "0h 00m";
                            ms.Cell(mr,  8).Value = "0h 00m";
                            ms.Cell(mr,  9).Value = "0h 00m";
                            ms.Cell(mr, 10).Value = "0h 00m";
                            ms.Cell(mr, 11).Value = "0h 00m";
                            ms.Cell(mr, 12).Value = "0h 00m";
                            mr++;

                            // Total Hours row
                            ms.Cell(mr,  4).Value = "Total Hours";
                            ms.Cell(mr,  5).Value = Fmt(empTotalPayroll);
                            ms.Cell(mr,  6).Value = Fmt(empTotalPayroll);
                            ms.Cell(mr,  7).Value = "0h 00m";
                            ms.Cell(mr,  8).Value = "0h 00m";
                            ms.Cell(mr,  9).Value = "0h 00m";
                            ms.Cell(mr, 10).Value = "0h 00m";
                            ms.Cell(mr, 11).Value = "0h 00m";
                            ms.Cell(mr, 12).Value = "0h 00m";
                            mr++;
                        }
                    }

                    mr += 3;
                    WriteScheduleBlock(ms, mr);
                }
            }

            // ── Stream the workbook ──────────────────────────────────────────
            using var stream = new System.IO.MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            string filename = $"Daily_Timesheet_{from}_to_{to}.xlsx";
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        }
        // ══════════════════════════════════════════════════════════════════════
        // ADD THIS ENDPOINT to your existing AttendanceController.cs
        // Paste it inside the AttendanceController class, before the closing brace.
        // ══════════════════════════════════════════════════════════════════════

        // POST api/Attendance/clockin-face
        // Called after JS face matching confirms identity on the kiosk.
        // The actual face comparison happens client-side; this endpoint
        // records the clock-in and optionally saves an audit photo.
        [HttpPost("clockin-face")]
        public async Task<IActionResult> ClockInWithFace([FromBody] APM.StaffZen.API.Dtos.FaceClockInDto dto)
        {
            try
            {
                if (dto.EmployeeId <= 0)
                    return BadRequest(new { error = "EmployeeId is required." });

                // Prevent double clock-in — scoped to the org if provided
                var existingQuery2 = _context.TimeEntries.Where(t =>
                    t.EmployeeId == dto.EmployeeId &&
                    t.ClockOut == null &&
                    !t.IsManual &&
                    !t.IsHourEntry);
                if (dto.OrganizationId > 0)
                    existingQuery2 = existingQuery2.Where(t => t.OrganizationId == dto.OrganizationId);

                var existing = await existingQuery2.AnyAsync();

                if (existing)
                    return BadRequest(new { error = "Already clocked in." });

                var employee = await _context.Employees.FindAsync(dto.EmployeeId);
                if (employee == null)
                    return NotFound(new { error = "Employee not found." });

                // ── Optional: save audit photo ────────────────────────────────────
                string? auditPhotoUrl = null;
                if (!string.IsNullOrEmpty(dto.AuditPhotoBase64))
                {
                    try
                    {
                        // Strip data-URI prefix if present: "data:image/jpeg;base64,..."
                        var base64 = dto.AuditPhotoBase64.Contains(',')
                            ? dto.AuditPhotoBase64.Split(',')[1]
                            : dto.AuditPhotoBase64;

                        var bytes = Convert.FromBase64String(base64);
                        var fileName = $"face_{dto.EmployeeId}_{DateTime.Now:yyyyMMddHHmmss}.jpg";
                        var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        Directory.CreateDirectory(folder);
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
                        auditPhotoUrl = $"/uploads/{fileName}";
                    }
                    catch (Exception photoEx)
                    {
                        _logger.LogWarning(photoEx, "Failed to save audit photo for employee {Id}.", dto.EmployeeId);
                        // Non-fatal — continue with the clock-in
                    }
                }

                // ── Record clock-in ───────────────────────────────────────────────
                var entry = new TimeEntry
                {
                    EmployeeId     = dto.EmployeeId,
                    OrganizationId = dto.OrganizationId > 0 ? dto.OrganizationId : (int?)null,
                    ClockIn        = DateTime.Now,
                    IsManual       = false
                };
                _context.TimeEntries.Add(entry);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Face clock-in: employee {Id} ({Name}) at {Time}.",
                    dto.EmployeeId, employee.FullName, entry.ClockIn);

                // ── Fire-and-forget notifications (same as regular clock-in) ──────
                var capturedFaceEmpId = dto.EmployeeId;
                var capturedFaceClockIn = entry.ClockIn;
                var capturedFaceEmail = employee.Email;
                var capturedFaceFullName = employee.FullName;
                var capturedFcmToken = employee.FcmToken;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var settings = await db.EmployeeNotificationSettings
                            .FirstOrDefaultAsync(s => s.EmployeeId == capturedFaceEmpId);

                        // Only notify if NotifClockIn is enabled
                        if (settings?.NotifClockIn == true)
                        {
                            if (settings.RemindersChannelEmail == true)
                                await _emailService.SendClockInEmail(capturedFaceEmail, capturedFaceFullName, capturedFaceClockIn);

                            if (settings.RemindersChannelPush == true && !string.IsNullOrEmpty(capturedFcmToken))
                                await _firebaseService.SendPushAsync(capturedFcmToken,
                                    "Clocked In ✅",
                                    $"You clocked in at {capturedFaceClockIn:hh:mm tt} via facial recognition.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Face clock-in notification failed for employee {Id}.", capturedFaceEmpId);
                    }
                });

                return Ok(new
                {
                    entryId = entry.Id,
                    clockIn = entry.ClockIn,
                    fullName = employee.FullName,
                    auditPhotoUrl
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ClockInWithFace failed for employee {Id}", dto.EmployeeId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

    // ── Raw SQL helper: reads TimeEntries bypassing EF model ────────────────────
    // Uses ADO.NET directly so it ALWAYS works regardless of whether the EF
    // model snapshot matches the live DB schema. If ClockInSelfieUrl /
    // ClockOutSelfieUrl columns do not yet exist in the DB the reader returns
    // null for those fields - clock-in/out still works, selfies just won't show.
    /// <summary>
    /// Returns a SQL fragment that filters TimeEntries by OrganizationId.
    /// When orgId is provided:  AND (OrganizationId = @orgId OR OrganizationId IS NULL)
    ///   — the IS NULL arm keeps legacy rows (created before multi-org) visible in any org.
    /// When orgId is null/0:    empty string (no extra filter).
    /// The parameter @orgId must be bound via ReadTimeEntriesRawAsync's organizationId argument.
    /// </summary>
    private static string BuildOrgFilter(int? organizationId)
    {
        if (!organizationId.HasValue || organizationId.Value <= 0) return "";
        return " AND (OrganizationId = @orgId OR OrganizationId IS NULL)";
    }

    private async Task<List<object>> ReadTimeEntriesRawAsync(
        string sql, int employeeId, DateTime start, DateTime? end, int? organizationId = null)
    {
        var results = new List<object>();
        var conn = _context.Database.GetDbConnection();
        bool wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var pEmp = cmd.CreateParameter(); pEmp.ParameterName = "@empId";
            pEmp.Value = employeeId; cmd.Parameters.Add(pEmp);

            var pStart = cmd.CreateParameter(); pStart.ParameterName = "@start";
            pStart.Value = start; cmd.Parameters.Add(pStart);

            if (end.HasValue)
            {
                var pEnd = cmd.CreateParameter(); pEnd.ParameterName = "@end";
                pEnd.Value = end.Value; cmd.Parameters.Add(pEnd);
            }

            // Bind @orgId when the caller added the org-filter clause to the SQL.
            if (organizationId.HasValue && organizationId.Value > 0)
            {
                var pOrg = cmd.CreateParameter(); pOrg.ParameterName = "@orgId";
                pOrg.Value = organizationId.Value; cmd.Parameters.Add(pOrg);
            }

            using var reader = await cmd.ExecuteReaderAsync();
            // Detect which columns actually exist so we handle DBs where the
            // selfie migration has not been applied yet gracefully.
            var colNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++) colNames.Add(reader.GetName(i));
            bool hasSelfieIn  = colNames.Contains("ClockInSelfieUrl");
            bool hasSelfieOut = colNames.Contains("ClockOutSelfieUrl");

            while (await reader.ReadAsync())
            {
                int     id          = reader.GetInt32(reader.GetOrdinal("Id"));
                var     clockIn     = reader.GetDateTime(reader.GetOrdinal("ClockIn"));
                var     clockOut    = reader.IsDBNull(reader.GetOrdinal("ClockOut"))    ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("ClockOut"));
                string? workedHrs   = reader.IsDBNull(reader.GetOrdinal("WorkedHours")) ? null : reader.GetString(reader.GetOrdinal("WorkedHours"));
                bool    isManual    = reader.GetBoolean(reader.GetOrdinal("IsManual"));
                bool    isHourEntry = reader.GetBoolean(reader.GetOrdinal("IsHourEntry"));
                bool    isBreakEntry= reader.GetBoolean(reader.GetOrdinal("IsBreakEntry"));
                string? selfieIn    = hasSelfieIn  && !reader.IsDBNull(reader.GetOrdinal("ClockInSelfieUrl"))
                                        ? reader.GetString(reader.GetOrdinal("ClockInSelfieUrl"))  : null;
                string? selfieOut   = hasSelfieOut && !reader.IsDBNull(reader.GetOrdinal("ClockOutSelfieUrl"))
                                        ? reader.GetString(reader.GetOrdinal("ClockOutSelfieUrl")) : null;

                results.Add(new {
                    id,
                    clockIn,
                    clockOut,
                    workedHours       = workedHrs,
                    isManual,
                    isHourEntry,
                    isBreakEntry,
                    clockInSelfieUrl  = selfieIn,
                    clockOutSelfieUrl = selfieOut
                });
            }
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
        return results;
    }

    /// <summary>
    /// Payload for the POST api/Attendance/clockin endpoint.
    /// SelfieBase64: optional (or required when RequireSelfie policy is on).
    ///   Accepts a raw base64 string or a data-URI like "data:image/jpeg;base64,..."
    /// </summary>
    public class ClockInRequest
    {
        public int     EmployeeId     { get; set; }
        /// <summary>
        /// The organization context in which the employee is clocking in.
        /// Time entries are scoped per (EmployeeId, OrganizationId) so each org
        /// maintains its own independent clock-in/out history.
        /// </summary>
        public int?    OrganizationId { get; set; }
        /// <summary>Base64-encoded JPEG selfie captured by the browser camera at clock-in time.</summary>
        public string? SelfieBase64   { get; set; }
    }

    /// <summary>
    /// Payload for the POST api/Attendance/clockout endpoint.
    /// SelfieBase64: optional (or required when RequireSelfie policy is on).
    /// </summary>
    public class ClockOutRequest
    {
        public int     EmployeeId     { get; set; }
        /// <summary>
        /// The organization context in which the employee is clocking out.
        /// Must match the OrganizationId used when clocking in so the correct
        /// open entry is found and closed.
        /// </summary>
        public int?    OrganizationId { get; set; }
        /// <summary>Base64-encoded JPEG selfie captured by the browser camera at clock-out time.</summary>
        public string? SelfieBase64   { get; set; }
    }

    public class UpdateEntryRequest
    {
        public int ChangedByEmpId { get; set; }
        public string NewClockIn { get; set; } = "";
        public string? NewClockOut { get; set; }
        public string? ReasonForChange { get; set; }
        /// <summary>"in" = user edited clock-in, "out" = user edited clock-out</summary>
        public string ChangedField { get; set; } = "in";
    }

    public class ManualEntryRequest
    {
        public int    EmployeeId     { get; set; }
        public string EntryType      { get; set; } = "in";  // "in", "out", "hour"
        public string Date           { get; set; } = "";    // "yyyy-MM-dd"
        public string? Time          { get; set; }           // "HH:mm" for in/out
        public int?   HourH          { get; set; }
        public int?   HourM          { get; set; }
        public int?   OrganizationId { get; set; }
        /// <summary>ID of the person making the request (may differ from EmployeeId when admin adds for someone else).</summary>
        public int?   RequesterId    { get; set; }
        /// <summary>When true (admin only): bypass the live-session block and back-date the active session instead.</summary>
        public bool   IsAdminOverride { get; set; } = false;
    }


        // ─────────────────────────────────────────────────────────────────────
        // RecalcDayEntries
        // Called after any add / edit / delete so that WorkedHours on every
        // remaining entry for that employee+day is recomputed from scratch,
        // honouring the sort order of the surviving entries only.
        //
        // Algorithm:
        //  1. Fetch all non-deleted entries for the employee on targetDate,
        //     sorted chronologically by ClockIn (break entries excluded from calc).
        //  2. Pair consecutive In→Out entries to produce closed sessions.
        //  3. For each closed session: WorkedHours = ClockOut - ClockIn.
        //  4. For the earliest open (no ClockOut) entry: WorkedHours = null
        //     (duration is live — shown in UI as ongoing).
        //  5. SaveChanges so all callers immediately see fresh values.
        // ─────────────────────────────────────────────────────────────────────
        private async Task RecalcDayEntries(int employeeId, DateTime targetDate, int? organizationId)
        {
            try
            {
                var dayStart = targetDate.Date;
                var dayEnd   = dayStart.AddDays(1);

                // Load all surviving entries for this employee on this day, sorted by ClockIn.
                var entries = await _context.TimeEntries
                    .Where(e => e.EmployeeId == employeeId
                             && e.ClockIn >= dayStart
                             && e.ClockIn <  dayEnd
                             && !e.IsBreakEntry
                             && (organizationId == null || e.OrganizationId == organizationId || e.OrganizationId == null))
                    .OrderBy(e => e.ClockIn)
                    .ToListAsync();

                if (!entries.Any()) return;

                // Recalculate WorkedHours for every closed entry.
                // Hour entries already have their WorkedHours set correctly at creation time.
                // For time entries (IsManual, normal clock sessions): recompute from ClockIn/ClockOut.
                foreach (var entry in entries)
                {
                    if (entry.IsHourEntry)
                    {
                        // Hour entries: WorkedHours was set at creation, leave it.
                        continue;
                    }

                    if (entry.ClockOut.HasValue)
                    {
                        // Closed session — recompute duration from the actual times.
                        var dur = entry.ClockOut.Value - entry.ClockIn;
                        if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;
                        entry.WorkedHours = dur.TotalMinutes >= 60
                            ? $"{(int)dur.TotalHours}h {dur.Minutes}m"
                            : $"{(int)dur.TotalMinutes}m";
                    }
                    else
                    {
                        // Open / live session — no WorkedHours stored; UI computes it live.
                        entry.WorkedHours = null;
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Recalc is best-effort — log but don't fail the parent request.
                _logger.LogWarning(ex, "RecalcDayEntries failed for emp {EmpId} on {Date}", employeeId, targetDate);
            }
        }

} // end class AttendanceController
} // end namespace
