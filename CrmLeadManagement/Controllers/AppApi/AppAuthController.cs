using System.Security.Claims;
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
    [HttpPost("admin-login")]
    [HttpPost("/api/auth/admin-login")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> AdminLogin([FromBody] AppLoginRequest request)
    {
        request.Role = "admin";
        return await ExecuteLogin(request, requiredRole: "admin");
    }

    /// <summary>
    /// Dedicated User Login API endpoint.
    /// Strictly verifies that the credentials belong to an authorized User account in the database.
    /// Rejects Admin accounts with HTTP 403 Forbidden.
    /// </summary>
    [HttpPost("user-login")]
    [HttpPost("/api/auth/user-login")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> UserLogin([FromBody] AppLoginRequest request)
    {
        request.Role = "user";
        return await ExecuteLogin(request, requiredRole: "user");
    }

    /// <summary>
    /// Authenticates a user with email and password, returning an opaque Bearer token.
    /// Rate-limited to 10 requests per minute per IP.
    /// Supports /api/v1/app/auth/login, /api/auth/login, /auth/login, and /login for JSON requests.
    /// </summary>
    [HttpPost("login")]
    [HttpPost("/auth/login")]
    [HttpPost("/login")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Login([FromBody] AppLoginRequest request)
    {
        var targetRole = request.Role ?? request.Portal;
        return await ExecuteLogin(request, requiredRole: targetRole);
    }

    private async Task<IActionResult> ExecuteLogin(AppLoginRequest request, string? requiredRole)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var email = (request.Email ?? request.UserId ?? "").Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized(new AppApiError { Message = "Invalid email or password." });
        }

        int.TryParse(email, out var numericId);
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (numericId > 0 && u.Id == numericId) ||
            (u.Email != null && u.Email.ToLower() == email.ToLower()));

        if (user == null)
        {
            _logger.LogWarning("Failed login for unknown email: {Email} from IP: {IP}, Device: {Device}", email, ip, request.DeviceId);
            return Unauthorized(new AppApiError { Message = "Invalid email or password." });
        }

        // Role check: The mobile app API is only for field and operational team roles, NOT for Admin.
        var isAppApi = Request.Path.Value?.Contains("/app", StringComparison.OrdinalIgnoreCase) == true &&
                       !Request.Path.Value.Contains("admin-login", StringComparison.OrdinalIgnoreCase);
        if (isAppApi && user.IsAdminAccount && !string.Equals(requiredRole, "admin", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Admin login rejected on mobile app API for user {UserId} ({Email}) from IP: {IP}", user.Id, user.Email, ip);
            return StatusCode(StatusCodes.Status403Forbidden, new AppApiError
            {
                Message = "Admin is not allowed to log in to the mobile app. This app is for Sales Manager and other team roles only. Please use the web portal."
            });
        }

        // =====================================================================
        // STRICT ROLE-BASED LOGIN SEGREGATION
        // =====================================================================
        if (string.Equals(requiredRole, "admin", StringComparison.OrdinalIgnoreCase))
        {
            if (!user.IsAdminAccount)
            {
                _logger.LogWarning("Admin login rejected for non-admin user {Email} ({Role}) from IP: {IP}", user.Email, user.Role, ip);
                return StatusCode(StatusCodes.Status403Forbidden, new AppApiError
                {
                    Message = "Access denied. Valid Admin credentials are required."
                });
            }
        }
        else if (string.Equals(requiredRole, "user", StringComparison.OrdinalIgnoreCase))
        {
            if (!user.IsUserAccount)
            {
                _logger.LogWarning("User login rejected for Admin user {Email} ({Role}) from IP: {IP}", user.Email, user.Role, ip);
                return StatusCode(StatusCodes.Status403Forbidden, new AppApiError
                {
                    Message = "Access denied. Valid User credentials are required."
                });
            }
        }

        var now = DateTime.UtcNow;
        if (user.LockoutUntil.HasValue && user.LockoutUntil > now)
        {
            _logger.LogWarning("Login attempt on locked account User {UserId} ({Email}) from IP: {IP}", user.Id, user.Email, ip);
            return StatusCode(StatusCodes.Status423Locked, new AppApiError
            {
                Message = "Account is temporarily locked due to multiple failed login attempts. Please try again later."
            });
        }

        var adminPassword = _configuration["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";

        bool passwordValid = false;
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            passwordValid = verifyResult == PasswordVerificationResult.Success ||
                            verifyResult == PasswordVerificationResult.SuccessRehashNeeded;
        }
        else
        {
            // Fallback for sample user record before explicit password hash was generated
            var firstWord = (user.Name ?? "User").Split(' ')[0];
            var userInitialPassword = $"{firstWord}@123!";
            if (request.Password == userInitialPassword)
            {
                passwordValid = true;
                user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            }
        }

        if (!passwordValid)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutUntil = now.AddMinutes(15);
                _logger.LogWarning("User {UserId} ({Email}) locked out for 15 minutes due to 5 failed login attempts from IP: {IP}", user.Id, user.Email, ip);
            }
            else
            {
                _logger.LogWarning("Failed password attempt #{Count} for user {UserId} ({Email}) from IP: {IP}", user.FailedLoginCount, user.Id, user.Email, ip);
            }

            await _db.SaveChangesAsync();
            return Unauthorized(new AppApiError { Message = "Invalid email or password." });
        }

        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Login attempted on inactive account User {UserId} ({Email}) from IP: {IP}", user.Id, user.Email, ip);
            return StatusCode(StatusCodes.Status403Forbidden, new AppApiError
            {
                Message = "User account is inactive. Please contact an administrator."
            });
        }

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        var (plainToken, expiresAt) = await _tokenService.IssueTokenAsync(user.Id, request.DeviceId);

        _logger.LogInformation("Successful login for user {UserId} ({Email}) from IP: {IP}, Device: {Device}", user.Id, user.Email, ip, request.DeviceId);

        return Ok(new AppLoginResponse
        {
            Token = plainToken,
            TokenType = "Bearer",
            ExpiresAt = expiresAt,
            User = new AppUserProfileDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role
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
