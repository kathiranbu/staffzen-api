using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    /// <summary>
    /// Manages persistent in-app notifications.
    ///
    /// GET  api/notifications?recipientId=N          – list for one user (newest first)
    /// PATCH api/notifications/{id}/read             – mark single notification read
    /// PATCH api/notifications/mark-all-read?recipientId=N – mark all read
    /// DELETE api/notifications/{id}                 – dismiss one
    /// DELETE api/notifications?recipientId=N        – clear all for user
    /// POST api/notifications/leave-applied          – called by LeaveRequestsController after save
    /// POST api/notifications/leave-reviewed         – called after admin approve/reject
    /// </summary>
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<NotificationsController> _log;

        public NotificationsController(ApplicationDbContext db,
                                       ILogger<NotificationsController> log)
        {
            _db  = db;
            _log = log;
        }

        // ── GET: list for one recipient ───────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetForRecipient([FromQuery] int recipientId)
        {
            if (recipientId <= 0)
                return BadRequest(new { message = "recipientId is required." });

            var list = await _db.Notifications
                .Where(n => n.RecipientId == recipientId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new
                {
                    n.Id,
                    n.RecipientId,
                    n.Type,
                    n.Title,
                    n.Message,
                    n.ReferenceId,
                    n.IsRead,
                    n.CreatedAt
                })
                .ToListAsync();

            return Ok(list);
        }

        // ── PATCH /{id}/read ──────────────────────────────────────────────────
        [HttpPatch("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var n = await _db.Notifications.FindAsync(id);
            if (n == null) return NotFound();
            n.IsRead = true;
            await _db.SaveChangesAsync();
            return Ok(new { id = n.Id, isRead = true });
        }

        // ── PATCH /mark-all-read ──────────────────────────────────────────────
        [HttpPatch("mark-all-read")]
        public async Task<IActionResult> MarkAllRead([FromQuery] int recipientId)
        {
            var rows = await _db.Notifications
                .Where(n => n.RecipientId == recipientId && !n.IsRead)
                .ToListAsync();

            rows.ForEach(n => n.IsRead = true);
            await _db.SaveChangesAsync();
            return Ok(new { updated = rows.Count });
        }

        // ── DELETE /{id} ──────────────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOne(int id)
        {
            var n = await _db.Notifications.FindAsync(id);
            if (n == null) return NotFound();
            _db.Notifications.Remove(n);
            await _db.SaveChangesAsync();
            return Ok(new { deleted = true });
        }

        // ── DELETE all for recipient ──────────────────────────────────────────
        [HttpDelete]
        public async Task<IActionResult> DeleteAll([FromQuery] int recipientId)
        {
            var rows = await _db.Notifications
                .Where(n => n.RecipientId == recipientId)
                .ToListAsync();

            _db.Notifications.RemoveRange(rows);
            await _db.SaveChangesAsync();
            return Ok(new { deleted = rows.Count });
        }

        // ── POST /leave-applied ───────────────────────────────────────────────
        /// <summary>
        /// Called right after a leave request is saved.
        /// Fans out one notification to every Admin / Manager in the same org.
        /// </summary>
        [HttpPost("leave-applied")]
        public async Task<IActionResult> NotifyLeaveApplied(
            [FromBody] LeaveAppliedNotifyDto dto)
        {
            try
            {
                // Resolve submitter's name
                var employee = await _db.Employees.FindAsync(dto.EmployeeId);
                if (employee == null)
                    return BadRequest(new { message = "Employee not found." });

                // Find all Admin / Manager / Team Lead members of the same org
                // (HR is excluded until the Team Lead forwards the request)
                var admins = await _db.OrganizationMembers
                    .Where(m => m.OrganizationId == dto.OrganizationId
                             && m.IsActive
                             && (m.OrgRole == "Admin" || m.OrgRole == "Manager"
                              || m.OrgRole == "Team Lead")
                             && m.EmployeeId != dto.EmployeeId)
                    .Select(m => m.EmployeeId)
                    .Distinct()
                    .ToListAsync();

                if (!admins.Any())
                    return Ok(new { created = 0, message = "No admins to notify." });

                var now = DateTime.UtcNow;
                var items = admins.Select(adminId => new Notification
                {
                    RecipientId = adminId,
                    Type        = "LeaveApplied",
                    Title       = "New Leave Request",
                    Message     = $"{employee.FullName} applied for leave",
                    ReferenceId = dto.LeaveRequestId,
                    IsRead      = false,
                    CreatedAt   = now
                }).ToList();

                _db.Notifications.AddRange(items);
                await _db.SaveChangesAsync();

                return Ok(new { created = items.Count });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in NotifyLeaveApplied");
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ── POST /leave-reviewed ──────────────────────────────────────────────
        /// <summary>
        /// Called after an admin approves or rejects a leave request.
        /// Creates exactly one notification for the leave-request owner.
        /// </summary>
        [HttpPost("leave-reviewed")]
        public async Task<IActionResult> NotifyLeaveReviewed(
            [FromBody] LeaveReviewedNotifyDto dto)
        {
            try
            {
                var isApproved = dto.Action.Equals("approve", StringComparison.OrdinalIgnoreCase)
                              || dto.Action.Equals("direct_approve", StringComparison.OrdinalIgnoreCase);

                var title   = isApproved ? "Leave Approved" : "Leave Rejected";
                var message = isApproved
                    ? "Your leave request has been approved"
                    : "Your leave request has been rejected";

                var n = new Notification
                {
                    RecipientId = dto.EmployeeId,
                    Type        = isApproved ? "LeaveApproved" : "LeaveRejected",
                    Title       = title,
                    Message     = message,
                    ReferenceId = dto.LeaveRequestId,
                    IsRead      = false,
                    CreatedAt   = DateTime.UtcNow
                };

                _db.Notifications.Add(n);
                await _db.SaveChangesAsync();

                return Ok(new { created = 1 });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in NotifyLeaveReviewed");
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ── POST /clock-event ─────────────────────────────────────────────────
        /// <summary>
        /// Saves a clock-in, clock-out, break, or shift alert as a persistent
        /// bell notification for the employee so it appears in the bell panel.
        /// Called from the Blazor client immediately after a clock action succeeds.
        /// </summary>
        [HttpPost("clock-event")]
        public async Task<IActionResult> SaveClockEvent([FromBody] ClockEventNotifyDto dto)
        {
            try
            {
                if (dto.RecipientId <= 0)
                    return BadRequest(new { message = "RecipientId is required." });

                // Allowed types — guard against arbitrary strings
                var allowed = new[] { "ClockIn", "ClockOut", "BreakStart", "BreakEnd", "ShiftAlert" };
                if (!allowed.Contains(dto.Type))
                    return BadRequest(new { message = "Invalid notification type." });

                var n = new Notification
                {
                    RecipientId = dto.RecipientId,
                    Type        = dto.Type,
                    Title       = dto.Title?.Trim()   ?? dto.Type,
                    Message     = dto.Message?.Trim() ?? "",
                    ReferenceId = dto.ReferenceId,
                    IsRead      = false,
                    CreatedAt   = DateTime.UtcNow
                };

                _db.Notifications.Add(n);
                await _db.SaveChangesAsync();

                return Ok(new { id = n.Id });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in SaveClockEvent");
                return StatusCode(500, new { message = "An error occurred." });
            }
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class LeaveAppliedNotifyDto
    {
        public int EmployeeId     { get; set; }
        public int OrganizationId { get; set; }
        public int LeaveRequestId { get; set; }
    }

    public class LeaveReviewedNotifyDto
    {
        /// <summary>approve | direct_approve | reject</summary>
        public string Action        { get; set; } = "";
        public int    EmployeeId    { get; set; }   // leave-request owner
        public int    LeaveRequestId { get; set; }
    }

    public class ClockEventNotifyDto
    {
        public int    RecipientId { get; set; }
        /// <summary>ClockIn | ClockOut | BreakStart | BreakEnd | ShiftAlert</summary>
        public string Type        { get; set; } = "";
        public string Title       { get; set; } = "";
        public string Message     { get; set; } = "";
        public int    ReferenceId { get; set; } = 0;
    }
}
