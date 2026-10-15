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
/// Serves the browser login page at /login, /auth/login, and /account/login.
/// </summary>
public class AuthController : Controller
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public AuthController(
        AppDbContext db,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
        _passwordHasher = new PasswordHasher<UserRoleEntry>();
    }

    /// <summary>
    /// Displays the login page.
    /// Routes supported: /login, /auth/login, /account/login
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("login")]
    [Route("auth/login")]
    [Route("account/login")]
    public IActionResult Login(string? returnUrl = null)
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

        return View(model);
    }

    /// <summary>
    /// Handles user login submission (supports standard HTML form post).
    /// </summary>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    [Route("login")]
    [Route("auth/login")]
    [Route("account/login")]
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

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.Email != null && u.Email.ToLower() == inputLower) ||
            (u.Name != null && u.Name.ToLower() == inputLower) ||
            (numericId > 0 && u.Id == numericId));

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

        // Verify password
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

        var destination = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action("List", "Lead") ?? "/Lead/List";

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
    /// Signs out the user, clears authentication cookie, and redirects to login page.
    /// Routes supported: /logout, /auth/logout, /account/logout
    /// </summary>
    [HttpGet]
    [HttpPost]
    [Route("logout")]
    [Route("auth/logout")]
    [Route("account/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Auth");
    }
}
