using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/time-off-requests")]
    public class TimeOffRequestsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TimeOffRequestsController> _logger;

        public TimeOffRequestsController(ApplicationDbContext context, ILogger<TimeOffRequestsController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // GET api/time-off-requests?start=2026-03-01&end=2026-03-31
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? start, [FromQuery] string? end)
        {
            try
            {
                var query = _context.TimeOffRequests
                    .Include(r => r.Employee)
                    .Include(r => r.Policy)
                    .AsQueryable();

                if (DateTime.TryParse(start, out var s))
                    query = query.Where(r => r.StartDate >= s);

                if (DateTime.TryParse(end, out var e))
                    query = query.Where(r => r.StartDate <= e);

                var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

                var result = list.Select(r => new
                {
                    r.Id,
                    r.EmployeeId,
                    employeeName = r.Employee?.FullName ?? "",
                    r.PolicyId,
                    policyName   = r.Policy?.Name ?? "",
                    startDate    = r.StartDate.ToString("yyyy-MM-dd"),
                    endDate      = r.EndDate?.ToString("yyyy-MM-dd"),
                    r.IsHalfDay,
                    r.HalfDayPart,
                    r.Reason,
                    r.Status,
                    createdAt    = r.CreatedAt
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching time off requests");
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // POST api/time-off-requests
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTimeOffRequestDto dto)
        {
            try
            {
                if (dto.EmployeeId <= 0)
                    return BadRequest(new { message = "Employee is required." });
                if (dto.PolicyId <= 0)
                    return BadRequest(new { message = "Policy is required." });
                if (!DateTime.TryParse(dto.StartDate, out var startDate))
                    return BadRequest(new { message = "Start date is required." });

                DateTime? endDate = null;
                if (!string.IsNullOrWhiteSpace(dto.EndDate) && DateTime.TryParse(dto.EndDate, out var ed))
                    endDate = ed;

                var request = new TimeOffRequest
                {
                    EmployeeId  = dto.EmployeeId,
                    PolicyId    = dto.PolicyId,
                    StartDate   = startDate,
                    EndDate     = endDate,
                    IsHalfDay   = dto.IsHalfDay,
                    HalfDayPart = dto.HalfDayPart,
                    Reason      = dto.Reason,
                    Status      = "Pending",
                    CreatedAt   = DateTime.UtcNow
                };

                _context.TimeOffRequests.Add(request);
                await _context.SaveChangesAsync();

                return Ok(new { id = request.Id, message = "Time off request created." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating time off request");
                return StatusCode(500, new { message = "An error occurred while saving the request." });
            }
        }

        // PUT api/time-off-requests/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CreateTimeOffRequestDto dto)
        {
            try
            {
                var req = await _context.TimeOffRequests.FindAsync(id);
                if (req == null) return NotFound(new { message = "Request not found." });

                if (dto.EmployeeId <= 0)
                    return BadRequest(new { message = "Employee is required." });
                if (dto.PolicyId <= 0)
                    return BadRequest(new { message = "Policy is required." });
                if (!DateTime.TryParse(dto.StartDate, out var startDate))
                    return BadRequest(new { message = "Start date is required." });

                DateTime? endDate = null;
                if (!string.IsNullOrWhiteSpace(dto.EndDate) && DateTime.TryParse(dto.EndDate, out var ed))
                    endDate = ed;

                req.EmployeeId  = dto.EmployeeId;
                req.PolicyId    = dto.PolicyId;
                req.StartDate   = startDate;
                req.EndDate     = endDate;
                req.IsHalfDay   = dto.IsHalfDay;
                req.HalfDayPart = dto.HalfDayPart;
                req.Reason      = dto.Reason;

                await _context.SaveChangesAsync();

                return Ok(new { id = req.Id, message = "Time off request updated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating time off request {Id}", id);
                return StatusCode(500, new { message = "An error occurred while saving the request." });
            }
        }

        // PATCH api/time-off-requests/{id}/status
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            try
            {
                var req = await _context.TimeOffRequests.FindAsync(id);
                if (req == null) return NotFound(new { message = "Request not found." });

                if (dto.Status != "Approved" && dto.Status != "Rejected" && dto.Status != "Pending" && dto.Status != "Cancelled")
                    return BadRequest(new { message = "Invalid status." });

                req.Status = dto.Status;
                await _context.SaveChangesAsync();
                return Ok(new { message = "Status updated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for request {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // DELETE api/time-off-requests/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var req = await _context.TimeOffRequests.FindAsync(id);
                if (req == null) return NotFound(new { message = "Request not found." });
                _context.TimeOffRequests.Remove(req);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Deleted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting request {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }
    }

    public class CreateTimeOffRequestDto
    {
        public int     EmployeeId  { get; set; }
        public int     PolicyId    { get; set; }
        public string  StartDate   { get; set; } = "";
        public string? EndDate     { get; set; }
        public bool    IsHalfDay   { get; set; }
        public string? HalfDayPart { get; set; }
        public string? Reason      { get; set; }
    }

    public class UpdateStatusDto
    {
        public string Status { get; set; } = "";
    }
}
