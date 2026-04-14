using APM.StaffZen.API.Data;
using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/time-off-policies")]
    public class TimeOffPoliciesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TimeOffPoliciesController> _logger;

        public TimeOffPoliciesController(ApplicationDbContext context, ILogger<TimeOffPoliciesController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // GET api/time-off-policies
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var policies     = await _context.TimeOffPolicies.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
                var assignments  = await _context.TimeOffPolicyAssignments.ToListAsync();

                // Build name lookups for summary text
                var empIds = assignments.Where(a => a.EmployeeId.HasValue).Select(a => a.EmployeeId!.Value).Distinct().ToList();
                var empMap = await _context.Employees
                    .Where(e => empIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => e.FullName);

                var grpIds = assignments.Where(a => a.GroupId.HasValue).Select(a => a.GroupId!.Value).Distinct().ToList();
                var grpMap = await _context.Groups
                    .Where(g => grpIds.Contains(g.Id))
                    .ToDictionaryAsync(g => g.Id, g => g.Name);

                var result = policies.Select(p =>
                {
                    var pa  = assignments.Where(a => a.PolicyId == p.Id).ToList();
                    var eIds = pa.Where(a => a.EmployeeId.HasValue).Select(a => a.EmployeeId!.Value).ToList();
                    var gIds = pa.Where(a => a.GroupId.HasValue).Select(a => a.GroupId!.Value).ToList();

                    string summary;
                    if (!eIds.Any() && !gIds.Any())
                    {
                        summary = "All Employees";
                    }
                    else
                    {
                        var parts = new List<string>();
                        if (gIds.Any())
                            parts.Add(gIds.Count == 1
                                ? grpMap.GetValueOrDefault(gIds[0], "1 Group")
                                : $"{gIds.Count} Groups");
                        if (eIds.Any())
                            parts.Add(eIds.Count == 1
                                ? empMap.GetValueOrDefault(eIds[0], "1 Member")
                                : $"{eIds.Count} Members");
                        summary = string.Join(", ", parts);
                    }

                    return ToDto(p, eIds, gIds, summary);
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving time off policies");
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // GET api/time-off-policies/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var p = await _context.TimeOffPolicies.FindAsync(id);
                if (p == null) return NotFound(new { message = "Policy not found." });

                var pa  = await _context.TimeOffPolicyAssignments.Where(a => a.PolicyId == id).ToListAsync();
                var eIds = pa.Where(a => a.EmployeeId.HasValue).Select(a => a.EmployeeId!.Value).ToList();
                var gIds = pa.Where(a => a.GroupId.HasValue).Select(a => a.GroupId!.Value).ToList();

                return Ok(ToDto(p, eIds, gIds, eIds.Any() || gIds.Any() ? "Custom" : "All Employees"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving policy {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // POST api/time-off-policies
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveTimeOffPolicyDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { message = "Policy name is required." });

                if (await _context.TimeOffPolicies.AnyAsync(p => p.Name.ToLower() == dto.Name.Trim().ToLower() && p.IsActive))
                    return BadRequest(new { message = "A policy with this name already exists." });

                var policy = MapFromDto(dto);
                _context.TimeOffPolicies.Add(policy);
                await _context.SaveChangesAsync();
                await SaveAssignments(policy.Id, dto.AssignedEmployeeIds, dto.AssignedGroupIds);

                return CreatedAtAction(nameof(GetById), new { id = policy.Id }, new { id = policy.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating time off policy");
                return StatusCode(500, new { message = "An error occurred while creating the policy." });
            }
        }

        // PUT api/time-off-policies/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveTimeOffPolicyDto dto)
        {
            try
            {
                var policy = await _context.TimeOffPolicies.FindAsync(id);
                if (policy == null) return NotFound(new { message = "Policy not found." });

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { message = "Policy name is required." });

                if (await _context.TimeOffPolicies.AnyAsync(p => p.Name.ToLower() == dto.Name.Trim().ToLower() && p.IsActive && p.Id != id))
                    return BadRequest(new { message = "A policy with this name already exists." });

                ApplyDto(policy, dto);
                await _context.SaveChangesAsync();

                // Replace all assignments
                _context.TimeOffPolicyAssignments.RemoveRange(
                    _context.TimeOffPolicyAssignments.Where(a => a.PolicyId == id));
                await _context.SaveChangesAsync();
                await SaveAssignments(id, dto.AssignedEmployeeIds, dto.AssignedGroupIds);

                return Ok(new { message = "Policy updated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating policy {Id}", id);
                return StatusCode(500, new { message = "An error occurred while updating the policy." });
            }
        }

        // DELETE api/time-off-policies/{id}  (soft delete)
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var policy = await _context.TimeOffPolicies.FindAsync(id);
                if (policy == null) return NotFound(new { message = "Policy not found." });

                policy.IsActive = false; // soft delete preserves history
                await _context.SaveChangesAsync();

                return Ok(new { message = "Policy deleted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting policy {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static TimeOffPolicyDto ToDto(TimeOffPolicy p, List<int> eIds, List<int> gIds, string summary) => new()
        {
            Id                       = p.Id,
            Name                     = p.Name,
            CompensationType         = p.CompensationType,
            Unit                     = p.Unit,
            AccrualType              = p.AccrualType,
            AnnualEntitlement        = p.AnnualEntitlement,
            ExcludePublicHolidays    = p.ExcludePublicHolidays,
            ExcludeNonWorkingDays    = p.ExcludeNonWorkingDays,
            AllowCarryForward        = p.AllowCarryForward,
            CarryForwardLimit        = p.CarryForwardLimit,
            CarryForwardExpiryMonths = p.CarryForwardExpiryMonths,
            IsActive                 = p.IsActive,
            CreatedAt                = p.CreatedAt,
            AssigneeSummary          = summary,
            AssignedEmployeeIds      = eIds,
            AssignedGroupIds         = gIds,
        };

        private static TimeOffPolicy MapFromDto(SaveTimeOffPolicyDto dto) => new()
        {
            Name                     = dto.Name.Trim(),
            CompensationType         = dto.CompensationType,
            Unit                     = dto.Unit,
            AccrualType              = dto.CompensationType == "Unpaid" ? "None" : dto.AccrualType,
            AnnualEntitlement        = dto.AnnualEntitlement,
            ExcludePublicHolidays    = dto.ExcludePublicHolidays,
            ExcludeNonWorkingDays    = dto.ExcludeNonWorkingDays,
            AllowCarryForward        = dto.AllowCarryForward,
            CarryForwardLimit        = dto.AllowCarryForward ? dto.CarryForwardLimit : null,
            CarryForwardExpiryMonths = dto.AllowCarryForward ? dto.CarryForwardExpiryMonths : null,
            IsActive                 = true,
            CreatedAt                = DateTime.UtcNow,
        };

        private static void ApplyDto(TimeOffPolicy policy, SaveTimeOffPolicyDto dto)
        {
            policy.Name                     = dto.Name.Trim();
            policy.CompensationType         = dto.CompensationType;
            policy.Unit                     = dto.Unit;
            policy.AccrualType              = dto.CompensationType == "Unpaid" ? "None" : dto.AccrualType;
            policy.AnnualEntitlement        = dto.AnnualEntitlement;
            policy.ExcludePublicHolidays    = dto.ExcludePublicHolidays;
            policy.ExcludeNonWorkingDays    = dto.ExcludeNonWorkingDays;
            policy.AllowCarryForward        = dto.AllowCarryForward;
            policy.CarryForwardLimit        = dto.AllowCarryForward ? dto.CarryForwardLimit : null;
            policy.CarryForwardExpiryMonths = dto.AllowCarryForward ? dto.CarryForwardExpiryMonths : null;
        }

        private async Task SaveAssignments(int policyId, List<int> empIds, List<int> grpIds)
        {
            var rows = new List<TimeOffPolicyAssignment>();
            foreach (var eid in empIds.Distinct())
                rows.Add(new TimeOffPolicyAssignment { PolicyId = policyId, EmployeeId = eid });
            foreach (var gid in grpIds.Distinct())
                rows.Add(new TimeOffPolicyAssignment { PolicyId = policyId, GroupId = gid });

            if (rows.Any())
            {
                _context.TimeOffPolicyAssignments.AddRange(rows);
                await _context.SaveChangesAsync();
            }
        }
    }
}
