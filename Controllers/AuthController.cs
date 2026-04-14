using APM.StaffZen.API.Data;
using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Models;
using APM.StaffZen.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordService _passwordService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(ApplicationDbContext context,
                              PasswordService passwordService,
                              IConfiguration configuration,
                              ILogger<AuthController> logger)
        {
            _context = context;
            _passwordService = passwordService;
            _configuration = configuration;
            _logger = logger;
        }

        // Standard email + password login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Email and password are required.");
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email.ToLower() == request.Email.ToLower());

            if (employee == null)
                return Unauthorized("Invalid email or password");

            if (!employee.IsActive)
                return Unauthorized("Account is not active. Please contact administrator.");

            // 🔐 Must have stored password hash — Google-only accounts have null hash
            if (string.IsNullOrEmpty(employee.PasswordHash) ||
                string.IsNullOrEmpty(employee.PasswordSalt))
            {
                return Unauthorized("This account uses Google Sign-In and has no password set. Please sign in with Google, or use 'Forgot Password' to set one.");
            }

            var isValid = _passwordService.VerifyPassword(
                request.Password,
                employee.PasswordHash,
                employee.PasswordSalt);

            if (!isValid)
                return Unauthorized("Invalid email or password");

            var loginResult = await BuildLoginResponseAsync(employee);
            return Ok(loginResult);
        }


        // Self-registration — creates a new Employee account (role = "Employee", IsActive = true)
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Full name, email and password are required." });

            var exists = await _context.Employees
                .AnyAsync(e => e.Email.ToLower() == request.Email.ToLower());

            if (exists)
                return Conflict(new { error = "An account with this email already exists." });

            _passwordService.CreatePasswordHash(request.Password, out string hash, out string salt);

            var employee = new Employee
            {
                FullName     = request.FullName.Trim(),
                Email        = request.Email.Trim().ToLower(),
                PasswordHash = hash,
                PasswordSalt = salt,
                MobileNumber = request.PhoneNumber,
                Role         = "Employee",
                IsActive     = true,
                IsOnboarded  = false   // must complete getting-started first
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            var result = await BuildLoginResponseAsync(employee);
            result.IsNewAccount = true;
            return Ok(result);
        }

        // Google self-registration — creates account from Google email (no password)
        [HttpPost("google-register")]
        public async Task<IActionResult> GoogleRegister([FromBody] GoogleLoginRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email))
                    return BadRequest(new { error = "Email is required." });

                var existing = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email.ToLower() == request.Email.ToLower());

                // Already registered and active — return full session (treat as login)
                if (existing != null && existing.IsActive)
                {
                    var existingResult = await BuildLoginResponseAsync(existing);
                    existingResult.IsNewAccount = false;
                    return Ok(existingResult);
                }

                // Pending invite record exists (IsActive = false) — activate it
                if (existing != null && !existing.IsActive)
                {
                    existing.IsActive    = true;
                    existing.IsOnboarded = false;
                    await _context.SaveChangesAsync();
                    var pendingResult = await BuildLoginResponseAsync(existing);
                    pendingResult.IsNewAccount = true;
                    return Ok(pendingResult);
                }

                // Brand new user — create account
                var fullName = !string.IsNullOrWhiteSpace(request.FullName)
                    ? request.FullName.Trim()
                    : request.Email.Split('@')[0];

                string? hash = null, salt = null;
                if (!string.IsNullOrWhiteSpace(request.Password))
                    _passwordService.CreatePasswordHash(request.Password, out hash, out salt);

                var employee = new Employee
                {
                    FullName     = fullName,
                    Email        = request.Email.Trim().ToLower(),
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    Role         = "Employee",
                    IsActive     = true,
                    IsOnboarded  = false
                };

                _context.Employees.Add(employee);
                await _context.SaveChangesAsync();

                var newResult = await BuildLoginResponseAsync(employee);
                newResult.IsNewAccount = true;
                return Ok(newResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GoogleRegister failed for {Email}", request.Email);
                return StatusCode(500, new { error = $"Registration failed: {ex.Message}" });
            }
        }

        // Google login — email already verified by Google JS SDK, no password needed.
        // If active account exists → log in.
        // If inactive/pending invite exists → activate and log in.
        // If no account at all → return 404 so the UI can prompt sign-up.
        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email))
                    return BadRequest(new { error = "Email is required." });

                // Search WITHOUT IsActive filter so pending invite records are found too
                var employee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email.ToLower() == request.Email.ToLower());

                if (employee == null)
                {
                    // Genuinely no account — tell the client so it can show a proper message
                    return NotFound(new { error = $"No StaffZen account found for {request.Email}. Please sign up first." });
                }

                // Pending invite record (IsActive = false) — activate on first Google login
                if (!employee.IsActive)
                {
                    employee.IsActive    = true;
                    employee.IsOnboarded = false;
                    await _context.SaveChangesAsync();

                    var activatedResult = await BuildLoginResponseAsync(employee);
                    activatedResult.IsNewAccount = true;
                    return Ok(activatedResult);
                }

                // Normal active account — log in
                var existingResult = await BuildLoginResponseAsync(employee);
                existingResult.IsNewAccount = false;
                return Ok(existingResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GoogleLogin failed for {Email}", request.Email);
                return StatusCode(500, new { error = $"Login failed: {ex.Message}" });
            }
        }
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto,
           [FromServices] EmailService emailService)
        {
            try
            {
                _logger.LogInformation("ForgotPassword called. Email={Email}, Phone={Phone}", dto.Email, dto.PhoneNumber);

                Employee? employee = null;

                if (!string.IsNullOrWhiteSpace(dto.Email))
                {
                    employee = await _context.Employees
                        .FirstOrDefaultAsync(e => e.Email.ToLower() == dto.Email.ToLower() && e.IsActive);
                }
                else if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                {
                    employee = await _context.Employees
                        .FirstOrDefaultAsync(e => e.MobileNumber == dto.PhoneNumber && e.IsActive);
                }

                if (employee == null)
                {
                    _logger.LogWarning("ForgotPassword: No active employee found for Email={Email}", dto.Email);
                    // Return Ok anyway to avoid revealing account existence
                    return Ok(new { message = "If an account exists, a reset link has been sent." });
                }

                _logger.LogInformation("ForgotPassword: Found employee Id={Id} Email={Email}", employee.Id, employee.Email);

                // Generate a secure token
                var tokenBytes = RandomNumberGenerator.GetBytes(32);
                var token = Convert.ToBase64String(tokenBytes)
                    .Replace("+", "-").Replace("/", "_").Replace("=", "");

                employee.PasswordResetToken = token;
                employee.PasswordResetExpiry = DateTime.UtcNow.AddHours(1);

                _logger.LogInformation("ForgotPassword: Saving token to DB for employee {Id}", employee.Id);
                await _context.SaveChangesAsync();
                _logger.LogInformation("ForgotPassword: Token saved. Sending email...");

                var blazorBase = _configuration["AppSettings:BlazorBaseUrl"] ?? "https://localhost:7299";
                var resetLink = $"{blazorBase}/reset-password?token={token}";

                var emailSent = await emailService.SendPasswordResetEmail(employee.Email, employee.FullName, resetLink);
                _logger.LogInformation("ForgotPassword: Email sent={Sent} to {Email}", emailSent, employee.Email);

                return Ok(new { message = "If an account exists, a reset link has been sent.", emailSent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ForgotPassword EXCEPTION for Email={Email}", dto.Email);
                return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
            }
        }

        // ── Reset Password ─────────────────────────────────────────────
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest("Token and new password are required.");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.PasswordResetToken == dto.Token);

            if (employee == null)
                return BadRequest("Invalid or expired reset token.");

            if (employee.PasswordResetExpiry == null || employee.PasswordResetExpiry < DateTime.UtcNow)
                return BadRequest("Reset token has expired. Please request a new one.");

            _passwordService.CreatePasswordHash(dto.NewPassword, out string newHash, out string newSalt);
            employee.PasswordHash = newHash;
            employee.PasswordSalt = newSalt;
            employee.PasswordResetToken = null;
            employee.PasswordResetExpiry = null;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Password updated successfully." });
        }

        // ── Validate Reset Token ───────────────────────────────────────
        [HttpGet("validate-reset-token")]
        public async Task<IActionResult> ValidateResetToken([FromQuery] string token)
        {
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.PasswordResetToken == token);

            if (employee == null || employee.PasswordResetExpiry < DateTime.UtcNow)
                return BadRequest("Invalid or expired token.");

            return Ok(new { email = employee.Email });
        }
    
        // ── Change Password (logged-in user, verifies current password) ─
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Email) ||
                    string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                    string.IsNullOrWhiteSpace(dto.NewPassword))
                    return BadRequest(new { error = "All fields are required." });

                var employee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email.ToLower() == dto.Email.ToLower() && e.IsActive);

                if (employee == null)
                    return NotFound(new { error = "Account not found." });

                if (string.IsNullOrEmpty(employee.PasswordHash) || string.IsNullOrEmpty(employee.PasswordSalt))
                    return BadRequest(new { error = "No password set on this account." });

                var isValid = _passwordService.VerifyPassword(
                    dto.CurrentPassword, employee.PasswordHash, employee.PasswordSalt);

                if (!isValid)
                    return BadRequest(new { error = "Current password is incorrect." });

                _passwordService.CreatePasswordHash(dto.NewPassword, out string newHash, out string newSalt);
                employee.PasswordHash = newHash;
                employee.PasswordSalt = newSalt;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Password changed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChangePassword failed for {Email}", dto.Email);
                return StatusCode(500, new { error = ex.Message });
            }
        }


        // Marks employee as onboarded — fallback if org creation returned null
        [HttpPost("complete-onboarding")]
        public async Task<IActionResult> CompleteOnboarding([FromBody] CompleteOnboardingRequest request)
        {
            var employee = await _context.Employees.FindAsync(request.EmployeeId);
            if (employee == null) return NotFound(new { error = "Employee not found." });

            employee.IsOnboarded = true;
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        // Sets or updates a password for a Google Sign-In account during onboarding.
        // Called after Google signup when the user sets a password in the org setup form.
        [HttpPost("set-password")]
        public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Email and password are required." });

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email.ToLower() == request.Email.ToLower() && e.IsActive);

            if (employee == null)
                return NotFound(new { error = "No active account found with that email." });

            _passwordService.CreatePasswordHash(request.Password, out string hash, out string salt);
            employee.PasswordHash = hash;
            employee.PasswordSalt = salt;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password set successfully. You can now log in with your email." });
        }


        // ── Builds a LoginResponse with all org memberships populated ──────
        private async Task<LoginResponse> BuildLoginResponseAsync(Employee employee)
        {
            // Load all org memberships for this employee
            List<UserOrgMembership> orgs = new();
            try
            {
                orgs = await _context.OrganizationMembers
                    .Include(m => m.Organization)
                    .Where(m => m.EmployeeId == employee.Id && m.IsActive)
                    .Select(m => new UserOrgMembership
                    {
                        OrganizationId   = m.OrganizationId,
                        OrganizationName = m.Organization.Name,
                        OrgRole          = m.OrgRole,
                        IsOwner          = m.Organization.EmployeeId == employee.Id
                    })
                    .OrderByDescending(m => m.IsOwner)
                    .ThenBy(m => m.OrganizationName)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load org memberships for employee {EmpId}. " +
                    "Ensure the OrganizationMembers migration has been applied (dotnet ef database update).",
                    employee.Id);
            }

            // Default active org = first org (prefer one they own)
            var defaultOrg = orgs.FirstOrDefault(o => o.IsOwner) ?? orgs.FirstOrDefault();

            return new LoginResponse
            {
                Id                   = employee.Id,
                FullName             = employee.FullName,
                Email                = employee.Email,
                Role                 = employee.Role,
                IsOnboarded          = employee.IsOnboarded,
                OrganizationName     = defaultOrg?.OrganizationName,
                ActiveOrganizationId = defaultOrg?.OrganizationId,
                ActiveOrgRole        = defaultOrg?.OrgRole,
                Organizations        = orgs
            };
        }
    }
}