using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// Mobile app authentication endpoints.
/// Route prefix: /api/v1/app/auth (and /api/auth)
/// </summary>
[ApiController]
[Route("api/v1/app/auth")]
[Route("api/auth")]
[Produces("application/json")]
public class AppAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppAuthController> _logger;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public AppAuthController(
        AppDbContext db,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<AppAuthController> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
        _passwordHasher = new PasswordHasher<UserRoleEntry>();
    }

    /// <summary>
    /// Dedicated Admin Login API endpoint.
    /// Strictly verifies that the credentials belong to an actual Admin or Super Admin account in the database.
    /// Rejects regular users with HTTP 403 Forbidden.
    /// </summary>
    [HttpPost("admin-login", Order = 1)]
    [HttpPost("admin-login/", Order = 2)]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> AdminLogin([FromBody] AppLoginRequest request)
    {
        return await ExecuteLogin(request, requiredRole: "admin");
    }

    /// <summary>
    /// User Login API endpoint.
    /// Authenticates users and administrators for the mobile application.
    /// </summary>
    [HttpPost("user-login", Order = 1)]
    [HttpPost("user-login/", Order = 2)]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> UserLogin([FromBody] AppLoginRequest request)
    {
        return await ExecuteLogin(request, requiredRole: null);
    }

    /// <summary>
    /// Authenticates a user or administrator with email/user ID and password, returning an opaque Bearer token.
    /// Rate-limited to 10 requests per minute per IP.
    /// Supports /api/v1/app/auth/login, /api/auth/login, /auth/login, and /login for JSON requests.
    /// </summary>
    [HttpPost("login", Order = 1)]
    [HttpPost("login/", Order = 2)]
    [HttpPost("/auth/login", Order = 3)]
    [HttpPost("/auth/login/", Order = 4)]
    [HttpPost("/login", Order = 5)]
    [HttpPost("/login/", Order = 6)]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Login([FromBody] AppLoginRequest request)
    {
        return await ExecuteLogin(request, requiredRole: null);
    }

    private async Task<IActionResult> ExecuteLogin(AppLoginRequest request, string? requiredRole)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        // Input: email (ya UserId) aur password. Input ko Trim() aur ToLower() karo, password ko trim mat karo.
        var rawInput = request.Email ?? request.UserId ?? request.Username ?? "";
        var input = rawInput.Trim().ToLowerInvariant();
        var password = request.Password ?? "";

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("Login failed: missing email/userId or password from IP {IP}", ip);
            return Unauthorized(new { success = false, message = "Invalid email or password." });
        }

        // User dhundo: Email, UserId ya Name (case-insensitive) se. Sirf ek hi match use karo.
        int.TryParse(input, out var numericId);
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.Email != null && u.Email.ToLower() == input) ||
            (u.UserId != null && u.UserId.ToLower() == input) ||
            (u.Name != null && u.Name.ToLower() == input) ||
            (numericId > 0 && u.Id == numericId));

        if (user == null)
        {
            _logger.LogWarning("Login failed: user not found for email/userId '{Input}' from IP {IP}", input, ip);
            return Unauthorized(new { success = false, message = "Invalid email or password." });
        }

        // If hitting dedicated admin endpoint, ensure user has Admin authority
        if (string.Equals(requiredRole, "admin", StringComparison.OrdinalIgnoreCase) && !user.IsAdminAccount)
        {
            _logger.LogWarning("Admin login rejected for non-admin user '{Email}' from IP {IP}", user.Email, ip);
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Access denied. Valid Admin credentials are required." });
        }

        // Status check: agar user Inactive hai to 403 do with clear message ("Your account is inactive").
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Login rejected: inactive account for '{Email}' (Status: {Status}) from IP {IP}", user.Email, user.Status, ip);
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Your account is inactive." });
        }

        // Lockout: LockoutUntil aur FailedLoginCount ka existing logic rakho.
        var now = DateTime.UtcNow;
        if (user.LockoutUntil.HasValue && user.LockoutUntil > now)
        {
            _logger.LogWarning("Login rejected: account '{Email}' is temporarily locked until {LockoutUntil} from IP {IP}", user.Email, user.LockoutUntil, ip);
            return StatusCode(StatusCodes.Status423Locked, new { success = false, message = "Account is temporarily locked. Please try again later." });
        }

        // Password verify:
        // Supports:
        // 1. PasswordHasher<UserRoleEntry> (PBKDF2)
        // 2. Legacy Plain-text
        // 3. Legacy SHA-256 hex
        bool passwordValid = false;
        bool needsRehash = false;

        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verifyResult == PasswordVerificationResult.Success)
            {
                passwordValid = true;
            }
            else if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                passwordValid = true;
                needsRehash = true;
            }
            else if (string.Equals(user.PasswordHash, password, StringComparison.Ordinal))
            {
                // Plain-text match (legacy migration)
                passwordValid = true;
                needsRehash = true;
            }
            else
            {
                // SHA-256 match (legacy)
                var sha256Hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
                if (string.Equals(user.PasswordHash, sha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    passwordValid = true;
                    needsRehash = true;
                }
            }
        }
        else if (user.IsAdminAccount && password == "admin123")
        {
            // Initial uninitialized admin password bootstrap
            passwordValid = true;
            needsRehash = true;
        }

        if (!passwordValid)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutUntil = now.AddMinutes(15);
                _logger.LogWarning("Account '{Email}' locked out for 15 minutes due to 5 consecutive failed login attempts from IP {IP}", user.Email, ip);
            }
            await _db.SaveChangesAsync();

            _logger.LogWarning("Login failed: wrong password for '{Email}' (Attempt #{Attempts}) from IP {IP}", user.Email, user.FailedLoginCount, ip);
            return Unauthorized(new { success = false, message = "Invalid email or password." });
        }

        // Sahi login par count reset karo
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;

        // Upgrade hash to latest format if needed
        if (needsRehash)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            _logger.LogInformation("Upgraded password hash for '{Email}' to standard PasswordHasher format", user.Email);
        }

        await _db.SaveChangesAsync();

        // Success par AccessTokens table me token banao (TokenHash save karo, plain token nahi)
        var (plainToken, expiresAt) = await _tokenService.IssueTokenAsync(user.Id, request.DeviceId);

        _logger.LogInformation("Login successful for '{Email}' (ID: {Id}, Role: {Role}) from IP {IP}, Device: {Device}", user.Email, user.Id, user.Role, ip, request.DeviceId);

        // Success JSON return format
        return Ok(new
        {
            success = true,
            message = "Login successful.",
            data = new
            {
                token = plainToken,
                expiresAt = expiresAt.ToString("o"),
                user = new
                {
                    id = user.Id,
                    name = user.Name ?? "",
                    email = user.Email ?? "",
                    role = user.Role ?? ""
                }
            }
        });
    }

    /// <summary>
    /// Revokes the current token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<IActionResult> Logout()
    {
        var tokenIdClaim = User.FindFirst("token_id")?.Value;
        if (int.TryParse(tokenIdClaim, out var tokenId))
        {
            await _tokenService.RevokeTokenAsync(tokenId);
        }

        return NoContent();
    }

    /// <summary>
    /// Revokes all active tokens for the current user across all devices.
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<IActionResult> LogoutAll()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            await _tokenService.RevokeAllUserTokensAsync(userId);
        }

        return NoContent();
    }

    /// <summary>
    /// Returns the currently authenticated user's profile.
    /// </summary>
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<IActionResult> GetMe()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new AppApiError { Message = "Unauthorized. Missing or invalid token." });
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new AppApiError { Message = "User no longer exists or is inactive." });
        }

        return Ok(new AppUserProfileDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role
        });
    }

    /// <summary>
    /// Changes the current user's password and revokes all other active tokens.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<IActionResult> ChangePassword([FromBody] AppChangePasswordRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentTokenIdClaim = User.FindFirst("token_id")?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new AppApiError { Message = "Unauthorized." });
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return Unauthorized(new AppApiError { Message = "User not found." });
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
            _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.OldPassword) != PasswordVerificationResult.Success)
        {
            return BadRequest(new AppApiError { Message = "Invalid current password." });
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword.Length < 8 ||
            !request.NewPassword.Any(char.IsLetter) ||
            !request.NewPassword.Any(char.IsDigit))
        {
            return BadRequest(new AppApiError
            {
                Message = "New password must be at least 8 characters long and contain at least one letter and one number."
            });
        }

        var adminPw = _configuration["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.NewPassword.Trim(), adminPw, StringComparison.Ordinal))
        {
            return BadRequest(new AppApiError
            {
                Message = "User password must be different from the administrator password."
            });
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        await _db.SaveChangesAsync();

        if (int.TryParse(currentTokenIdClaim, out var currentTokenId))
        {
            await _tokenService.RevokeOtherUserTokensAsync(userId, currentTokenId);
        }

        return NoContent();
    }

    /// <summary>
    /// Optional self-registration endpoint (enabled if Auth:RegistrationMode is Open or ApprovalRequired).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Register([FromBody] AppRegisterRequest request)
    {
        var registrationMode = _configuration.GetValue<string>("Auth:RegistrationMode") ?? "Disabled";
        if (string.Equals(registrationMode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppApiError { Message = "Registration is currently disabled." });
        }

        var name = request.Name?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new AppApiError { Message = "Name and email are required." });
        }

        if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower()))
        {
            return Conflict(new AppApiError { Message = "A user with this email address already exists." });
        }

        if (string.IsNullOrWhiteSpace(request.Password) ||
            request.Password.Length < 8 ||
            !request.Password.Any(char.IsLetter) ||
            !request.Password.Any(char.IsDigit))
        {
            return BadRequest(new AppApiError
            {
                Message = "Password must be at least 8 characters long and contain at least one letter and one number."
            });
        }

        var adminSeedPw = _configuration["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        if (string.Equals(request.Password.Trim(), adminSeedPw, StringComparison.Ordinal))
        {
            return BadRequest(new AppApiError
            {
                Message = "User password must be different from the administrator password."
            });
        }

        // Prevent self-escalation: End-users can NEVER register as Admin
        if (!string.IsNullOrWhiteSpace(request.Role) &&
            (string.Equals(request.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(request.Role, "Super Admin", StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AppApiError
            {
                Message = "Access denied: Users cannot register or assign themselves the Admin role."
            });
        }

        var status = string.Equals(registrationMode, "ApprovalRequired", StringComparison.OrdinalIgnoreCase)
            ? "Inactive"
            : "Active";

        var newUser = new UserRoleEntry
        {
            Name = name,
            Email = email,
            Role = "Sales Executive",
            Status = status,
            IsAuthorizedUser = true
        };

        newUser.PasswordHash = _passwordHasher.HashPassword(newUser, request.Password);

        _db.Users.Add(newUser);
        await _db.SaveChangesAsync();

        return Ok(new AppUserProfileDto
        {
            Id = newUser.Id,
            Name = newUser.Name,
            Email = newUser.Email,
            Role = newUser.Role
        });
    }
}
