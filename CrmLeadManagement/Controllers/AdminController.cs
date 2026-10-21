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
/// Dedicated Administrator Controller.
/// Manages Admin authentication, Admin session handling, and the Admin Dashboard.
/// Enforces strict database-verified role authorization on every action.
/// </summary>
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public AdminController(
        AppDbContext db,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<AdminController> logger,
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
    /// Root admin route. Redirects authenticated Admins to the dashboard, otherwise to the admin login.
    /// Route: /admin
    /// </summary>
    [HttpGet("admin")]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out var userId))
            {
                var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (dbUser != null && dbUser.IsAdminAccount && string.Equals(dbUser.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction(nameof(Dashboard));
                }
            }
        }

        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// Displays the dedicated Admin Login page.
    /// Route: /admin/login
    /// </summary>
    [HttpGet("admin/login")]
    [HttpGet("auth/admin-login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // If already authenticated as an active Admin, redirect directly to Admin Dashboard
        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out var userId))
            {
                var dbUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (dbUser != null && dbUser.IsAdminAccount && string.Equals(dbUser.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
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
            LoginType = "admin"
        };

        ViewBag.AdminEmail = "admin";
        ViewBag.AdminPassword = "admin123";

        return View("~/Views/Auth/AdminLogin.cshtml", model);
    }

    /// <summary>
    /// Handles Admin Login submission.
    /// ONLY authorized Admin credentials (Admin ID: admin, Password: admin123) can log in.
    /// Regular user credentials must NEVER allow access to the Admin Portal.
    /// If incorrect admin credentials, display: "Invalid Admin ID or Password."
    /// Route: /admin/login
    /// </summary>
    [HttpPost("admin/login")]
    [HttpPost("auth/admin-login")]
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
            var msg = "Please enter both Admin ID and Password.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("~/Views/Auth/AdminLogin.cshtml", model);
        }

        // =========================================================================
        // STRICT ADMIN AUTHENTICATION
        // Admin login must work ONLY with credentials:
        // Admin ID: admin
        // Password: admin123
        // Regular user credentials must NEVER allow access to the Admin Portal.
        // =========================================================================
        bool isAdminId = string.Equals(input, "admin", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(input, "admin@visioncrm.com", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(input, "admin@nimbuscrm.com", StringComparison.OrdinalIgnoreCase);

        if (!isAdminId || password != "admin123")
        {
            _logger.LogWarning("Admin login failed for input '{Input}' from IP {IP}", input, ip);
            var msg = "Invalid Admin ID or Password.";
            if (isAjax) return Unauthorized(new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return View("~/Views/Auth/AdminLogin.cshtml", model);
        }

        // Query database for admin record
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.UserId != null && u.UserId.ToLower() == "admin") ||
            (u.Email != null && (u.Email.ToLower() == "admin" || u.Email.ToLower() == "admin@visioncrm.com" || u.Email.ToLower() == "admin@nimbuscrm.com")) ||
            u.Role == "Admin");

        if (user == null)
        {
            user = new UserRoleEntry
            {
                UserId = "admin",
                Name = "Administrator",
                Email = "admin@visioncrm.com",
                Role = "Admin",
                Status = "Active",
                IsAuthorizedUser = false
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, "admin123");
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Provisioned Admin account for Admin ID: admin.");
        }
        else
        {
            user.UserId = "admin";
            user.Role = "Admin";
            user.Status = "Active";
            user.IsAuthorizedUser = false;
            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, "admin123") != PasswordVerificationResult.Success)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, "admin123");
                await _db.SaveChangesAsync();
            }
        }

        var now = DateTime.UtcNow;

        // Check account active status
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var msg = "Administrator account is inactive. Please contact system management.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Auth/AdminLogin.cshtml", model);
        }

        // Reset lockout for admin
        user.LockoutUntil = null;
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        // Issue opaque Bearer token for client-side API calls
        var (plainToken, tokenExpiresAt) = await _tokenService.IssueTokenAsync(user.Id, "admin-web");

        // Build claims explicitly tagged with session_type = Admin
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name ?? "Administrator"),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, "Admin"),
            new("session_type", "Admin"),
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

        await _auditLogService.LogAsync("AdminLoginSuccess", user.Email, user.Id, user.Email, "Admin signed in successfully", ip);
        _logger.LogInformation("Administrator {Email} logged in successfully from IP {IP}", user.Email, ip);

        var destination = "/admin/dashboard";
        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) &&
            !model.ReturnUrl.Contains("login", StringComparison.OrdinalIgnoreCase) &&
            model.ReturnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
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
                    role = "Admin"
                }
            });
        }

        return Redirect(destination);
    }

    /// <summary>
    /// Displays the Admin Dashboard.
    /// Route: /admin/dashboard
    /// Protected strictly by [AdminAuthorize]. User credentials or non-admin sessions CANNOT access.
    /// </summary>
    [HttpGet("admin/dashboard")]
    [AdminAuthorize]
    public async Task<IActionResult> Dashboard()
    {
        ViewData["Title"] = "Admin Dashboard";
        ViewData["ActivePage"] = "AdminDashboard";

        // Query system summary statistics
        var totalUsers = await _db.Users.CountAsync();
        var activeAdmins = await _db.Users.CountAsync(u => (u.Role == "Admin" || u.Role == "Super Admin") && u.Status == "Active");
        var totalLeads = await _db.Leads.CountAsync();
        var totalProjects = await _db.WorkProjects.CountAsync();
        var totalInvoices = await _db.WorkInvoices.CountAsync();
        var recentAuditLogs = await _db.AuditLogs.OrderByDescending(a => a.Timestamp).Take(8).ToListAsync();

        var viewModel = new AdminDashboardViewModel
        {
            TotalUsers = totalUsers,
            ActiveAdmins = activeAdmins,
            TotalLeads = totalLeads,
            TotalProjects = totalProjects,
            TotalInvoices = totalInvoices,
            RecentAuditLogs = recentAuditLogs
        };

        return View("~/Views/Admin/Dashboard.cshtml", viewModel);
    }

    /// <summary>
    /// Admin-only REST API endpoint returning system stats.
    /// Route: /api/admin/dashboard/stats
    /// </summary>
    [HttpGet("api/admin/dashboard/stats")]
    [AdminAuthorize]
    public async Task<IActionResult> GetDashboardStats()
    {
        var totalUsers = await _db.Users.CountAsync();
        var activeAdmins = await _db.Users.CountAsync(u => (u.Role == "Admin" || u.Role == "Super Admin") && u.Status == "Active");
        var totalLeads = await _db.Leads.CountAsync();
        var totalProjects = await _db.WorkProjects.CountAsync();
        var totalInvoices = await _db.WorkInvoices.CountAsync();

        return Ok(new
        {
            totalUsers,
            activeAdmins,
            totalLeads,
            totalProjects,
            totalInvoices
        });
    }

    /// <summary>
    /// Admin-only REST API endpoint returning system audit logs.
    /// Route: /api/admin/audit-logs
    /// </summary>
    [HttpGet("api/admin/audit-logs")]
    [AdminAuthorize]
    public async Task<IActionResult> GetAuditLogs()
    {
        var logs = await _db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Take(50)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.Action,
                a.PerformedBy,
                a.TargetUserEmail,
                a.Details,
                a.IpAddress
            })
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// Signs out the Administrator, invalidates active database token, and redirects to Admin Login.
    /// Route: /admin/logout
    /// </summary>
    [HttpGet("admin/logout")]
    [HttpPost("admin/logout")]
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

public class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int ActiveAdmins { get; set; }
    public int TotalLeads { get; set; }
    public int TotalProjects { get; set; }
    public int TotalInvoices { get; set; }
    public List<AuditLog> RecentAuditLogs { get; set; } = new();
}
