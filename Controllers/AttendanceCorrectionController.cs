using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using APM.StaffZen.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/attendance-corrections")]
    public class AttendanceCorrectionController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AttendanceCorrectionController> _logger;
        private readonly EmailService   _emailService;
        private readonly FirebaseService _firebaseService;

        private static readonly TimeSpan _istOffset = new(5, 30, 0);
        private static DateTime ToIst(DateTime utc)    => utc + _istOffset;
        private static DateTime IstToUtc(DateTime ist) => ist - _istOffset;

        public AttendanceCorrectionController(
            ApplicationDbContext context,
            ILogger<AttendanceCorrectionController> logger,
            EmailService emailService,
            FirebaseService firebaseService)
        {
            _context         = context;
            _logger          = logger;
            _emailService    = emailService;
            _firebaseService = firebaseService;
        }

        // POST api/attendance-corrections
        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] SubmitCorrectionDto dto)
        {
            try
            {
                var entry = await _context.TimeEntries
                    .FirstOrDefaultAsync(t => t.Id == dto.TimeEntryId && t.EmployeeId == dto.EmployeeId);

                if (entry == null)
                    return NotFound(new { error = "Time entry not found." });
                if (entry.ClockOut != null)
                    return BadRequest(new { error = "This entry already has a clock-out time." });
                // ClockOut is null (active session) — allow correction.
                // This covers: status null/empty (just clocked in), "Present", "Late", "Unmarked", "Pending".

                var existing = await _context.AttendanceCorrectionRequests
                    .FirstOrDefaultAsync(r => r.TimeEntryId == dto.TimeEntryId && r.Status == "Pending");
                if (existing != null)
                    return BadRequest(new { error = "A correction request for this entry is already pending." });

                if (!TimeOnly.TryParse(dto.RequestedClockOutTime, out var clockOutTime))
                    return BadRequest(new { error = "Invalid time format. Use HH:mm." });

                var attendanceDate       = entry.AttendanceDate?.Date ?? entry.ClockIn.Date;
                var requestedClockOutIst = attendanceDate.Add(clockOutTime.ToTimeSpan());
                var requestedClockOutUtc = IstToUtc(requestedClockOutIst);

                // entry.ClockIn is stored as IST local (DateTimeKind.Unspecified),
                // so compare IST vs IST to avoid the 5:30 offset subtraction making
                // the requested clock-out appear earlier than clock-in.
                if (requestedClockOutIst <= entry.ClockIn)
                    return BadRequest(new { error = "Requested clock-out must be after clock-in." });

                var request = new AttendanceCorrectionRequest
                {
                    EmployeeId        = dto.EmployeeId,
                    OrganizationId    = entry.OrganizationId ?? 0,
                    TimeEntryId       = dto.TimeEntryId,
                    AttendanceDate    = attendanceDate,
                    RequestedClockOut = requestedClockOutUtc,
                    Reason            = dto.Reason?.Trim(),
                    Status            = "Pending",
                    CreatedAt         = DateTime.UtcNow
                };

                _context.AttendanceCorrectionRequests.Add(request);
                entry.AttendanceStatus = "Pending";
                await _context.SaveChangesAsync();

                _logger.LogInformation("Correction request #{Id} submitted by emp {Emp}.", request.Id, dto.EmployeeId);

                // ── Notify admins of the new correction request ──────────────
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var submitter = await _context.Employees.FindAsync(dto.EmployeeId);
                        if (submitter == null) return;

                        // Find all admins / owners in this org
                        var orgId = entry.OrganizationId ?? 0;
                        var adminMembers = await _context.OrganizationMembers
                            .Include(m => m.Employee)
                            .Where(m => m.OrganizationId == orgId &&
                                        m.IsActive &&
                                        (m.OrgRole == "Admin" || m.OrgRole == "Owner" || m.OrgRole == "Manager") &&
                                        m.Employee != null)
                            .ToListAsync();

                        string dateStr    = attendanceDate.ToString("dd MMM yyyy");
                        string clockInStr = entry.ClockIn.ToString("hh:mm tt");
                        string reqOutStr  = requestedClockOutIst.ToString("hh:mm tt");
                        string reason     = string.IsNullOrWhiteSpace(dto.Reason) ? "No reason provided." : dto.Reason;

                        string emailBody =
                            $"<b>{submitter.FullName}</b> has submitted a clock-out correction request.<br/><br/>" +
                            $"<b>Date:</b> {dateStr}<br/>" +
                            $"<b>Clock In:</b> {clockInStr}<br/>" +
                            $"<b>Requested Clock Out:</b> {reqOutStr}<br/>" +
                            $"<b>Reason:</b> {reason}<br/><br/>" +
                            "Please review this request in the <b>Attendance Dashboard</b>.";

                        string pushMsg = $"{submitter.FullName} submitted a correction request for {dateStr}. Please review it in the dashboard.";

                        foreach (var admin in adminMembers)
                        {
                            var emp = admin.Employee!;
                            var adminSettings = await _context.EmployeeNotificationSettings
                                .FirstOrDefaultAsync(s => s.EmployeeId == emp.Id);

                            // Email — always send unless explicitly disabled
                            if (adminSettings == null || adminSettings.RemindersChannelEmail == true)
                                await _emailService.SendGenericAsync(
                                    emp.Email, emp.FullName,
                                    $"⚠️ Correction Request: {submitter.FullName} — {dateStr}",
                                    emailBody);

                            // Push
                            if (adminSettings?.RemindersChannelPush == true && !string.IsNullOrEmpty(emp.FcmToken))
                                await _firebaseService.SendPushAsync(
                                    emp.FcmToken,
                                    "⚠️ New Correction Request",
                                    pushMsg);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to notify admins of correction request #{Id}", request.Id);
                    }
                });

                return Ok(new { requestId = request.Id, status = "Pending" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting correction");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET api/attendance-corrections/employee/{employeeId}
        [HttpGet("employee/{employeeId}")]
        public async Task<IActionResult> GetForEmployee(int employeeId, [FromQuery] int? organizationId = null)
        {
            try
            {
                var query = _context.AttendanceCorrectionRequests.Where(r => r.EmployeeId == employeeId);
                if (organizationId.HasValue && organizationId.Value > 0)
                    query = query.Where(r => r.OrganizationId == organizationId.Value);

                var requests = await query
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new CorrectionRequestDto
                    {
                        Id                = r.Id,
                        EmployeeId        = r.EmployeeId,
                        TimeEntryId       = r.TimeEntryId,
                        AttendanceDate    = r.AttendanceDate,
                        RequestedClockOut = ToIst(r.RequestedClockOut),
                        Reason            = r.Reason,
                        Status            = r.Status,
                        ApprovedClockOut  = r.ApprovedClockOut.HasValue ? ToIst(r.ApprovedClockOut.Value) : (DateTime?)null,
                        AdminNote         = r.AdminNote,
                        CreatedAt         = r.CreatedAt
                    })
                    .ToListAsync();

                return Ok(requests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetForEmployee failed for {EmpId}", employeeId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET api/attendance-corrections/pending?organizationId=
        [HttpGet("pending")]
        public async Task<IActionResult> GetPending([FromQuery] int organizationId)
        {
            try
            {
                if (organizationId <= 0) return BadRequest(new { error = "organizationId required." });

                var requests = await _context.AttendanceCorrectionRequests
                    .Include(r => r.Employee)
                    .Include(r => r.TimeEntry)
                    .Where(r => r.OrganizationId == organizationId && r.Status == "Pending")
                    .OrderBy(r => r.AttendanceDate)
                    .ToListAsync();

                var groupIds = requests
                    .Where(r => r.Employee?.GroupId != null)
                    .Select(r => r.Employee!.GroupId!.Value)
                    .Distinct().ToList();
                var groupNames = await _context.Groups
                    .Where(g => groupIds.Contains(g.Id))
                    .Select(g => new { g.Id, g.Name })
                    .ToDictionaryAsync(g => g.Id, g => g.Name);

                var result = requests.Select(r => new AdminCorrectionRequestDto
                {
                    Id                = r.Id,
                    EmployeeId        = r.EmployeeId,
                    EmployeeName      = r.Employee?.FullName ?? "",
                    EmployeeEmail     = r.Employee?.Email ?? "",
                    ProfileImageUrl   = r.Employee?.ProfileImageUrl,
                    GroupId           = r.Employee?.GroupId,
                    DepartmentName    = r.Employee?.GroupId != null && groupNames.TryGetValue(r.Employee.GroupId.Value, out var gn) ? gn : null,
                    TimeEntryId       = r.TimeEntryId,
                    AttendanceDate    = r.AttendanceDate,
                    ClockIn           = r.TimeEntry != null ? r.TimeEntry.ClockIn : (DateTime?)null,
                    RequestedClockOut = ToIst(r.RequestedClockOut),
                    Reason            = r.Reason,
                    Status            = r.Status,
                    CreatedAt         = r.CreatedAt
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPending failed for org {OrgId}", organizationId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET api/attendance-corrections/all?organizationId=&status=
        [HttpGet("all")]
        public async Task<IActionResult> GetAll([FromQuery] int organizationId, [FromQuery] string? status = null)
        {
            try
            {
                if (organizationId <= 0) return BadRequest(new { error = "organizationId required." });

                var query = _context.AttendanceCorrectionRequests
                    .Include(r => r.Employee)
                    .Include(r => r.TimeEntry)
                    .Where(r => r.OrganizationId == organizationId);

                if (!string.IsNullOrWhiteSpace(status))
                    query = query.Where(r => r.Status == status);

                var requests = await query.OrderByDescending(r => r.AttendanceDate).ToListAsync();

                var groupIds = requests
                    .Where(r => r.Employee?.GroupId != null)
                    .Select(r => r.Employee!.GroupId!.Value)
                    .Distinct().ToList();
                var groupNames = await _context.Groups
                    .Where(g => groupIds.Contains(g.Id))
                    .Select(g => new { g.Id, g.Name })
                    .ToDictionaryAsync(g => g.Id, g => g.Name);

                var result = requests.Select(r => new AdminCorrectionRequestDto
                {
                    Id                = r.Id,
                    EmployeeId        = r.EmployeeId,
                    EmployeeName      = r.Employee?.FullName ?? "",
                    EmployeeEmail     = r.Employee?.Email ?? "",
                    ProfileImageUrl   = r.Employee?.ProfileImageUrl,
                    GroupId           = r.Employee?.GroupId,
                    DepartmentName    = r.Employee?.GroupId != null && groupNames.TryGetValue(r.Employee.GroupId.Value, out var gn) ? gn : null,
                    TimeEntryId       = r.TimeEntryId,
                    AttendanceDate    = r.AttendanceDate,
                    ClockIn           = r.TimeEntry != null ? r.TimeEntry.ClockIn : (DateTime?)null,
                    RequestedClockOut = ToIst(r.RequestedClockOut),
                    Reason            = r.Reason,
                    Status            = r.Status,
                    AdminNote         = r.AdminNote,
                    ReviewedAt        = r.ReviewedAt,
                    CreatedAt         = r.CreatedAt
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAll failed for org {OrgId}", organizationId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // PUT api/attendance-corrections/{id}/approve
        [HttpPut("{id}/approve")]
        public async Task<IActionResult> Approve(int id, [FromBody] ApproveDto dto)
        {
            try
            {
                var request = await _context.AttendanceCorrectionRequests
                    .Include(r => r.TimeEntry)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (request == null) return NotFound(new { error = "Correction request not found." });
                if (request.Status != "Pending") return BadRequest(new { error = $"Request is already {request.Status}." });

                var entry = request.TimeEntry;
                if (entry == null) return NotFound(new { error = "Associated time entry not found." });

                // ── Admin can override final status to Absent ────────────────
                // If MarkAsAbsent is true, we skip the clock-out update entirely
                // and just close the request as Absent.
                if (dto.MarkAsAbsent == true)
                {
                    entry.AttendanceStatus = "Absent";
                    // ClockOut stays null — the record has no valid clock-out
                    request.Status               = "Approved";
                    request.AdminNote            = dto.AdminNote?.Trim();
                    request.ReviewedByEmployeeId = dto.ReviewedByEmployeeId;
                    request.ReviewedAt           = DateTime.UtcNow;

                    await _context.SaveChangesAsync();

                    // Notify employee
                    _ = NotifyEmployeeAsync(request.EmployeeId, "Absent",
                        request.AttendanceDate, null, dto.AdminNote, isRejection: false);

                    _logger.LogInformation("Correction #{Id} approved as Absent by admin.", id);
                    return Ok(new
                    {
                        requestId        = request.Id,
                        status           = "Approved",
                        attendanceStatus = "Absent",
                        workedHours      = (string?)null
                    });
                }

                // ── Normal approval: set clock-out time ──────────────────────
                DateTime finalClockOutUtc;
                if (!string.IsNullOrWhiteSpace(dto.OverrideClockOutTime) &&
                    TimeOnly.TryParse(dto.OverrideClockOutTime, out var overrideTime))
                {
                    finalClockOutUtc = IstToUtc(request.AttendanceDate.Add(overrideTime.ToTimeSpan()));
                }
                else
                {
                    finalClockOutUtc = request.RequestedClockOut;
                }

                // Compare IST vs IST — entry.ClockIn is stored as IST local (not UTC),
                // so we must not apply IstToUtc() to it before comparing.
                var finalClockOutIst = ToIst(finalClockOutUtc);
                if (finalClockOutIst <= entry.ClockIn)
                    return BadRequest(new { error = "Approved clock-out must be after clock-in." });
                entry.ClockOut = finalClockOutIst;
                var worked = finalClockOutIst - entry.ClockIn;
                int hrs  = (int)worked.TotalHours;
                int mins = worked.Minutes;
                entry.WorkedHours = mins > 0 ? $"{hrs} hrs {mins} mins" : $"{hrs} hrs";

                // ── Recalculate Present / Late using shared helper ───────────
                // Admin can force a specific status ("Present" or "Late") instead of auto-calculating
                string recalcStatus;
                if (!string.IsNullOrWhiteSpace(dto.ForceStatus) &&
                    (dto.ForceStatus == "Present" || dto.ForceStatus == "Late"))
                {
                    recalcStatus = dto.ForceStatus;
                }
                else
                {
                    try
                    {
                        var emp          = await _context.Employees.FindAsync(request.EmployeeId);
                        var allSchedules = await _context.WorkSchedules.ToListAsync();
                        var schedule     = AttendanceStatusHelper.ResolveSchedule(emp?.WorkSchedule, allSchedules);
                        recalcStatus     = AttendanceStatusHelper.ComputeClockInStatus(entry.ClockIn, schedule);
                    }
                    catch { recalcStatus = "Present"; }
                }

                entry.AttendanceStatus = recalcStatus;

                request.Status               = "Approved";
                request.ApprovedClockOut     = finalClockOutUtc;
                request.AdminNote            = dto.AdminNote?.Trim();
                request.ReviewedByEmployeeId = dto.ReviewedByEmployeeId;
                request.ReviewedAt           = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // ── Send acknowledgement to employee ────────────────────────
                _ = NotifyEmployeeAsync(request.EmployeeId, recalcStatus,
                    request.AttendanceDate, ToIst(finalClockOutUtc), dto.AdminNote, isRejection: false);

                _logger.LogInformation("Correction #{Id} approved, ClockOut={Co}, Status={S}", id, finalClockOutUtc, recalcStatus);
                return Ok(new
                {
                    requestId        = request.Id,
                    status           = "Approved",
                    approvedClockOut = ToIst(finalClockOutUtc).ToString("HH:mm"),
                    attendanceStatus = entry.AttendanceStatus,
                    workedHours      = entry.WorkedHours
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approve failed for #{Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // PUT api/attendance-corrections/{id}/reject
        [HttpPut("{id}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] RejectDto dto)
        {
            try
            {
                var request = await _context.AttendanceCorrectionRequests
                    .Include(r => r.TimeEntry)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (request == null) return NotFound(new { error = "Correction request not found." });
                if (request.Status != "Pending") return BadRequest(new { error = $"Request is already {request.Status}." });

                request.Status               = "Rejected";
                request.AdminNote            = dto.AdminNote?.Trim();
                request.ReviewedByEmployeeId = dto.ReviewedByEmployeeId;
                request.ReviewedAt           = DateTime.UtcNow;

                if (request.TimeEntry != null)
                    request.TimeEntry.AttendanceStatus = "Unmarked";

                await _context.SaveChangesAsync();

                // ── Send acknowledgement to employee ────────────────────────
                _ = NotifyEmployeeAsync(request.EmployeeId, "Rejected",
                    request.AttendanceDate, null, dto.AdminNote, isRejection: true);

                _logger.LogInformation("Correction #{Id} rejected.", id);
                return Ok(new { requestId = request.Id, status = "Rejected" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reject failed for #{Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── Internal: send email + push to employee after admin decision ──
        private Task NotifyEmployeeAsync(
            int employeeId, string finalStatus, DateTime attendanceDate,
            DateTime? approvedClockOut, string? adminNote, bool isRejection)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var emp      = await _context.Employees.FindAsync(employeeId);
                    var settings = await _context.EmployeeNotificationSettings
                        .FirstOrDefaultAsync(s => s.EmployeeId == employeeId);
                    if (emp == null) return;

                    string dateStr   = attendanceDate.ToString("dd MMM yyyy");
                    string subjectSuffix = isRejection ? "Rejected" : $"Approved — {finalStatus}";
                    string subject   = $"Attendance Correction Request {subjectSuffix}";

                    string bodyLines;
                    if (isRejection)
                    {
                        bodyLines =
                            $"Your attendance correction request for <b>{dateStr}</b> has been <b>rejected</b>.<br/><br/>" +
                            (string.IsNullOrWhiteSpace(adminNote)
                                ? "No additional note was provided by the admin."
                                : $"Admin note: <em>{adminNote}</em>");
                    }
                    else if (finalStatus == "Absent")
                    {
                        bodyLines =
                            $"Your attendance correction request for <b>{dateStr}</b> has been reviewed.<br/><br/>" +
                            $"Your attendance has been marked as <b>Absent</b> for this day.<br/><br/>" +
                            (string.IsNullOrWhiteSpace(adminNote)
                                ? ""
                                : $"Admin note: <em>{adminNote}</em>");
                    }
                    else
                    {
                        string clockOutStr = approvedClockOut.HasValue
                            ? approvedClockOut.Value.ToString("hh:mm tt") : "–";
                        bodyLines =
                            $"Your attendance correction request for <b>{dateStr}</b> has been <b>approved</b>.<br/><br/>" +
                            $"Your attendance has been updated to: <b>{finalStatus}</b><br/>" +
                            $"Approved clock-out time: <b>{clockOutStr}</b><br/><br/>" +
                            (string.IsNullOrWhiteSpace(adminNote)
                                ? ""
                                : $"Admin note: <em>{adminNote}</em>");
                    }

                    // Email
                    if (settings == null || settings.RemindersChannelEmail == true)
                        await _emailService.SendGenericAsync(emp.Email, emp.FullName, subject, bodyLines);

                    // Push
                    string pushTitle = isRejection
                        ? "Correction Request Rejected ❌"
                        : finalStatus == "Absent"
                            ? "Attendance Marked Absent 📋"
                            : $"Correction Approved — {finalStatus} ✅";

                    string pushBody = isRejection
                        ? $"Your correction request for {dateStr} was rejected."
                        : finalStatus == "Absent"
                            ? $"Your attendance for {dateStr} is marked Absent."
                            : $"Your attendance for {dateStr} is now {finalStatus}.";

                    if (settings?.RemindersChannelPush == true && !string.IsNullOrEmpty(emp.FcmToken))
                        await _firebaseService.SendPushAsync(emp.FcmToken, pushTitle, pushBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Correction acknowledgement notification failed for emp {Id}", employeeId);
                }
            });
        }

        // GET api/attendance-corrections/dashboard?organizationId=&date=&statusFilter=
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard(
            [FromQuery] int organizationId,
            [FromQuery] string date,
            [FromQuery] string? statusFilter = null)
        {
            try
            {
                if (organizationId <= 0) return BadRequest(new { error = "organizationId required." });
                if (!DateTime.TryParse(date, out var parsedDate)) return BadRequest(new { error = "Invalid date." });

                var dayStart = parsedDate.Date;
                var dayEnd   = dayStart.AddDays(1);

                var members = await _context.OrganizationMembers
                    .Include(m => m.Employee)
                    .Where(m => m.OrganizationId == organizationId && m.IsActive)
                    .Select(m => new
                    {
                        Id              = m.Employee.Id,
                        FullName        = m.Employee.FullName,
                        Email           = m.Employee.Email,
                        ProfileImageUrl = m.Employee.ProfileImageUrl,
                        GroupId         = m.Employee.GroupId,
                        MemberCode      = m.Employee.MemberCode
                    })
                    .ToListAsync();

                var empIds = members.Select(m => m.Id).ToList();

                var entries = await _context.TimeEntries
                    .Include(t => t.Employee)   // needed to read WorkSchedule for on-the-fly Unmarked detection
                    .Where(t => empIds.Contains(t.EmployeeId) &&
                                t.OrganizationId == organizationId &&
                                !t.IsManual && !t.IsHourEntry && !t.IsBreakEntry &&
                                t.ClockIn >= dayStart && t.ClockIn < dayEnd)
                    .OrderBy(t => t.ClockIn)
                    .ToListAsync();

                var groupIds = members.Where(m => m.GroupId.HasValue).Select(m => m.GroupId!.Value).Distinct().ToList();

                // Load groups with full policy info so we can derive warning windows on-the-fly
                var groups = await _context.Groups
                    .Where(g => groupIds.Contains(g.Id))
                    .ToListAsync();
                var groupNames     = groups.ToDictionary(g => g.Id, g => g.Name);
                var groupPolicyMap = groups.ToDictionary(g => g.Id, g => g);

                // Load default work schedule so we can compute shift end time for Unmarked detection
                var allSchedules      = await _context.WorkSchedules.ToListAsync();
                var defaultSchedule   = allSchedules.FirstOrDefault(s => s.IsDefault) ?? allSchedules.FirstOrDefault();
                var nowIst            = DateTime.Now; // server runs IST

                var pendingEmpIds = (await _context.AttendanceCorrectionRequests
                    .Where(r => r.OrganizationId == organizationId &&
                                r.AttendanceDate >= dayStart && r.AttendanceDate < dayEnd &&
                                r.Status == "Pending")
                    .Select(r => r.EmployeeId)
                    .ToListAsync()).ToHashSet();

                var allRows = members.Select(m =>
                {
                    var empEntries = entries.Where(t => t.EmployeeId == m.Id).ToList();
                    string status;
                    if (!empEntries.Any())
                        status = "Absent";
                    else
                    {
                        var first = empEntries.First();

                        // ── Derive status ────────────────────────────────────────────────────────
                        // Priority order:
                        //  1. Already explicitly stamped as Unmarked / Pending / Present → keep it.
                        //  2. ClockOut is null → check if the warning window has passed:
                        //     if yes → Unmarked (on-the-fly, regardless of whether the background
                        //     service has written it yet, and regardless of group policy type).
                        //     This is the fix: the count is always correct on any dashboard refresh.
                        //  3. ClockOut is null, warning window NOT passed → Active or Late.
                        //  4. ClockOut present but no status stamped → Present.

                        if (first.AttendanceStatus == "Unmarked" || first.AttendanceStatus == "Pending")
                        {
                            // Already marked — preserve as-is
                            status = first.AttendanceStatus;
                        }
                        else if (first.ClockOut == null)
                        {
                            // Session still open — compute shift end + warning window on-the-fly
                            var empSchedule = allSchedules.FirstOrDefault(s => s.Name == (first.Employee?.WorkSchedule ?? ""))
                                           ?? defaultSchedule;

                            // Get group warning window (default 30 min if not a ReminderBased group)
                            int warningAfterMins = 30;
                            if (m.GroupId.HasValue && groupPolicyMap.TryGetValue(m.GroupId.Value, out var grp)
                                && grp.WarningAfterEndMins > 0)
                                warningAfterMins = grp.WarningAfterEndMins;

                            var shiftEnd = GetDashboardShiftEndTime(empSchedule, first.ClockIn.DayOfWeek);

                            bool isUnmarked = false;
                            if (shiftEnd != TimeSpan.Zero)
                            {
                                var shiftEndDt  = first.ClockIn.Date + shiftEnd;
                                var unmarkedAt  = shiftEndDt.AddMinutes(warningAfterMins + 1);
                                isUnmarked = nowIst > unmarkedAt;
                            }

                            if (isUnmarked)
                                status = "Unmarked";
                            else if (first.AttendanceStatus == "Late")
                                status = "Late";   // clocked in late, still active
                            else if (!string.IsNullOrEmpty(first.AttendanceStatus))
                                status = first.AttendanceStatus;
                            else
                                status = "Active"; // still within shift / warning window
                        }
                        else
                        {
                            // Clocked out — use stamped status or default to Present
                            status = !string.IsNullOrEmpty(first.AttendanceStatus)
                                ? first.AttendanceStatus
                                : "Present";
                        }
                    }
                    return new DashboardRowDto
                    {
                        EmployeeId        = m.Id,
                        EmployeeName      = m.FullName,
                        Email             = m.Email,
                        ProfileImageUrl   = m.ProfileImageUrl,
                        MemberCode        = m.MemberCode,
                        DepartmentName    = m.GroupId.HasValue && groupNames.TryGetValue(m.GroupId.Value, out var gn) ? gn : null,
                        AttendanceStatus  = status,
                        ClockIn           = empEntries.FirstOrDefault()?.ClockIn is DateTime ci ? ci : (DateTime?)null,
                        ClockOut          = empEntries.FirstOrDefault()?.ClockOut is DateTime co ? co : (DateTime?)null,
                        WorkedHours       = empEntries.FirstOrDefault()?.WorkedHours,
                        HasPendingRequest = pendingEmpIds.Contains(m.Id)
                    };
                }).ToList();

                // Req 1 & 3: Late employees are also Present (clocked in, just late).
                //            Unmarked employees are also Present (they clocked in but forgot to clock out).
                // NOTE: Present count = everyone who physically clocked in (Present + Active + Unmarked + Pending).
                //       Late is counted separately (they are in both Present and Late counts).
                var present  = allRows.Count(r => r.AttendanceStatus == "Present"
                                               || r.AttendanceStatus == "Active"
                                               || r.AttendanceStatus == "Late"
                                               || r.AttendanceStatus == "Unmarked"
                                               || r.AttendanceStatus == "Pending");
                var late     = allRows.Count(r => r.AttendanceStatus == "Late");
                var absent   = allRows.Count(r => r.AttendanceStatus == "Absent");
                var unmarked = allRows.Count(r => r.AttendanceStatus == "Unmarked" || r.AttendanceStatus == "Pending");

                // Req 1 & 3: "Present" filter tab shows all employees who physically clocked in —
                //             including Late (clocked in after grace) and Unmarked (forgot to clock out).
                var filtered = (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
                    ? allRows.Where(r =>
                        statusFilter == "Unmarked"
                            ? r.AttendanceStatus == "Unmarked" || r.AttendanceStatus == "Pending"
                            : statusFilter == "Late"
                                ? r.AttendanceStatus == "Late"
                                : statusFilter == "Present"
                                    ? r.AttendanceStatus == "Present"
                                      || r.AttendanceStatus == "Active"
                                      || r.AttendanceStatus == "Late"
                                      || r.AttendanceStatus == "Unmarked"
                                      || r.AttendanceStatus == "Pending"
                                    : r.AttendanceStatus == statusFilter).ToList()
                    : allRows;

                return Ok(new
                {
                    date         = dayStart.ToString("yyyy-MM-dd"),
                    totalMembers = members.Count,
                    summary      = new { present, late, absent, unmarked },
                    employees    = filtered.OrderByDescending(r => r.AttendanceStatus == "Unmarked" || r.AttendanceStatus == "Pending")
                                          .ThenBy(r => r.EmployeeName)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard failed for org {OrgId}", organizationId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Returns the shift end TimeSpan for a given day from a work schedule.
        /// Used by the dashboard to detect Unmarked status on-the-fly without
        /// waiting for the background ReminderBasedClockOutService to write to the DB.
        /// </summary>
        private static TimeSpan GetDashboardShiftEndTime(WorkSchedule? schedule, DayOfWeek dow)
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
                if (!workDays.Contains(dayKey)) return TimeSpan.Zero; // rest day — never Unmarked
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

    // ── DTOs ──────────────────────────────────────────────────────────────

    public class SubmitCorrectionDto
    {
        public int     EmployeeId            { get; set; }
        public int     TimeEntryId           { get; set; }
        public string  RequestedClockOutTime { get; set; } = "";
        public string? Reason                { get; set; }
    }

    public class ApproveDto
    {
        public int     ReviewedByEmployeeId { get; set; }
        public string? OverrideClockOutTime { get; set; }
        public string? AdminNote            { get; set; }
        /// <summary>
        /// When true, the entry is marked Absent without setting a clock-out time.
        /// OverrideClockOutTime is ignored when this is true.
        /// </summary>
        public bool?   MarkAsAbsent         { get; set; }
        /// <summary>
        /// When set to "Present" or "Late", forces the attendance status to this value
        /// instead of auto-calculating based on clock-in time vs schedule.
        /// </summary>
        public string? ForceStatus          { get; set; }
    }

    public class RejectDto
    {
        public int     ReviewedByEmployeeId { get; set; }
        public string? AdminNote            { get; set; }
    }

    public class CorrectionRequestDto
    {
        public int       Id                { get; set; }
        public int       EmployeeId        { get; set; }
        public int       TimeEntryId       { get; set; }
        public DateTime  AttendanceDate    { get; set; }
        public DateTime  RequestedClockOut { get; set; }
        public string?   Reason           { get; set; }
        public string    Status           { get; set; } = "";
        public DateTime? ApprovedClockOut { get; set; }
        public string?   AdminNote        { get; set; }
        public DateTime  CreatedAt        { get; set; }
    }

    public class AdminCorrectionRequestDto
    {
        public int       Id                { get; set; }
        public int       EmployeeId        { get; set; }
        public string    EmployeeName      { get; set; } = "";
        public string    EmployeeEmail     { get; set; } = "";
        public string?   ProfileImageUrl   { get; set; }
        public int?      GroupId           { get; set; }
        public string?   DepartmentName    { get; set; }
        public int       TimeEntryId       { get; set; }
        public DateTime  AttendanceDate    { get; set; }
        public DateTime? ClockIn          { get; set; }
        public DateTime  RequestedClockOut { get; set; }
        public string?   Reason           { get; set; }
        public string    Status           { get; set; } = "";
        public string?   AdminNote        { get; set; }
        public DateTime? ReviewedAt       { get; set; }
        public DateTime  CreatedAt        { get; set; }
    }

    public class DashboardRowDto
    {
        public int       EmployeeId        { get; set; }
        public string    EmployeeName      { get; set; } = "";
        public string    Email             { get; set; } = "";
        public string?   ProfileImageUrl   { get; set; }
        public string?   MemberCode        { get; set; }
        public string?   DepartmentName    { get; set; }
        public string    AttendanceStatus  { get; set; } = "Absent";
        public DateTime? ClockIn          { get; set; }
        public DateTime? ClockOut         { get; set; }
        public string?   WorkedHours      { get; set; }
        public bool      HasPendingRequest { get; set; }
    }
}
