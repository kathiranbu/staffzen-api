using APM.StaffZen.API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/employees/{employeeId}/fcm-token")]
    public class FcmTokenController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FcmTokenController> _logger;

        public FcmTokenController(ApplicationDbContext context, ILogger<FcmTokenController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        /// <summary>Save or update the FCM device token for push notifications.</summary>
        [HttpPut]
        public async Task<IActionResult> SaveToken(int employeeId, [FromBody] FcmTokenRequest req)
        {
            try
            {
                var emp = await _context.Employees.FindAsync(employeeId);
                if (emp == null) return NotFound(new { error = "Employee not found." });

                emp.FcmToken = req.Token;
                await _context.SaveChangesAsync();
                _logger.LogInformation("FCM token updated for employee {Id}", employeeId);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveFcmToken failed for employee {Id}", employeeId);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class FcmTokenRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
