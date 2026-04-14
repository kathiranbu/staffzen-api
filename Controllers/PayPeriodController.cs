using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using APM.StaffZen.API.Data;

namespace APM.StaffZen.API.Controllers
{
    /// <summary>
    /// GET  api/organizations/{orgId}/pay-period   → returns the saved pay period, or 204 if none.
    /// PUT  api/organizations/{orgId}/pay-period   → creates or updates the pay period.
    /// </summary>
    [ApiController]
    [Route("api/organizations/{orgId:int}/pay-period")]
    public class PayPeriodController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PayPeriodController> _logger;

        public PayPeriodController(ApplicationDbContext context, ILogger<PayPeriodController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // ── GET ────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Get(int orgId)
        {
            try
            {
                var row = await _context.Database
                    .SqlQueryRaw<PayPeriodRow>(
                        "SELECT Id, OrganizationId, Name, Frequency, StartDow, FirstDay, SemiDay, StartDate " +
                        "FROM PayPeriodSettings WHERE OrganizationId = {0}", orgId)
                    .FirstOrDefaultAsync();

                if (row == null) return NoContent();   // 204 → Blazor service treats as "not configured yet"

                return Ok(new
                {
                    id             = row.Id,
                    organizationId = row.OrganizationId,
                    name           = row.Name,
                    frequency      = row.Frequency,
                    startDow       = row.StartDow,
                    firstDay       = row.FirstDay,
                    semiDay        = row.SemiDay,
                    startDate      = row.StartDate
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET pay-period failed for org {OrgId}", orgId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── PUT ────────────────────────────────────────────────────────────────
        [HttpPut]
        public async Task<IActionResult> Upsert(int orgId, [FromBody] PayPeriodDto dto)
        {
            if (dto == null) return BadRequest(new { error = "Body is required." });

            try
            {
                // Upsert: UPDATE if exists, INSERT otherwise
                var affected = await _context.Database.ExecuteSqlRawAsync(@"
                    IF EXISTS (SELECT 1 FROM PayPeriodSettings WHERE OrganizationId = {0})
                        UPDATE PayPeriodSettings
                        SET Name      = {1},
                            Frequency = {2},
                            StartDow  = {3},
                            FirstDay  = {4},
                            SemiDay   = {5},
                            StartDate = {6}
                        WHERE OrganizationId = {0}
                    ELSE
                        INSERT INTO PayPeriodSettings
                            (OrganizationId, Name, Frequency, StartDow, FirstDay, SemiDay, StartDate)
                        VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                    orgId,
                    dto.Name   ?? "Default",
                    dto.Frequency ?? "Monthly",
                    dto.StartDow,
                    dto.FirstDay,
                    dto.SemiDay,
                    dto.StartDate == default ? DateTime.Today : dto.StartDate);

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PUT pay-period failed for org {OrgId}", orgId);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class PayPeriodDto
    {
        public int      OrganizationId { get; set; }
        public string   Name           { get; set; } = "";
        public string   Frequency      { get; set; } = "Monthly";
        public int      StartDow       { get; set; } = 1;
        public int      FirstDay       { get; set; } = 1;
        public int      SemiDay        { get; set; } = 16;
        public DateTime StartDate      { get; set; } = DateTime.Today;
    }

    // Keyless entity used for raw SQL projection
    public class PayPeriodRow
    {
        public int      Id             { get; set; }
        public int      OrganizationId { get; set; }
        public string   Name           { get; set; } = "";
        public string   Frequency      { get; set; } = "Monthly";
        public int      StartDow       { get; set; }
        public int      FirstDay       { get; set; }
        public int      SemiDay        { get; set; }
        public DateTime StartDate      { get; set; }
    }
}
