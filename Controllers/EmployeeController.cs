using APM.StaffZen.API.Data;
using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/employees")]
    public class EmployeeController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EmployeeController> _logger;

        public EmployeeController(ApplicationDbContext context, ILogger<EmployeeController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get employees. If organizationId is supplied, only returns members of that org.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetEmployees([FromQuery] int? organizationId = null)
        {
            try
            {
                List<EmployeeListDto> employees;

                if (organizationId.HasValue && organizationId.Value > 0)
                {
                    // When filtering by org: join through OrganizationMembers so that
                    // IsActive reflects the membership status in THIS org, not the global
                    // Employee.IsActive flag. This means a person already active in another
                    // org will correctly show as "Pending" until they accept this org's invite.
                    employees = await _context.OrganizationMembers
                        .Where(m => m.OrganizationId == organizationId.Value)
                        .Include(m => m.Employee)
                        .OrderByDescending(m => m.IsActive)
                        .ThenBy(m => m.Employee.FullName)
                        .Select(m => new EmployeeListDto
                        {
                            Id              = m.Employee.Id,
                            FullName        = m.Employee.FullName,
                            Email           = m.Employee.Email,
                            Role            = m.Employee.Role,
                            MobileNumber    = m.Employee.MobileNumber,
                            CountryCode     = m.Employee.CountryCode,
                            ProfileImageUrl = m.Employee.ProfileImageUrl,
                            IsActive        = m.IsActive,   // org-level membership status
                            GroupId         = m.Employee.GroupId,
                            GroupName       = m.Employee.GroupId != null
                                ? _context.Groups.Where(g => g.Id == m.Employee.GroupId).Select(g => g.Name).FirstOrDefault()
                                : null,
                            HasFaceData     = m.Employee.FaceDescriptor != null
                        })
                        .ToListAsync();
                }
                else
                {
                    // No org filter — fall back to global employee list with Employee.IsActive
                    employees = await _context.Employees
                        .OrderByDescending(e => e.IsActive)
                        .ThenBy(e => e.FullName)
                        .Select(e => new EmployeeListDto
                        {
                            Id              = e.Id,
                            FullName        = e.FullName,
                            Email           = e.Email,
                            Role            = e.Role,
                            MobileNumber    = e.MobileNumber,
                            CountryCode     = e.CountryCode,
                            ProfileImageUrl = e.ProfileImageUrl,
                            IsActive        = e.IsActive,
                            GroupId         = e.GroupId,
                            GroupName       = e.GroupId != null
                                ? _context.Groups.Where(g => g.Id == e.GroupId).Select(g => g.Name).FirstOrDefault()
                                : null,
                            HasFaceData     = e.FaceDescriptor != null
                        })
                        .ToListAsync();
                }

                return Ok(employees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees");
                return StatusCode(500, new { message = "An error occurred while retrieving employees" });
            }
        }

        /// <summary>
        /// Get employee by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetEmployee(int id)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);

                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                return Ok(employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employee with ID {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the employee" });
            }
        }

        /// <summary>
        /// Check if email already exists globally (only for active/onboarded employees).
        /// NOTE: Use check-org-member for invite validation — a person can belong to multiple orgs.
        /// </summary>
        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmailExists([FromQuery] string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email))
                    return BadRequest(new { exists = false, message = "Email is required" });

                var exists = await _context.Employees
                    .AnyAsync(e => e.Email.ToLower() == email.ToLower() && e.IsActive);

                return Ok(new { exists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email existence for {Email}", email);
                return StatusCode(500, new { exists = false, message = "An error occurred" });
            }
        }

        /// <summary>
        /// Check if an email belongs to an ACTIVE member of a specific organization.
        /// This is the correct check to use before inviting — allows the same person
        /// to be invited to multiple orgs without false "already exists" errors.
        /// </summary>
        [HttpGet("check-org-member")]
        public async Task<IActionResult> CheckOrgMember([FromQuery] string email, [FromQuery] int orgId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email))
                    return BadRequest(new { exists = false, message = "Email is required" });

                if (orgId <= 0)
                    return BadRequest(new { exists = false, message = "orgId is required" });

                var exists = await _context.OrganizationMembers
                    .Include(m => m.Employee)
                    .AnyAsync(m => m.OrganizationId == orgId
                               && m.Employee.Email.ToLower() == email.ToLower()
                               && m.IsActive);

                return Ok(new { exists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking org membership for {Email} in org {OrgId}", email, orgId);
                return StatusCode(500, new { exists = false, message = "An error occurred" });
            }
        }

        /// <summary>
        /// Add a single employee
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddEmployee([FromBody] CreateEmployeeDto employeeDto)
        {
            try
            {
                // Validate email uniqueness
                if (await _context.Employees.AnyAsync(e => e.Email == employeeDto.Email))
                {
                    return BadRequest(new { message = "An employee with this email already exists" });
                }

                var employee = new Employee
                {
                    FullName = employeeDto.FullName,
                    Email = employeeDto.Email,
                    Role = employeeDto.Role ?? "User",
                    MobileNumber = employeeDto.MobileNumber,
                    CountryCode = employeeDto.CountryCode,
                    GroupId = employeeDto.GroupId,
                    IsActive = true,
                    DateOfBirth = employeeDto.DateOfBirth
                };

                _context.Employees.Add(employee);
                await _context.SaveChangesAsync();

                return CreatedAtAction(
                    nameof(GetEmployee),
                    new { id = employee.Id },
                    employee
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding employee");
                return StatusCode(500, new { message = "An error occurred while adding the employee" });
            }
        }

        /// <summary>
        /// Update employee
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateEmployee(int id, [FromBody] UpdateEmployeeDto employeeDto)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);

                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                // Check email uniqueness if email is being changed
                if (employeeDto.Email != employee.Email &&
                    await _context.Employees.AnyAsync(e => e.Email == employeeDto.Email && e.Id != id))
                {
                    return BadRequest(new { message = "An employee with this email already exists" });
                }

                employee.FullName = employeeDto.FullName;
                employee.Email = employeeDto.Email;
                employee.Role = employeeDto.Role;
                employee.MobileNumber = employeeDto.MobileNumber;
                employee.CountryCode = employeeDto.CountryCode;
                employee.DateOfBirth = employeeDto.DateOfBirth;
                employee.IsActive = employeeDto.IsActive;
                employee.GroupId = employeeDto.GroupId;

                await _context.SaveChangesAsync();

                return Ok(employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee with ID {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred while updating the employee" });
            }
        }

        /// <summary>
        /// Permanently deletes an employee and ALL related data.
        /// DB-level ON DELETE CASCADE handles: TimeEntries, TimeEntryChangeLogs, OrganizationMembers.
        /// We manually remove: EmployeeInvites, EmployeeNotificationSettings, TimeOffPolicyAssignments.
        /// Optional tables are guarded in case migrations haven't been applied yet.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                // 1. Invite records (matched by email — no FK to Employees)
                var invites = await _context.EmployeeInvites
                    .Where(i => i.Email == employee.Email)
                    .ToListAsync();
                _context.EmployeeInvites.RemoveRange(invites);

                // 2. Notification settings — guard: table may not exist if migration not yet applied
                try
                {
                    var notifSettings = await _context.EmployeeNotificationSettings
                        .Where(n => n.EmployeeId == id)
                        .ToListAsync();
                    _context.EmployeeNotificationSettings.RemoveRange(notifSettings);
                }
                catch (Exception ex) when (ex.Message.Contains("Invalid object name"))
                {
                    _logger.LogWarning("EmployeeNotificationSettings table not found — skipping (run migrations).");
                }

                // 3. Time-off policy assignments — guard: table may not exist
                try
                {
                    var policyAssignments = await _context.TimeOffPolicyAssignments
                        .Where(a => a.EmployeeId == id)
                        .ToListAsync();
                    _context.TimeOffPolicyAssignments.RemoveRange(policyAssignments);
                }
                catch (Exception ex) when (ex.Message.Contains("Invalid object name"))
                {
                    _logger.LogWarning("TimeOffPolicyAssignments table not found — skipping (run migrations).");
                }

                // 4. Remove employee — DB cascades handle TimeEntries, ChangeLogs, OrgMembers
                _context.Employees.Remove(employee);

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Employee {Id} ({Email}) and all related data permanently deleted.", id, employee.Email);

                return Ok(new { message = "Member and all related data deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting employee {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred while deleting the member." });
            }
        }

        /// <summary>
        /// Update employee's group assignment
        /// </summary>
        [HttpPatch("{id}/group")]
        public async Task<IActionResult> UpdateEmployeeGroup(int id, [FromBody] UpdateGroupAssignmentDto dto)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);

                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                // Validate group exists if GroupId is provided
                if (dto.GroupId.HasValue)
                {
                    var groupExists = await _context.Groups.AnyAsync(g => g.Id == dto.GroupId.Value);
                    if (!groupExists)
                        return BadRequest(new { message = "Group not found" });
                }

                employee.GroupId = dto.GroupId;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Group assignment updated successfully",
                    groupId = employee.GroupId,
                    groupName = employee.GroupId.HasValue
                        ? await _context.Groups.Where(g => g.Id == employee.GroupId).Select(g => g.Name).FirstOrDefaultAsync()
                        : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating group for employee with ID {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred while updating the group assignment" });
            }
        }

        /// <summary>
        /// Update employee's employment info (role + group) from the profile panel.
        /// </summary>
        [HttpPatch("{id}/employment")]
        public async Task<IActionResult> UpdateEmployment(int id, [FromBody] UpdateEmploymentDto dto)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                if (!string.IsNullOrWhiteSpace(dto.Role))
                    employee.Role = dto.Role;

                employee.GroupId = dto.GroupId; // null = remove from group

                await _context.SaveChangesAsync();

                var groupName = dto.GroupId.HasValue
                    ? await _context.Groups.Where(g => g.Id == dto.GroupId).Select(g => g.Name).FirstOrDefaultAsync()
                    : null;

                return Ok(new { message = "Employment info updated.", role = employee.Role, groupId = employee.GroupId, groupName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employment for employee {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        /// <summary>
        /// Archive a member (soft-delete: sets IsActive = false).
        /// </summary>
        [HttpDelete("{id}/archive")]
        public async Task<IActionResult> ArchiveEmployee(int id)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                employee.IsActive = false;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Member archived." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error archiving employee {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        /// <summary>
        /// Get employee profile
        /// </summary>
        [HttpGet("{id}/profile")]
        public async Task<IActionResult> GetProfile(int id)
        {
            try
            {
                var employee = await _context.Employees
                    .Include(e => e.Organization)
                    .FirstOrDefaultAsync(e => e.Id == id);

                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                var profile = new
                {
                    employee.Id,
                    employee.FullName,
                    employee.Email,
                    employee.MobileNumber,
                    employee.DateOfBirth,
                    employee.ProfileImageUrl,
                    employee.Role,
                    employee.HasFaceData,
                    OrganizationName = employee.Organization?.Name
                };

                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving profile for employee {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the profile" });
            }
        }

        /// <summary>
        /// Update employee profile
        /// </summary>
        [HttpPut("{id}/profile")]
        public async Task<IActionResult> UpdateProfile(int id, [FromBody] UpdateProfileDto dto)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);

                if (employee == null)
                    return NotFound(new { message = "Employee not found" });

                employee.FullName = dto.FullName;
                employee.MobileNumber = dto.MobileNumber;
                employee.CountryCode = dto.CountryCode;
                employee.DateOfBirth = dto.DateOfBirth;
                employee.ProfileImageUrl = dto.ProfileImageUrl;

                await _context.SaveChangesAsync();

                var updatedProfile = new
                {
                    employee.Id,
                    employee.FullName,
                    employee.Email,
                    employee.MobileNumber,
                    employee.DateOfBirth,
                    employee.ProfileImageUrl,
                    employee.Role,
                    employee.HasFaceData
                };

                return Ok(updatedProfile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile for employee {EmployeeId}", id);
                return StatusCode(500, new { message = "An error occurred while updating the profile" });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // FACIAL RECOGNITION ENDPOINTS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Enroll or update a face descriptor for an employee.
        /// POST api/employees/{id}/face-descriptor
        /// Body: { "descriptor": "[0.12, -0.34, ...]" }
        /// </summary>
        [HttpPost("{id}/face-descriptor")]
        public async Task<IActionResult> SaveFaceDescriptor(int id, [FromBody] SaveFaceDescriptorDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Descriptor))
                    return BadRequest(new { message = "Descriptor is required." });

                var employee = await _context.Employees.FindAsync(id);
                if (employee == null)
                    return NotFound(new { message = "Employee not found." });

                employee.FaceDescriptor = dto.Descriptor;

                // Save enrolment photo if provided
                string? enrollPhotoUrl = employee.ProfileImageUrl; // keep existing unless new one provided
                if (!string.IsNullOrWhiteSpace(dto.EnrollPhoto))
                {
                    try
                    {
                        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        Directory.CreateDirectory(uploadsPath);
                        var fileName = $"face-enroll-{id}-{Guid.NewGuid()}.jpg";
                        var filePath = Path.Combine(uploadsPath, fileName);
                        var photoBytes = Convert.FromBase64String(dto.EnrollPhoto);
                        await System.IO.File.WriteAllBytesAsync(filePath, photoBytes);
                        enrollPhotoUrl = $"/uploads/{fileName}";
                        // Store as profile image so it is visible on the account page
                        employee.ProfileImageUrl = enrollPhotoUrl;
                    }
                    catch (Exception photoEx)
                    {
                        _logger.LogWarning(photoEx, "Could not save enrol photo for employee {Id}", id);
                    }
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Face descriptor enrolled for employee {Id}.", id);
                return Ok(new { message = "Face data saved successfully.", hasFaceData = true, enrollPhotoUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveFaceDescriptor failed for employee {Id}", id);
                return StatusCode(500, new { message = "An error occurred while saving face data." });
            }
        }

        /// <summary>
        /// Remove an employee's face descriptor.
        /// DELETE api/employees/{id}/face-descriptor
        /// </summary>
        [HttpDelete("{id}/face-descriptor")]
        public async Task<IActionResult> DeleteFaceDescriptor(int id)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee == null)
                    return NotFound(new { message = "Employee not found." });

                employee.FaceDescriptor = null;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Face descriptor removed for employee {Id}.", id);
                return Ok(new { message = "Face data deleted successfully.", hasFaceData = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteFaceDescriptor failed for employee {Id}", id);
                return StatusCode(500, new { message = "An error occurred while deleting face data." });
            }
        }

        /// <summary>
        /// Get an employee's stored face descriptor (for client-side matching).
        /// GET api/employees/{id}/face-descriptor
        /// Returns null descriptor when not enrolled.
        /// </summary>
        [HttpGet("{id}/face-descriptor")]
        public async Task<IActionResult> GetFaceDescriptor(int id)
        {
            try
            {
                var result = await _context.Employees
                    .Where(e => e.Id == id)
                    .Select(e => new { e.FaceDescriptor, e.FullName })
                    .FirstOrDefaultAsync();

                if (result == null)
                    return NotFound(new { message = "Employee not found." });

                var emp2 = await _context.Employees
                    .Where(e => e.Id == id)
                    .Select(e => new { e.FaceDescriptor, e.FullName, e.ProfileImageUrl })
                    .FirstOrDefaultAsync();

                return Ok(new
                {
                    descriptor     = result.FaceDescriptor,
                    hasFaceData    = result.FaceDescriptor != null,
                    fullName       = result.FullName,
                    enrollPhotoUrl = emp2?.ProfileImageUrl
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetFaceDescriptor failed for employee {Id}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        /// <summary>
        /// Get face descriptors for all enrolled employees in an organization.
        /// Used by the kiosk page to load all faces for client-side matching.
        /// GET api/employees/face-descriptors?organizationId=X
        /// </summary>
        [HttpGet("face-descriptors")]
        public async Task<IActionResult> GetAllFaceDescriptors([FromQuery] int organizationId)
        {
            try
            {
                if (organizationId <= 0)
                    return BadRequest(new { message = "organizationId is required." });

                // Return only employees with enrolled face data
                var enrolled = await _context.OrganizationMembers
                    .Where(m => m.OrganizationId == organizationId && m.IsActive)
                    .Include(m => m.Employee)
                    .Where(m => m.Employee.FaceDescriptor != null)
                    .Select(m => new
                    {
                        employeeId      = m.Employee.Id,
                        fullName        = m.Employee.FullName,
                        profileImageUrl = m.Employee.ProfileImageUrl,
                        descriptor      = m.Employee.FaceDescriptor
                    })
                    .ToListAsync();

                return Ok(enrolled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllFaceDescriptors failed for org {OrgId}", organizationId);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }
    }
}
