using APM.StaffZen.API.Data;
using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/organizations")]
    public class OrganizationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<OrganizationController> _logger;

        public OrganizationController(ApplicationDbContext context,
                                      ILogger<OrganizationController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // ── POST api/organizations ─────────────────────────────────────────
        /// <summary>
        /// Creates a new Organization. The creator is auto-added as Admin member.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrganizationDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { error = "Organization name is required." });

            if (dto.EmployeeId <= 0)
                return BadRequest(new { error = "EmployeeId is required." });

            var employee = await _context.Employees.FindAsync(dto.EmployeeId);
            if (employee == null)
                return NotFound(new { error = "Employee not found." });

            var org = new Organization
            {
                Name             = dto.Name.Trim(),
                Country          = dto.Country,
                PhoneNumber      = dto.PhoneNumber,
                CountryCode      = dto.CountryCode,
                Industry         = dto.Industry,
                OrganizationSize = dto.OrganizationSize,
                OwnerRole        = dto.OwnerRole,
                EmployeeId       = dto.EmployeeId,
                CreatedAt        = DateTime.UtcNow
            };

            _context.Organizations.Add(org);
            await _context.SaveChangesAsync();

            // Link creator as Admin in OrganizationMembers
            var member = new OrganizationMember
            {
                OrganizationId = org.Id,
                EmployeeId     = dto.EmployeeId,
                OrgRole        = "Admin",
                JoinedAt       = DateTime.UtcNow,
                IsActive       = true
            };
            _context.OrganizationMembers.Add(member);

            // Also update legacy OrganizationId on Employee for backwards compatibility
            employee.OrganizationId = org.Id;
            employee.IsOnboarded    = true;
            employee.Role           = "Admin";

            await _context.SaveChangesAsync();

            _logger.LogInformation("Organization {OrgId} '{OrgName}' created by employee {EmpId}.",
                                   org.Id, org.Name, dto.EmployeeId);

            return Ok(MapToDto(org, new List<OrgMemberView>
            {
                new() { Id = employee.Id, FullName = employee.FullName, Email = employee.Email,
                        OrgRole = "Admin", IsActive = true, ProfileImageUrl = employee.ProfileImageUrl }
            }));
        }

        // ── GET api/organizations/{id} ─────────────────────────────────────
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var org = await _context.Organizations.FindAsync(id);
            if (org == null) return NotFound(new { error = "Organization not found." });

            var members = await GetOrgMembersView(id);
            return Ok(MapToDto(org, members));
        }

        // ── GET api/organizations ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var orgs = await _context.Organizations
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var result = new List<OrganizationDto>();
            foreach (var org in orgs)
                result.Add(MapToDto(org, await GetOrgMembersView(org.Id)));

            return Ok(result);
        }

        // ── GET api/organizations/by-employee/{employeeId} ─────────────────
        /// <summary>
        /// Returns all organizations that a given employee belongs to,
        /// along with their role in each.
        /// </summary>
        [HttpGet("by-employee/{employeeId:int}")]
        public async Task<IActionResult> GetByEmployee(int employeeId)
        {
            // Return both accepted (IsActive=true) and pending (IsActive=false) memberships
            // so the client can show the org list and pending invitations separately.
            var memberships = await _context.OrganizationMembers
                .Include(m => m.Organization)
                .Where(m => m.EmployeeId == employeeId)
                .Select(m => new
                {
                    organizationId   = m.OrganizationId,
                    organizationName = m.Organization.Name,
                    orgRole          = m.OrgRole,
                    joinedAt         = m.JoinedAt,
                    isOwner          = m.Organization.EmployeeId == employeeId,
                    isActive         = m.IsActive
                })
                .OrderBy(m => m.organizationName)
                .ToListAsync();

            return Ok(memberships);
        }

        // ── GET api/organizations/{id}/employees ───────────────────────────
        /// <summary>
        /// Returns all ACTIVE members of an organization (via OrganizationMembers join table).
        /// </summary>
        [HttpGet("{id:int}/employees")]
        public async Task<IActionResult> GetEmployees(int id)
        {
            var exists = await _context.Organizations.AnyAsync(o => o.Id == id);
            if (!exists) return NotFound(new { error = "Organization not found." });

            var members = await GetOrgMembersView(id);
            return Ok(members);
        }

        // ── PUT api/organizations/{id}/devices ────────────────────────────
        [HttpPut("{id:int}/devices")]
        public async Task<IActionResult> SaveDevices(int id, [FromBody] SaveDevicesDto dto)
        {
            var org = await _context.Organizations.FindAsync(id);
            if (org == null) return NotFound(new { error = "Organization not found." });

            org.SelectedDevices = dto.SelectedDevices;
            await _context.SaveChangesAsync();
            return Ok(new { organizationId = org.Id, selectedDevices = org.SelectedDevices });
        }

        // ── PUT api/organizations/{id} ─────────────────────────────────────
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CreateOrganizationDto dto)
        {
            var org = await _context.Organizations.FindAsync(id);
            if (org == null) return NotFound(new { error = "Organization not found." });

            if (!string.IsNullOrWhiteSpace(dto.Name))  org.Name             = dto.Name.Trim();
            if (dto.Country        != null)             org.Country          = dto.Country;
            if (dto.PhoneNumber    != null)             org.PhoneNumber      = dto.PhoneNumber;
            if (dto.CountryCode    != null)             org.CountryCode      = dto.CountryCode;
            if (dto.Industry       != null)             org.Industry         = dto.Industry;
            if (dto.OrganizationSize != null)           org.OrganizationSize = dto.OrganizationSize;
            if (dto.OwnerRole      != null)             org.OwnerRole        = dto.OwnerRole;

            await _context.SaveChangesAsync();
            return Ok(MapToDto(org, await GetOrgMembersView(id)));
        }

        // ── POST api/organizations/{id}/members ────────────────────────────
        /// <summary>
        /// Adds an existing employee (already in the system) to an organization.
        /// Used when re-assigning or manually adding members. 
        /// For new invitees, the InviteController handles this.
        /// </summary>
        [HttpPost("{id:int}/members")]
        public async Task<IActionResult> AddMember(int id, [FromBody] AddMemberDto dto)
        {
            var org = await _context.Organizations.FindAsync(id);
            if (org == null) return NotFound(new { error = "Organization not found." });

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email.ToLower() == dto.Email.ToLower() && e.IsActive);

            if (employee == null)
                return NotFound(new { error = "No active employee found with that email." });

            // Idempotent — if already a member, just update the role
            var existing = await _context.OrganizationMembers
                .FirstOrDefaultAsync(m => m.OrganizationId == id && m.EmployeeId == employee.Id);

            if (existing != null)
            {
                existing.OrgRole  = dto.OrgRole ?? existing.OrgRole;
                existing.IsActive = true;
            }
            else
            {
                _context.OrganizationMembers.Add(new OrganizationMember
                {
                    OrganizationId = id,
                    EmployeeId     = employee.Id,
                    OrgRole        = dto.OrgRole ?? "Employee",
                    JoinedAt       = DateTime.UtcNow,
                    IsActive       = true
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Member added.", employeeId = employee.Id, orgRole = dto.OrgRole ?? "Employee" });
        }

        // ── DELETE api/organizations/{id}/members/{employeeId} ─────────────
        /// <summary>Removes a member from an organization.</summary>
        [HttpDelete("{id:int}/members/{employeeId:int}")]
        public async Task<IActionResult> RemoveMember(int id, int employeeId)
        {
            var member = await _context.OrganizationMembers
                .FirstOrDefaultAsync(m => m.OrganizationId == id && m.EmployeeId == employeeId);

            if (member == null) return NotFound(new { error = "Member not found in this organization." });

            // Don't allow removing the owner
            var org = await _context.Organizations.FindAsync(id);
            if (org?.EmployeeId == employeeId)
                return BadRequest(new { error = "Cannot remove the organization owner." });

            if (member.IsActive)
            {
                // Active member — soft-delete: keep the row but mark inactive
                member.IsActive = false;
            }
            else
            {
                // Pending member (IsActive already false) — hard-delete the membership row
                // so they disappear from the list immediately.
                _context.OrganizationMembers.Remove(member);

                // Also clean up the Employee record if it was created solely for this invite
                // (i.e. never completed onboarding and has no other org memberships).
                var employee = await _context.Employees.FindAsync(employeeId);
                if (employee != null && !employee.IsActive)
                {
                    var otherMemberships = await _context.OrganizationMembers
                        .AnyAsync(m => m.EmployeeId == employeeId && m.OrganizationId != id);
                    if (!otherMemberships)
                    {
                        // Orphan pending employee — remove entirely so the email can be re-invited later
                        _context.Employees.Remove(employee);
                    }
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Member removed from organization." });
        }

        // ── PATCH api/organizations/{id}/members/{employeeId}/role ─────────
        /// <summary>Updates a member's role within a specific organization.</summary>
        [HttpPatch("{id:int}/members/{employeeId:int}/role")]
        public async Task<IActionResult> UpdateMemberRole(int id, int employeeId, [FromBody] UpdateMemberRoleDto dto)
        {
            var member = await _context.OrganizationMembers
                .FirstOrDefaultAsync(m => m.OrganizationId == id && m.EmployeeId == employeeId && m.IsActive);

            if (member == null) return NotFound(new { error = "Active member not found." });

            member.OrgRole = dto.OrgRole;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Role updated.", orgRole = member.OrgRole });
        }

        // ── GET api/organizations/{id}/time-tracking-policy ───────────────
        /// <summary>
        /// Returns the time-tracking policy for an organization.
        /// If no policy row exists yet, returns defaults (without creating a row).
        /// </summary>
        [HttpGet("{id:int}/time-tracking-policy")]
        public async Task<IActionResult> GetTimeTrackingPolicy(int id)
        {
            var policy = await _context.TimeTrackingPolicies
                .FirstOrDefaultAsync(p => p.OrganizationId == id);

            if (policy == null)
                return Ok(new TimeTrackingPolicyDto { OrganizationId = id });

            return Ok(MapPolicyToDto(policy));
        }

        // ── PUT api/organizations/{id}/time-tracking-policy ───────────────
        /// <summary>
        /// Creates or replaces the time-tracking policy for an organization.
        /// Upserts: if no row exists it is created; otherwise the existing row is updated.
        /// </summary>
        [HttpPut("{id:int}/time-tracking-policy")]
        public async Task<IActionResult> UpsertTimeTrackingPolicy(int id, [FromBody] TimeTrackingPolicyDto dto)
        {
            var org = await _context.Organizations.FindAsync(id);
            if (org == null) return NotFound(new { error = "Organization not found." });

            var policy = await _context.TimeTrackingPolicies
                .FirstOrDefaultAsync(p => p.OrganizationId == id);

            if (policy == null)
            {
                policy = new APM.StaffZen.API.Models.TimeTrackingPolicy { OrganizationId = id };
                _context.TimeTrackingPolicies.Add(policy);
            }

            policy.AutoClockOutEnabled       = dto.AutoClockOutEnabled;
            policy.AutoClockOutAfterDuration = dto.AutoClockOutAfterDuration;
            policy.AutoClockOutAfterHours    = dto.AutoClockOutAfterHours;
            policy.AutoClockOutAfterMins     = dto.AutoClockOutAfterMins;
            policy.AutoClockOutAtTime        = dto.AutoClockOutAtTime;
            policy.AutoClockOutTime          = dto.AutoClockOutTime ?? "23:00";

            await _context.SaveChangesAsync();
            _logger.LogInformation("TimeTrackingPolicy upserted for org {OrgId}.", id);
            return Ok(MapPolicyToDto(policy));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private async Task<List<OrgMemberView>> GetOrgMembersView(int orgId)
        {
            return await _context.OrganizationMembers
                .Include(m => m.Employee)
                .Where(m => m.OrganizationId == orgId && m.IsActive)
                .OrderByDescending(m => m.OrgRole == "Admin")
                .ThenBy(m => m.Employee.FullName)
                .Select(m => new OrgMemberView
                {
                    Id              = m.Employee.Id,
                    FullName        = m.Employee.FullName,
                    Email           = m.Employee.Email,
                    OrgRole         = m.OrgRole,
                    IsActive        = m.Employee.IsActive,
                    ProfileImageUrl = m.Employee.ProfileImageUrl
                })
                .ToListAsync();
        }

        private static TimeTrackingPolicyDto MapPolicyToDto(APM.StaffZen.API.Models.TimeTrackingPolicy p) =>
            new()
            {
                Id                       = p.Id,
                OrganizationId           = p.OrganizationId,
                AutoClockOutEnabled      = p.AutoClockOutEnabled,
                AutoClockOutAfterDuration = p.AutoClockOutAfterDuration,
                AutoClockOutAfterHours   = p.AutoClockOutAfterHours,
                AutoClockOutAfterMins    = p.AutoClockOutAfterMins,
                AutoClockOutAtTime       = p.AutoClockOutAtTime,
                AutoClockOutTime         = p.AutoClockOutTime
            };

        private static OrganizationDto MapToDto(Organization org, List<OrgMemberView> members) =>
            new()
            {
                Id               = org.Id,
                Name             = org.Name,
                Country          = org.Country,
                PhoneNumber      = org.PhoneNumber,
                CountryCode      = org.CountryCode,
                Industry         = org.Industry,
                OrganizationSize = org.OrganizationSize,
                OwnerRole        = org.OwnerRole,
                EmployeeId       = org.EmployeeId,
                CreatedAt        = org.CreatedAt,
                Employees        = members.Select(m => new OrgEmployeeSummaryDto
                {
                    Id              = m.Id,
                    FullName        = m.FullName,
                    Email           = m.Email,
                    Role            = m.OrgRole,
                    IsActive        = m.IsActive,
                    ProfileImageUrl = m.ProfileImageUrl
                }).ToList()
            };

        private class OrgMemberView
        {
            public int     Id              { get; set; }
            public string  FullName        { get; set; } = "";
            public string  Email           { get; set; } = "";
            public string  OrgRole         { get; set; } = "";
            public bool    IsActive        { get; set; }
            public string? ProfileImageUrl { get; set; }
        }
    }

    // ── Extra DTOs used only in this controller ──────────────────────────
    public class AddMemberDto
    {
        public string  Email   { get; set; } = "";
        public string? OrgRole { get; set; }
    }

    public class UpdateMemberRoleDto
    {
        public string OrgRole { get; set; } = "Employee";
    }

    public class TimeTrackingPolicyDto
    {
        public int    Id                        { get; set; }
        public int    OrganizationId            { get; set; }
        public bool   AutoClockOutEnabled       { get; set; } = false;
        public bool   AutoClockOutAfterDuration { get; set; } = false;
        public int    AutoClockOutAfterHours    { get; set; } = 8;
        public int    AutoClockOutAfterMins     { get; set; } = 0;
        public bool   AutoClockOutAtTime        { get; set; } = false;
        public string AutoClockOutTime          { get; set; } = "23:00";
    }
}
