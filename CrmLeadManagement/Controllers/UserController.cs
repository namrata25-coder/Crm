using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using CrmLeadManagement.Services;

namespace CrmLeadManagement.Controllers;

/// <summary>
/// Dedicated User Controller.
/// Manages User authentication, User session handling, and the User Dashboard.
/// Enforces strict database-verified role authorization on every action.
/// </summary>
public class UserController : Controller
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserController> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public UserController(
        AppDbContext db,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<UserController> logger,
        IAuditLogService auditLogService)
    {
        _db = db;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
        _auditLogService = auditLogService;
        _passwordHasher = new PasswordHasher<UserRoleEntry>();
    }

    /// <summary>
    /// Root user route. Redirects authenticated Users to the dashboard, otherwise to the user login.
    /// Route: /user
    /// </summary>
    [HttpGet("user")]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out var userId))
            {
                var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (dbUser != null && dbUser.IsUserAccount && string.Equals(dbUser.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction(nameof(Dashboard));
                }
            }
        }

        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// Displays the dedicated User Login page.
    /// Route: /user/login
    /// </summary>
    [HttpGet("user/login")]
    [HttpGet("auth/user-login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // If already authenticated as an active User, redirect directly to User Dashboard
        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out var userId))
            {
                var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (dbUser != null && dbUser.IsUserAccount && string.Equals(dbUser.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl.StartsWith("/user", StringComparison.OrdinalIgnoreCase))
                    {
                        return Redirect(returnUrl);
                    }
                    return RedirectToAction(nameof(Dashboard));
                }
            }
        }

        var model = new LoginViewModel
        {
            ReturnUrl = returnUrl,
            RememberMe = true,
            LoginType = "user"
        };

        // Populate dynamic demo users for Quick Fill (non-admin accounts only)
        var demoUsers = await _db.Users
            .Where(u => u.Role != "Admin" && u.Role != "Super Admin")
            .OrderBy(u => u.Name)
            .Select(u => new
            {
                u.Id,
                Name = u.Name ?? "User",
                Email = u.Email ?? "",
                Role = u.Role ?? "User",
                Status = u.Status ?? "Active"
            })
            .ToListAsync();

        ViewBag.DemoUsers = demoUsers;
        ViewBag.DefaultUserEmail = demoUsers.FirstOrDefault()?.Email ?? "user@visioncrm.com";
        ViewBag.DefaultUserPassword = "UserPassword123!";

        return View("~/Views/Auth/UserLogin.cshtml", model);
    }

    /// <summary>
    /// Handles User Login submission.
    /// ONLY valid User credentials can log in.
    /// If an Admin enters Admin credentials on User Login, REJECT with:
    /// "Access denied. Valid User credentials are required."
    /// Route: /user/login
    /// </summary>
    [HttpPost("user/login")]
    [HttpPost("auth/user-login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Login([FromForm] LoginViewModel model)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                     Request.Headers["Accept"].ToString().Contains("application/json");

        var input = (model.UserId ?? model.Email ?? "").Trim();
        var password = model.Password ?? "";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(password))
        {
            var msg = "Please enter both User ID and Password.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("~/Views/Auth/UserLogin.cshtml", model);
        }

        // =========================================================================
        // STRICT USER LOGIN AUTHENTICATION
        // 1. Admin credentials must NEVER work on the User Login page.
        // 2. Only valid credentials belonging to a registered user are allowed.
        // 3. If unregistered or invalid password:
        //    "Invalid User ID or Password. Please register first if you don't have an account."
        // =========================================================================
        bool isAdminInput = string.Equals(input, "admin", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(input, "admin@visioncrm.com", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(input, "admin@nimbuscrm.com", StringComparison.OrdinalIgnoreCase);

        if (isAdminInput && password == "admin123")
        {
            _logger.LogWarning("Admin credentials entered on User Login portal from IP {IP}. REJECTED.", ip);
            await _auditLogService.LogAsync("UserLoginRejected", input, 0, input,
                "Access denied: Administrator entered credentials on User Login page", ip);

            const string adminDeniedMsg = "Access denied. Administrator credentials cannot be used on the User Login portal. Please use the Admin Portal.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = adminDeniedMsg });
            model.ErrorMessage = adminDeniedMsg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Auth/UserLogin.cshtml", model);
        }

        var inputLower = input.ToLower();
        long.TryParse(input, out var numericId);

        // Query database for user
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.UserId != null && u.UserId.ToLower() == inputLower) ||
            (u.Email != null && u.Email.ToLower() == inputLower) ||
            (u.Name != null && u.Name.ToLower() == inputLower) ||
            (numericId > 0 && u.Id == numericId));

        if (user == null)
        {
            _logger.LogWarning("User login failed: unregistered User ID '{Input}' from IP {IP}", input, ip);
            const string unregMsg = "Invalid User ID or Password. Please register first if you don't have an account.";
            if (isAjax) return Unauthorized(new { success = false, message = unregMsg });
            model.ErrorMessage = unregMsg;
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return View("~/Views/Auth/UserLogin.cshtml", model);
        }

        // Reject if target user account is an Admin account
        if (user.IsAdminAccount)
        {
            _logger.LogWarning("SECURITY ALERT: Admin account {Email} ({Role}) attempted to log in through User Login portal from IP {IP}. REJECTED.",
                user.Email, user.Role, ip);

            await _auditLogService.LogAsync("UserLoginRejected", user.Email, user.Id, user.Email,
                "Access denied: Administrator account entered credentials on User Login page", ip);

            const string deniedMsg = "Access denied. Administrator credentials cannot be used on the User Login portal. Please use the Admin Portal.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = deniedMsg });
            model.ErrorMessage = deniedMsg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Auth/UserLogin.cshtml", model);
        }

        var now = DateTime.UtcNow;

        // Check lockout
        if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > now)
        {
            var remaining = Math.Ceiling((user.LockoutUntil.Value - now).TotalMinutes);
            var lockMsg = $"Account is temporarily locked due to multiple failed login attempts. Please try again in {remaining} minute(s).";
            if (isAjax) return StatusCode(StatusCodes.Status423Locked, new { success = false, message = lockMsg });
            model.ErrorMessage = lockMsg;
            Response.StatusCode = StatusCodes.Status423Locked;
            return View("~/Views/Auth/UserLogin.cshtml", model);
        }

        // Verify password against user's own registered password
        bool passwordValid = false;
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            passwordValid = verifyResult == PasswordVerificationResult.Success ||
                            verifyResult == PasswordVerificationResult.SuccessRehashNeeded;
        }

        if (!passwordValid)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutUntil = now.AddMinutes(15);
                _logger.LogWarning("Account {Email} locked out due to failed attempts from IP {IP}", user.Email, ip);
            }
            await _db.SaveChangesAsync();

            const string invalidMsg = "Invalid User ID or Password. Please register first if you don't have an account.";
            if (isAjax) return Unauthorized(new { success = false, message = invalidMsg });
            model.ErrorMessage = invalidMsg;
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return View("~/Views/Auth/UserLogin.cshtml", model);
        }

        // Check account active status
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var inactiveMsg = "Your user account is inactive. Please contact system administrator.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = inactiveMsg });
            model.ErrorMessage = inactiveMsg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Auth/UserLogin.cshtml", model);
        }

        // Success: Reset lockout counters
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        // Issue opaque Bearer token for client-side API calls
        var (plainToken, tokenExpiresAt) = await _tokenService.IssueTokenAsync(user.Id, "user-web");

        // Build claims explicitly tagged with session_type = User
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name ?? "User"),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.Role ?? "User"),
            new("session_type", "User"),
            new("token_id", plainToken)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(12)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        await _auditLogService.LogAsync("UserLoginSuccess", user.Email, user.Id, user.Email, "User signed in successfully", ip);
        _logger.LogInformation("User {Email} ({Role}) logged in successfully from IP {IP}", user.Email, user.Role, ip);

        var destination = "/user/dashboard";
        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) &&
            !model.ReturnUrl.Contains("login", StringComparison.OrdinalIgnoreCase) &&
            !model.ReturnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
        {
            destination = model.ReturnUrl;
        }

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
    /// Displays the User Dashboard.
    /// Route: /user/dashboard
    /// Protected strictly by [UserAuthorize]. Admin sessions CANNOT access.
    /// </summary>
    [HttpGet("user/dashboard")]
    [UserAuthorize]
    public async Task<IActionResult> Dashboard()
    {
        ViewData["Title"] = "User Dashboard";
        ViewData["ActivePage"] = "UserDashboard";

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdStr, out var userId);
        var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var userName = dbUser?.Name ?? User.Identity?.Name ?? "User";

        // Query assigned leads
        var assignedLeads = LeadStore.Leads
            .Where(l => string.Equals(l.AssignedTo, userName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(l.AssignedTo, dbUser?.Email, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingTasksCount = await _db.WorkTasks.CountAsync(t => t.AssignedMember == userName && t.Status != "Completed");
        var activeProjectsCount = await _db.WorkProjects.CountAsync(p => p.Status != "Completed" && p.Status != "Cancelled");

        var viewModel = new UserDashboardViewModel
        {
            UserName = userName,
            UserEmail = dbUser?.Email ?? "",
            UserRole = dbUser?.Role ?? "User",
            AssignedLeadsCount = assignedLeads.Count,
            PendingTasksCount = pendingTasksCount,
            ActiveProjectsCount = activeProjectsCount,
            AssignedLeads = assignedLeads.Take(10).ToList()
        };

        return View("~/Views/User/Dashboard.cshtml", viewModel);
    }

    /// <summary>
    /// User-only REST API endpoint returning user workspace stats.
    /// Route: /api/user/dashboard/stats
    /// </summary>
    [HttpGet("api/user/dashboard/stats")]
    [UserAuthorize]
    public async Task<IActionResult> GetDashboardStats()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdStr, out var userId);
        var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var userName = dbUser?.Name ?? User.Identity?.Name ?? "User";

        var assignedLeadsCount = LeadStore.Leads.Count(l =>
            string.Equals(l.AssignedTo, userName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.AssignedTo, dbUser?.Email, StringComparison.OrdinalIgnoreCase));

        var pendingTasksCount = await _db.WorkTasks.CountAsync(t => t.AssignedMember == userName && t.Status != "Completed");

        return Ok(new
        {
            userName,
            userRole = dbUser?.Role ?? "User",
            assignedLeadsCount,
            pendingTasksCount
        });
    }

    /// <summary>
    /// User-only REST API endpoint returning current user profile.
    /// Route: /api/user/profile
    /// </summary>
    [HttpGet("api/user/profile")]
    [UserAuthorize]
    public async Task<IActionResult> GetProfile()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdStr, out var userId);
        var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (dbUser == null)
        {
            return NotFound(new { success = false, message = "User profile not found." });
        }

        return Ok(new
        {
            id = dbUser.Id,
            name = dbUser.Name,
            email = dbUser.Email,
            role = dbUser.Role,
            status = dbUser.Status
        });
    }

    /// <summary>
    /// Signs out the User, invalidates active database token, and redirects to User Login.
    /// Route: /user/logout
    /// </summary>
    [HttpGet("user/logout")]
    [HttpPost("user/logout")]
    public async Task<IActionResult> Logout()
    {
        var tokenId = User.FindFirst("token_id")?.Value;
        if (!string.IsNullOrWhiteSpace(tokenId))
        {
            await _tokenService.RevokeTokenStringAsync(tokenId);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}

public class UserDashboardViewModel
{
    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string UserRole { get; set; } = "";
    public int AssignedLeadsCount { get; set; }
    public int PendingTasksCount { get; set; }
    public int ActiveProjectsCount { get; set; }
    public List<Lead> AssignedLeads { get; set; } = new();
}
