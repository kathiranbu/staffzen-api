using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/leave-requests")]
    public class LeaveRequestsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<LeaveRequestsController> _log;

        public LeaveRequestsController(ApplicationDbContext db,
                                       ILogger<LeaveRequestsController> log)
        {
            _db  = db;
            _log = log;
        }

        // ── Internal notification helpers ─────────────────────────────────────
        private async Task SendLeaveAppliedNotificationsAsync(int employeeId, int leaveRequestId)
        {
            try
            {
                var member = await _db.OrganizationMembers
                    .Where(m => m.EmployeeId == employeeId && m.IsActive)
                    .OrderByDescending(m => m.JoinedAt)
                    .FirstOrDefaultAsync();

                if (member == null) return;

                var admins = await _db.OrganizationMembers
                    .Where(m => m.OrganizationId == member.OrganizationId
                             && m.IsActive
                             && (m.OrgRole == "Admin" || m.OrgRole == "Manager"
                              || m.OrgRole == "Team Lead")
                             && m.EmployeeId != employeeId)
                    .Select(m => m.EmployeeId)
                    .Distinct()
                    .ToListAsync();

                if (!admins.Any()) return;

                var employee = await _db.Employees.FindAsync(employeeId);
                if (employee == null) return;

                var now   = DateTime.UtcNow;
                var items = admins.Select(adminId => new Notification
                {
                    RecipientId = adminId,
                    Type        = "LeaveApplied",
                    Title       = "New Leave Request",
                    Message     = $"{employee.FullName} applied for leave",
                    ReferenceId = leaveRequestId,
                    IsRead      = false,
                    CreatedAt   = now
                }).ToList();

                _db.Notifications.AddRange(items);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SendLeaveAppliedNotifications failed (non-fatal)");
            }
        }

        private async Task SendLeaveForwardedToHRNotificationsAsync(int employeeId, int leaveRequestId)
        {
            try
            {
                var member = await _db.OrganizationMembers
                    .Where(m => m.EmployeeId == employeeId && m.IsActive)
                    .OrderByDescending(m => m.JoinedAt)
                    .FirstOrDefaultAsync();

                if (member == null) return;

                var hrMembers = await _db.OrganizationMembers
                    .Where(m => m.OrganizationId == member.OrganizationId
                             && m.IsActive
                             && (m.OrgRole == "HR" || m.OrgRole == "Admin")
                             && m.EmployeeId != employeeId)
                    .Select(m => m.EmployeeId)
                    .Distinct()
                    .ToListAsync();

                if (!hrMembers.Any()) return;

                var employee = await _db.Employees.FindAsync(employeeId);
                if (employee == null) return;

                var now   = DateTime.UtcNow;
                var items = hrMembers.Select(recipientId => new Notification
                {
                    RecipientId = recipientId,
                    Type        = "LeaveApplied",
                    Title       = "Leave Request Forwarded",
                    Message     = $"{employee.FullName}'s leave request has been forwarded by Team Lead",
                    ReferenceId = leaveRequestId,
                    IsRead      = false,
                    CreatedAt   = now
                }).ToList();

                _db.Notifications.AddRange(items);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SendLeaveForwardedToHRNotifications failed (non-fatal)");
            }
        }

        private async Task SendLeaveReviewedNotificationAsync(
            int employeeId, int leaveRequestId, string action)
        {
            try
            {
                var approved = action.Equals("approve",        StringComparison.OrdinalIgnoreCase)
                            || action.Equals("direct_approve", StringComparison.OrdinalIgnoreCase);

                _db.Notifications.Add(new Notification
                {
                    RecipientId = employeeId,
                    Type        = approved ? "LeaveApproved" : "LeaveRejected",
                    Title       = approved ? "Leave Approved" : "Leave Rejected",
                    Message     = approved
                                    ? "Your leave request has been approved"
                                    : "Your leave request has been rejected",
                    ReferenceId = leaveRequestId,
                    IsRead      = false,
                    CreatedAt   = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SendLeaveReviewedNotification failed (non-fatal)");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // GET api/leave-requests
        // ─────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? status,
            [FromQuery] int?    employeeId,
            [FromQuery] string? panel)
        {
            try
            {
                var q = _db.LeaveRequests
                           .Include(r => r.Employee)
                           .AsQueryable();

                if (employeeId.HasValue && employeeId.Value > 0)
                    q = q.Where(r => r.EmployeeId == employeeId.Value);

                if (!string.IsNullOrWhiteSpace(panel))
                {
                    if (panel.Equals("teamlead", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(r => r.Status == "Pending_TeamLead"
                                      || r.Status == "Rejected_TeamLead");
                    else if (panel.Equals("hr", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(r => r.Status == "Pending_HR"
                                      || r.Status == "Approved"
                                      || r.Status == "Rejected_HR");
                }
                else if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    q = q.Where(r => r.Status == status);
                }

                var list = await q.OrderByDescending(r => r.CreatedAt).ToListAsync();

                return Ok(list.Select(r => new
                {
                    r.Id,
                    r.EmployeeId,
                    employeeName  = r.Employee?.FullName ?? "",
                    r.LeaveTypeId,
                    leaveTypeName = r.LeaveTypeName ?? "",
                    startDate     = r.StartDate.ToString("yyyy-MM-dd"),
                    endDate       = r.EndDate.ToString("yyyy-MM-dd"),
                    r.Reason,
                    r.Status,
                    r.ReviewedBy,
                    r.RejectReason,
                    createdAt     = r.CreatedAt
                }));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error fetching leave requests");
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // GET api/leave-requests/{id}  — single record (used by notification click)
        // ─────────────────────────────────────────────────────────────────
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var r = await _db.LeaveRequests
                    .Include(x => x.Employee)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (r == null) return NotFound(new { message = "Leave request not found." });

                return Ok(new
                {
                    r.Id,
                    r.EmployeeId,
                    employeeName  = r.Employee?.FullName ?? "",
                    r.LeaveTypeId,
                    leaveTypeName = r.LeaveTypeName ?? "",
                    startDate     = r.StartDate.ToString("yyyy-MM-dd"),
                    endDate       = r.EndDate.ToString("yyyy-MM-dd"),
                    r.Reason,
                    r.Status,
                    r.ReviewedBy,
                    r.RejectReason,
                    createdAt     = r.CreatedAt
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error fetching leave request {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // POST api/leave-requests
        // ─────────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateLeaveRequestDto dto)
        {
            try
            {
                if (dto.EmployeeId <= 0)
                    return BadRequest(new { message = "EmployeeId is required." });

                if (!DateTime.TryParse(dto.StartDate, out var start))
                    return BadRequest(new { message = "StartDate is required." });

                if (!DateTime.TryParse(dto.EndDate, out var end))
                    return BadRequest(new { message = "EndDate is required." });

                if (end < start)
                    return BadRequest(new { message = "EndDate must be on or after StartDate." });

                var req = new LeaveRequest
                {
                    EmployeeId    = dto.EmployeeId,
                    LeaveTypeId   = dto.LeaveTypeId,
                    LeaveTypeName = dto.LeaveTypeName?.Trim(),
                    StartDate     = start,
                    EndDate       = end,
                    Reason        = dto.Reason,
                    Status        = "Pending_TeamLead",
                    CreatedAt     = DateTime.UtcNow
                };

                _db.LeaveRequests.Add(req);
                await _db.SaveChangesAsync();

                // Notify all admins of the org (non-fatal)
                await SendLeaveAppliedNotificationsAsync(dto.EmployeeId, req.Id);

                return Ok(new { id = req.Id, message = "Leave request submitted." });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error creating leave request");
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // PATCH api/leave-requests/{id}/review
        // ─────────────────────────────────────────────────────────────────
        [HttpPatch("{id}/review")]
        public async Task<IActionResult> Review(int id, [FromBody] ReviewLeaveRequestDto dto)
        {
            try
            {
                var req = await _db.LeaveRequests.FindAsync(id);
                if (req == null)
                    return NotFound(new { message = "Leave request not found." });

                var validReviewers = new[] { "TeamLead", "HR" };
                if (!validReviewers.Contains(dto.ReviewedBy))
                    return BadRequest(new { message = "ReviewedBy must be 'TeamLead' or 'HR'." });

                var validActions = new[] { "approve", "reject", "forward_to_hr", "direct_approve" };
                if (!validActions.Contains(dto.Action))
                    return BadRequest(new { message = "Invalid action." });

                bool notifyEmployee = false;

                if (dto.ReviewedBy == "TeamLead")
                {
                    if (req.Status != "Pending_TeamLead")
                        return BadRequest(new { message = "This request is not pending Team Lead review." });

                    req.ReviewedBy = "TeamLead";

                    switch (dto.Action)
                    {
                        case "forward_to_hr":
                            req.Status       = "Pending_HR";
                            req.RejectReason = null;
                            await SendLeaveForwardedToHRNotificationsAsync(req.EmployeeId, req.Id);
                            break;

                        case "direct_approve":
                        case "approve":
                            req.Status       = "Approved";
                            req.RejectReason = null;
                            notifyEmployee   = true;
                            break;

                        case "reject":
                            if (string.IsNullOrWhiteSpace(dto.RejectReason))
                                return BadRequest(new { message = "A rejection reason is required." });
                            req.Status       = "Rejected_TeamLead";
                            req.RejectReason = dto.RejectReason.Trim();
                            notifyEmployee   = true;
                            break;
                    }
                }
                else // HR
                {
                    if (req.Status != "Pending_HR")
                        return BadRequest(new { message = "This request is not pending HR review." });

                    req.ReviewedBy = "HR";

                    switch (dto.Action)
                    {
                        case "approve":
                        case "direct_approve":
                            req.Status       = "Approved";
                            req.RejectReason = null;
                            notifyEmployee   = true;
                            break;

                        case "reject":
                            if (string.IsNullOrWhiteSpace(dto.RejectReason))
                                return BadRequest(new { message = "A rejection reason is required." });
                            req.Status       = "Rejected_HR";
                            req.RejectReason = dto.RejectReason.Trim();
                            notifyEmployee   = true;
                            break;
                    }
                }

                await _db.SaveChangesAsync();

                // Notify the employee if the action was a final approve/reject
                if (notifyEmployee)
                    await SendLeaveReviewedNotificationAsync(req.EmployeeId, req.Id, dto.Action);

                return Ok(new { id = req.Id, status = req.Status, message = "Review recorded." });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error reviewing leave request {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DELETE api/leave-requests/{id}
        // ─────────────────────────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var req = await _db.LeaveRequests.FindAsync(id);
                if (req == null)
                    return NotFound(new { message = "Leave request not found." });

                _db.LeaveRequests.Remove(req);
                await _db.SaveChangesAsync();

                return Ok(new { message = "Deleted." });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error deleting leave request {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public class CreateLeaveRequestDto
    {
        public int     EmployeeId    { get; set; }
        public int?    LeaveTypeId   { get; set; }
        public string? LeaveTypeName { get; set; }
        public string  StartDate     { get; set; } = "";
        public string  EndDate       { get; set; } = "";
        public string? Reason        { get; set; }
    }

    public class ReviewLeaveRequestDto
    {
        public string  Action       { get; set; } = "";
        public string  ReviewedBy   { get; set; } = "";
        public string? RejectReason { get; set; }
    }
}
