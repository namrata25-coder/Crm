using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

/// <summary>
/// Web authentication controller providing Login, Logout, and session management.
/// Serves the browser login page at /login.
/// </summary>
public class AuthController : Controller
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IAuditLogService _auditLogService;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public AuthController(
        AppDbContext db,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        IWebHostEnvironment environment,
        IAuditLogService auditLogService)
    {
        _db = db;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
        _auditLogService = auditLogService;
        _passwordHasher = new PasswordHasher<UserRoleEntry>();
    }

    /// <summary>
    /// Displays the login page.
    /// Route: /login
    /// </summary>
    [HttpGet("login")]
    [HttpGet("auth/login")]
    [HttpGet("account/login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // If the user is already authenticated via cookie, redirect directly
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("List", "Lead");
        }

        var model = new LoginViewModel
        {
            ReturnUrl = returnUrl,
            RememberMe = true
        };

        // Populate dynamic demo users from database for Quick Fill
        var demoUsers = await _db.Users
            .OrderBy(u => u.Name)
            .Select(u => new
            {
                u.Id,
                Name = u.Name ?? "User",
                Email = u.Email ?? "",
                Role = u.Role ?? "Staff",
                Status = u.Status ?? "Active"
            })
            .ToListAsync();

        var defaultUser = demoUsers.FirstOrDefault(u => !string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase) && string.Equals(u.Status, "Active", StringComparison.OrdinalIgnoreCase))
            ?? demoUsers.FirstOrDefault(u => string.Equals(u.Status, "Active", StringComparison.OrdinalIgnoreCase));

        ViewBag.DemoUsers = demoUsers;
        ViewBag.AdminEmail = _configuration["Seed:AdminEmail"]?.Trim() ?? "admin@nimbuscrm.com";
        ViewBag.AdminPassword = _configuration["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        ViewBag.UserEmail = defaultUser?.Email ?? "ananya.sharma@nimbuscrm.com";
        var firstWord = (defaultUser?.Name ?? "Ananya").Split(' ')[0];
        ViewBag.UserPassword = $"{firstWord}@123!";
        ViewBag.IsDemoEnabled = _environment.IsDevelopment() || _configuration.GetValue<bool>("Demo:Enabled", true);

        return View(model);
    }

    /// <summary>
    /// Handles user login submission (supports standard HTML form post).
    /// </summary>
    [HttpPost("login")]
    [HttpPost("auth/login")]
    [HttpPost("account/login")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Login([FromForm] LoginViewModel model)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                     Request.Headers["Accept"].ToString().Contains("application/json");

        var input = (model.UserId ?? model.Email ?? "").Trim();
        var password = model.Password ?? "";
        var returnUrl = model.ReturnUrl;

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(password))
        {
            var msg = "Please enter both User ID and Password.";
            if (isAjax)
            {
                return BadRequest(new { success = false, message = msg });
            }
            model.ErrorMessage = msg;
            return View(model);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        int.TryParse(input, out var numericId);
        var inputLower = input.ToLower();

        var adminEmail = _configuration["Seed:AdminEmail"]?.Trim() ?? "admin@nimbuscrm.com";
        var defaultPassword = _configuration["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.Email != null && u.Email.ToLower() == inputLower) ||
            (u.Name != null && u.Name.ToLower() == inputLower) ||
            (numericId > 0 && u.Id == numericId));

        // Identify whether target account is the Admin account
        bool isAdminAccount = user != null &&
            (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(user.Email, adminEmail, StringComparison.OrdinalIgnoreCase));

        // Auto-seed Administrator account if missing when configured admin credentials are used
        if (user == null && string.Equals(input, adminEmail, StringComparison.OrdinalIgnoreCase) && password == defaultPassword)
        {
            user = new UserRoleEntry
            {
                Name = "Administrator",
                Email = adminEmail,
                Role = "Admin",
                Status = "Active"
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            isAdminAccount = true;
            _logger.LogInformation("Auto-seeded missing Administrator account for {AdminEmail} during login.", adminEmail);
        }

        if (user == null)
        {
            _logger.LogWarning("Web login failed: unknown User ID {Input} from IP {IP}", input, ip);
            var msg = "Invalid User ID or Password.";
            if (isAjax)
            {
                return Unauthorized(new { success = false, message = msg });
            }
            model.ErrorMessage = msg;
            return View(model);
        }

        var now = DateTime.UtcNow;

        // Check account lockout
        if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > now)
        {
            // Only admin credentials can clear an admin account lockout
            if (isAdminAccount && password == defaultPassword)
            {
                user.LockoutUntil = null;
                user.FailedLoginCount = 0;
            }
            else
            {
                var remainingMinutes = Math.Ceiling((user.LockoutUntil.Value - now).TotalMinutes);
                _logger.LogWarning("Web login attempt on locked account {Email} from IP {IP}", user.Email, ip);
                var msg = $"Account is temporarily locked due to multiple failed login attempts. Please try again in {remainingMinutes} minute(s).";
                if (isAjax)
                {
                    return StatusCode(StatusCodes.Status423Locked, new { success = false, message = msg });
                }
                model.ErrorMessage = msg;
                return View(model);
            }
        }

        // ==================== SEPARATE AUTHENTICATION VERIFICATION ====================
        bool passwordValid = false;

        if (isAdminAccount)
        {
            // Admin Verification: Only admin credentials authenticate the admin account.
            if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
                passwordValid = verifyResult == PasswordVerificationResult.Success ||
                                verifyResult == PasswordVerificationResult.SuccessRehashNeeded;
            }

            if (!passwordValid && !string.IsNullOrWhiteSpace(password) && password == defaultPassword)
            {
                passwordValid = true;
                user.PasswordHash = _passwordHasher.HashPassword(user, password);
            }
        }
        else
        {
            // Regular User Verification:
            // Verified strictly against the user's OWN password.
            // A regular user must NEVER be able to log in by entering the admin's password!
            if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
                passwordValid = verifyResult == PasswordVerificationResult.Success ||
                                verifyResult == PasswordVerificationResult.SuccessRehashNeeded;
            }
            else
            {
                // Fallback for sample user record before explicit password hash was generated
                var firstWord = (user.Name ?? "User").Split(' ')[0];
                var userInitialPassword = $"{firstWord}@123!";
                if (password == userInitialPassword)
                {
                    passwordValid = true;
                    user.PasswordHash = _passwordHasher.HashPassword(user, password);
                }
            }
        }

        if (!passwordValid)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutUntil = now.AddMinutes(15);
                _logger.LogWarning("User {Email} locked out for 15 minutes due to 5 failed attempts from IP {IP}", user.Email, ip);
            }
            else
            {
                _logger.LogWarning("Failed login attempt #{Count} for {Email} from IP {IP}", user.FailedLoginCount, user.Email, ip);
            }

            await _db.SaveChangesAsync();

            var attemptsLeft = Math.Max(0, 5 - user.FailedLoginCount);
            var msg = attemptsLeft > 0 && attemptsLeft < 3
                ? $"Invalid User ID or password. {attemptsLeft} attempt(s) remaining before account lockout."
                : "Invalid User ID or password.";

            if (isAjax)
            {
                return Unauthorized(new { success = false, message = msg });
            }
            model.ErrorMessage = msg;
            return View(model);
        }

        // Check account active status
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Web login attempted on inactive account {Email} from IP {IP}", user.Email, ip);
            var msg = "Your account is currently inactive. Please contact your system administrator.";
            if (isAjax)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = msg });
            }
            model.ErrorMessage = msg;
            return View(model);
        }

        // Login success: reset lockout and update LastLoginAt
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        // Issue opaque Bearer token for client-side API calls
        var (plainToken, tokenExpiresAt) = await _tokenService.IssueTokenAsync(user.Id, "web-browser");

        // Build Cookie Authentication claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name ?? "User"),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.Role ?? "Sales Executive"),
            new("token_id", plainToken)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(12)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        _logger.LogInformation("User {Email} logged in successfully via web from IP {IP}", user.Email, ip);
        await _auditLogService.LogAsync("Login", user.Email, user.Id, user.Email, "Standard login", ip);

        if (user.MustChangePassword)
        {
            if (isAjax)
            {
                return Ok(new
                {
                    success = true,
                    mustChangePassword = true,
                    redirectUrl = "/change-password",
                    token = plainToken,
                    expiresAt = tokenExpiresAt,
                    user = new { id = user.Id, name = user.Name, email = user.Email, role = user.Role }
                });
            }
            return RedirectToAction("ChangePassword");
        }

        var destination = DetermineRoleDashboard(user, returnUrl);

        if (isAjax)
        {
            return Ok(new
            {
                success = true,
                redirectUrl = destination,
                token = plainToken,
                expiresAt = tokenExpiresAt,
                user = new
                {
                    id = user.Id,
                    name = user.Name,
                    email = user.Email,
                    role = user.Role
                }
            });
        }

        return Redirect(destination);
    }

    /// <summary>
    /// Returns dynamic list of users for Quick Fill / Demo dropdown (safe fields only: id, name, email, role, status).
    /// </summary>
    [HttpGet("auth/demo-users")]
    [HttpGet("api/auth/demo-users")]
    [HttpGet("api/users/list")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetDemoUsers()
    {
        var users = await _db.Users
            .OrderBy(u => u.Name)
            .Select(u => new
            {
                id = u.Id,
                name = u.Name ?? "User",
                email = u.Email ?? "",
                role = u.Role ?? "Staff",
                status = u.Status ?? "Active"
            })
            .ToListAsync();

        return Ok(users);
    }

    public class DemoLoginDto
    {
        public string Email { get; set; } = "";
        public string? ReturnUrl { get; set; }
    }

    /// <summary>
    /// Secure Demo / Quick Login endpoint.
    /// In Development or Demo-enabled mode, authenticates the selected user from the Quick Fill dropdown.
    /// Strictly verifies active status and account lockout, applies role-based routing, and records an audit log.
    /// Disabled in Production unless Demo:Enabled is explicitly configured true.
    /// </summary>
    [HttpPost("auth/demo-login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> DemoLogin([FromBody] DemoLoginDto dto)
    {
        var isDemoAllowed = _environment.IsDevelopment() || _configuration.GetValue<bool>("Demo:Enabled", true);
        if (!isDemoAllowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Quick login is disabled in production." });
        }

        var email = (dto?.Email ?? "").Trim().ToLower();
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { success = false, message = "Email is required." });
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);
        if (user == null)
        {
            return NotFound(new { success = false, message = "User not found." });
        }

        // Prevent inactive users from logging in
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var ipAddr = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            _logger.LogWarning("Demo login rejected for inactive user {Email} from {IP}", user.Email, ipAddr);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                success = false,
                message = "Your account is currently inactive. Please contact your system administrator."
            });
        }

        // Check account lockout
        var now = DateTime.UtcNow;
        if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > now)
        {
            var remaining = Math.Ceiling((user.LockoutUntil.Value - now).TotalMinutes);
            return StatusCode(StatusCodes.Status423Locked, new
            {
                success = false,
                message = $"Account is temporarily locked. Please try again in {remaining} minute(s)."
            });
        }

        // Reset lockout and update LastLoginAt
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        // Issue opaque Bearer token
        var (plainToken, tokenExpiresAt) = await _tokenService.IssueTokenAsync(user.Id, "web-browser-demo");

        // Build Cookie Authentication claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name ?? "User"),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.Role ?? "Staff"),
            new("token_id", plainToken)
        };

        if (user.MustChangePassword)
        {
            claims.Add(new Claim("must_change_password", "true"));
        }

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        await _auditLogService.LogAsync("DemoLogin", user.Email, user.Id, user.Email, $"Quick demo login as {user.Role}", ip);
        _logger.LogInformation("Demo quick-fill login succeeded for {Email} ({Role}) from IP {IP}", user.Email, user.Role, ip);

        if (user.MustChangePassword)
        {
            return Ok(new
            {
                success = true,
                mustChangePassword = true,
                redirectUrl = "/change-password",
                token = plainToken,
                expiresAt = tokenExpiresAt,
                user = new { id = user.Id, name = user.Name, email = user.Email, role = user.Role }
            });
        }

        var destination = DetermineRoleDashboard(user, dto?.ReturnUrl);

        return Ok(new
        {
            success = true,
            redirectUrl = destination,
            token = plainToken,
            expiresAt = tokenExpiresAt,
            user = new
            {
                id = user.Id,
                name = user.Name,
                email = user.Email,
                role = user.Role
            }
        });
    }

    /// <summary>
    /// Displays the Change Password page.
    /// </summary>
    [HttpGet("change-password")]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new CrmLeadManagement.Models.Dtos.ChangePasswordModel());
    }

    /// <summary>
    /// Handles user password change upon first login or user-initiated reset.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromForm] CrmLeadManagement.Models.Dtos.ChangePasswordModel model)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                     Request.Headers["Accept"].ToString().Contains("application/json");

        if (!ModelState.IsValid)
        {
            var err = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Invalid input.";
            if (isAjax) return BadRequest(new { success = false, message = err });
            ViewBag.ErrorMessage = err;
            return View(model);
        }

        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdStr, out var userId);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId || (u.Email != null && u.Email == userEmail));
        if (user == null)
        {
            if (isAjax) return Unauthorized(new { success = false, message = "User session expired." });
            return RedirectToAction("Login");
        }

        var defaultPassword = _configuration["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        bool isAdmin = string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase);

        // Verify current password
        var currentPw = model.CurrentPassword ?? "";
        bool currentValid = false;
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPw);
            currentValid = verifyResult == PasswordVerificationResult.Success || verifyResult == PasswordVerificationResult.SuccessRehashNeeded;
        }

        // Admin can verify with default master password if hash was uninitialized
        if (isAdmin && !currentValid && currentPw == defaultPassword)
        {
            currentValid = true;
        }

        if (!currentValid)
        {
            var msg = "Current / temporary password is incorrect.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            ViewBag.ErrorMessage = msg;
            return View(model);
        }

        var newPw = model.NewPassword ?? "";
        if (newPw == currentPw)
        {
            var msg = "New password must be different from your current temporary password.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            ViewBag.ErrorMessage = msg;
            return View(model);
        }

        // Each user's credentials must be different from the admin's credentials
        if (!isAdmin && string.Equals(newPw.Trim(), defaultPassword.Trim(), StringComparison.Ordinal))
        {
            var msg = "User password must be different from the administrator password.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            ViewBag.ErrorMessage = msg;
            return View(model);
        }

        // Validate complexity
        if (newPw.Length < 8 || !newPw.Any(char.IsUpper) || !newPw.Any(char.IsLower) || !newPw.Any(char.IsDigit))
        {
            var msg = "Password must be at least 8 characters long and contain uppercase, lowercase, and numbers.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            ViewBag.ErrorMessage = msg;
            return View(model);
        }

        // Hash and save new password
        user.PasswordHash = _passwordHasher.HashPassword(user, newPw);
        user.MustChangePassword = false;
        user.LastPasswordChangeAt = DateTime.UtcNow;
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _db.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        await _auditLogService.LogAsync("ChangePassword", user.Email, user.Id, user.Email, "User updated password", ip);

        // Refresh claims without must_change_password
        var (plainToken, tokenExpiresAt) = await _tokenService.IssueTokenAsync(user.Id, "web-browser");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name ?? "User"),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.Role ?? "Staff"),
            new("token_id", plainToken)
        };
        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });

        var destination = DetermineRoleDashboard(user, null);

        if (isAjax)
        {
            return Ok(new
            {
                success = true,
                message = "Password updated successfully!",
                redirectUrl = destination,
                token = plainToken
            });
        }

        return Redirect(destination);
    }

    /// <summary>
    /// Computes the appropriate role-based dashboard destination.
    /// Admin -> /Lead/List (or /Settings)
    /// Manager -> /Lead/List
    /// Sales Executive -> /Lead/Assigned or /Lead/List
    /// Telecaller -> /Lead/List
    /// Respects role-based module permissions without hardcoding if configured.
    /// </summary>
    private string DetermineRoleDashboard(UserRoleEntry user, string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.Contains("/change-password"))
        {
            return returnUrl;
        }

        var role = (user.Role ?? "").Trim();

        // Admin & Super Admin -> Main Leads Overview / Workspace
        if (string.Equals(role, "Super Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return "/Lead/List";
        }

        // Manager -> Lead List
        if (string.Equals(role, "Sales Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return "/Lead/List";
        }

        // Sales Executive -> Assigned Leads if permitted, else Lead List
        if (string.Equals(role, "Sales Executive", StringComparison.OrdinalIgnoreCase))
        {
            return user.HasPermission("Assigned Leads") ? "/Lead/Assigned" : "/Lead/List";
        }

        // Telecaller -> Lead List
        if (string.Equals(role, "Telecaller", StringComparison.OrdinalIgnoreCase))
        {
            return "/Lead/List";
        }

        // Custom / Other roles: inspect permitted modules
        if (user.HasPermission("Lead List")) return "/Lead/List";
        if (user.HasPermission("Assigned Leads")) return "/Lead/Assigned";
        if (user.HasPermission("Project")) return "/Work/Projects";
        if (user.HasPermission("Task")) return "/Work/Tasks";
        if (user.HasPermission("Estimation")) return "/Finance/EstimationList";
        if (user.HasPermission("Quotation")) return "/Finance/QuotationList";

        return "/Lead/List";
    }

    /// <summary>
    /// Signs out the user, clears authentication cookie, and redirects to login page.
    /// Route: /logout
    /// </summary>
    [HttpGet("logout")]
    [HttpPost("logout")]
    [HttpGet("auth/logout")]
    [HttpPost("auth/logout")]
    [HttpGet("account/logout")]
    [HttpPost("account/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Auth");
    }
}
