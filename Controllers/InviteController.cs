using APM.StaffZen.API.Data;
using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Models;
using APM.StaffZen.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/invite")]
    public class InviteController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly PasswordService _passwordService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InviteController> _logger;

        public InviteController(
            ApplicationDbContext context,
            EmailService emailService,
            PasswordService passwordService,
            IConfiguration configuration,
            ILogger<InviteController> logger)
        {
            _context = context;
            _emailService = emailService;
            _passwordService = passwordService;
            _configuration = configuration;
            _logger = logger;
        }

        // 1️⃣ Admin sends invite
        [HttpPost]
        public async Task<IActionResult> SendInvite([FromBody] InviteRequestDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.FullName))
                    return BadRequest(new { message = "Email and Full Name are required." });

                if (!dto.OrganizationId.HasValue || dto.OrganizationId <= 0)
                    return BadRequest(new { message = "OrganizationId is required. Please select an organization before inviting members." });

                var org = await _context.Organizations.FindAsync(dto.OrganizationId);
                if (org == null)
                    return NotFound(new { message = "Organization not found." });

                // Check if already an active member of THIS org
                var existingMember = await _context.OrganizationMembers
                    .Include(m => m.Employee)
                    .FirstOrDefaultAsync(m => m.OrganizationId == dto.OrganizationId
                                           && m.Employee.Email.ToLower() == dto.Email.ToLower()
                                           && m.IsActive);

                if (existingMember != null)
                    return BadRequest(new { message = "This person is already a member of this organization." });

                var token = Guid.NewGuid().ToString("N");

                var invite = new EmployeeInvite
                {
                    Email          = dto.Email,
                    Role           = dto.Role,
                    Token          = token,
                    ExpiryDate     = DateTime.UtcNow.AddDays(3),
                    IsUsed         = false,
                    OrganizationId = dto.OrganizationId!.Value,
                    OrgRole        = dto.OrgRole
                };
                _context.EmployeeInvites.Add(invite);

                // If employee doesn't exist at all, create a pending record
                var existingEmployee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email.ToLower() == dto.Email.ToLower());

                if (existingEmployee == null)
                {
                    var employee = new Employee
                    {
                        FullName     = dto.FullName,
                        Email        = dto.Email,
                        MobileNumber = dto.PhoneNumber,
                        CountryCode  = dto.CountryCode,
                        Role         = dto.Role,
                        GroupId      = dto.GroupId,
                        IsActive     = false
                    };
                    _context.Employees.Add(employee);
                    await _context.SaveChangesAsync(); // save first to get employee.Id

                    // Add pending OrganizationMember so they appear in the members list
                    _context.OrganizationMembers.Add(new OrganizationMember
                    {
                        OrganizationId = dto.OrganizationId!.Value,
                        EmployeeId     = employee.Id,
                        OrgRole        = dto.OrgRole ?? dto.Role ?? "Employee",
                        IsActive       = false   // pending until onboarding complete
                    });
                    _logger.LogInformation("Created pending employee + org membership for: {Email}", dto.Email);
                }
                else
                {
                    // Update the existing employee's profile details
                    existingEmployee.FullName     = dto.FullName;
                    existingEmployee.MobileNumber = dto.PhoneNumber;
                    existingEmployee.CountryCode  = dto.CountryCode;
                    if (dto.GroupId.HasValue) existingEmployee.GroupId = dto.GroupId;
                    // Do NOT overwrite Role here — it belongs to the employee globally;
                    // OrgRole (per-org) is stored on the OrganizationMember row.

                    // Ensure a pending OrganizationMember row exists for THIS org.
                    // The employee may already belong to other orgs — that is fine.
                    var existingOrgMember = await _context.OrganizationMembers
                        .FirstOrDefaultAsync(m => m.OrganizationId == dto.OrganizationId!.Value
                                               && m.EmployeeId == existingEmployee.Id);

                    if (existingOrgMember == null)
                    {
                        // No row for this org at all — create a pending one
                        _context.OrganizationMembers.Add(new OrganizationMember
                        {
                            OrganizationId = dto.OrganizationId!.Value,
                            EmployeeId     = existingEmployee.Id,
                            OrgRole        = dto.OrgRole ?? dto.Role ?? "Employee",
                            IsActive       = false
                        });
                    }
                    else if (!existingOrgMember.IsActive)
                    {
                        // Previously removed or expired invite — reset to pending with updated role
                        existingOrgMember.OrgRole  = dto.OrgRole ?? dto.Role ?? existingOrgMember.OrgRole;
                        existingOrgMember.IsActive = false; // stays pending until onboarding completes
                    }
                    // else: already an active member — the check above (line 51) would
                    // have already returned 400, so this branch is unreachable.
                }

                await _context.SaveChangesAsync();

                var baseUrl = _configuration["AppSettings:BlazorBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var inviteLink = $"{baseUrl}/onboarding?token={Uri.EscapeDataString(token)}";

                var emailSent = await _emailService.SendInviteEmail(dto.Email, dto.FullName, inviteLink, org.Name);

                if (!emailSent)
                {
                    return Ok(new
                    {
                        message = "Invite created, but email could not be sent.",
                        emailSent = false,
                        inviteLink
                    });
                }

                return Ok(new { message = "Invite sent successfully", emailSent = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending invite for {Email}", dto.Email);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // 2️⃣ Validate invite link
        [HttpGet("validate")]
        public async Task<IActionResult> ValidateInvite(string token)
        {
            try
            {
                var invite = await _context.EmployeeInvites
                    .FirstOrDefaultAsync(x => x.Token == token && !x.IsUsed);

                if (invite == null || invite.ExpiryDate < DateTime.UtcNow)
                    return BadRequest("Invalid or expired invite");

                // Find the employee regardless of active status —
                // cross-org invitees are already active in another org.
                var employee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email == invite.Email);

                // Get org name for display
                var orgName = invite.OrganizationId > 0
                    ? (await _context.Organizations.FindAsync(invite.OrganizationId))?.Name
                    : null;

                return Ok(new
                {
                    invite.Email,
                    invite.Role,
                    FullName        = employee?.FullName,
                    MobileNumber    = employee?.MobileNumber,
                    CountryCode     = employee?.CountryCode,
                    OrgName         = orgName,
                    invite.OrganizationId,
                    invite.OrgRole,
                    IsExistingUser  = employee?.IsActive ?? false   // tells frontend to show join-only form
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating invite token");
                return StatusCode(500, "Error validating invite");
            }
        }

        // 3️⃣ Complete onboarding — activates user AND links them to org
        [HttpPost("complete")]
        public async Task<IActionResult> CompleteInvite([FromBody] CompleteInviteDto dto)
        {
            try
            {
                var invite = await _context.EmployeeInvites
                    .FirstOrDefaultAsync(x => x.Token == dto.Token && !x.IsUsed);

                if (invite == null)
                    return BadRequest("Invalid invite");

                // Find employee by email — works for both new users (IsActive=false)
                // and cross-org invites where the person is already active in another org.
                var employee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email == invite.Email);

                if (employee == null)
                    return BadRequest("Employee record not found");

                var imageUrl = await SaveProfileImage(dto.ProfileImageBase64);

                // Only overwrite profile fields if not already onboarded
                // (cross-org invites: don't overwrite the existing profile)
                if (!employee.IsOnboarded)
                {
                    employee.FullName        = dto.FullName;
                    employee.DateOfBirth     = dto.DateOfBirth;
                    employee.MobileNumber    = dto.MobileNumber;
                    employee.ProfileImageUrl = imageUrl ?? employee.ProfileImageUrl;
                }
                employee.IsActive    = true;
                employee.IsOnboarded = true;

                // Link employee to the organization they were invited to
                if (invite.OrganizationId > 0)
                {
                    // Only set legacy OrganizationId if this is a brand-new user
                    // (cross-org invitees already have a primary org — don't overwrite it)
                    if (!employee.IsOnboarded)
                        employee.OrganizationId = invite.OrganizationId;

                    // Activate the pending OrganizationMember row created during invite,
                    // or create a new active row if one doesn't exist yet.
                    var existingMembership = await _context.OrganizationMembers
                        .FirstOrDefaultAsync(m => m.OrganizationId == invite.OrganizationId
                                               && m.EmployeeId == employee.Id);
                    if (existingMembership != null)
                    {
                        // Pending row was created during invite — activate it now
                        existingMembership.IsActive = true;
                        existingMembership.JoinedAt = DateTime.UtcNow;
                        existingMembership.OrgRole  = invite.OrgRole ?? existingMembership.OrgRole;
                    }
                    else
                    {
                        _context.OrganizationMembers.Add(new OrganizationMember
                        {
                            OrganizationId = invite.OrganizationId,
                            EmployeeId     = employee.Id,
                            OrgRole        = invite.OrgRole,
                            JoinedAt       = DateTime.UtcNow,
                            IsActive       = true
                        });
                    }
                }

                invite.IsUsed = true;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Employee {Email} onboarded and linked to org {OrgId}",
                    invite.Email, invite.OrganizationId);

                return Ok(new
                {
                    message        = "Employee onboarded successfully",
                    employeeId     = employee.Id,
                    organizationId = invite.OrganizationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing invite");
                return StatusCode(500, "An error occurred while completing onboarding");
            }
        }

        [HttpPost("set-password")]
        public async Task<IActionResult> SetPassword(SetPasswordDto dto)
        {
            var invite = await _context.EmployeeInvites
                .FirstOrDefaultAsync(x => x.Token == dto.Token && x.IsUsed);
            if (invite == null) return BadRequest("Invalid token");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email == invite.Email);
            if (employee == null) return BadRequest("Employee not found");

            // Cross-org invitees already have a password — don't overwrite it.
            // Only set password for brand-new users who don't have one yet.
            if (!string.IsNullOrEmpty(employee.PasswordHash))
                return Ok("Password already set");

            if (string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Password is required");

            _passwordService.CreatePasswordHash(dto.Password, out string hash, out string salt);
            employee.PasswordHash = hash;
            employee.PasswordSalt = salt;
            await _context.SaveChangesAsync();
            return Ok("Password created successfully");
        }

        private async Task<string?> SaveProfileImage(string? base64Image)
        {
            if (string.IsNullOrEmpty(base64Image)) return null;
            var bytes    = Convert.FromBase64String(base64Image);
            var fileName = Guid.NewGuid() + ".png";
            var folder   = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
            return $"/uploads/{fileName}";
        }
    }
}
