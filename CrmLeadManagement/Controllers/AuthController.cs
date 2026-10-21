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
    public async Task<IActionResult> Login(string? returnUrl = null, [FromQuery] string? portal = null, [FromQuery] string? registered = null)
    {
        // If the user is already authenticated via cookie, redirect directly to their dashboard
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Super Admin");
            return Redirect(isAdmin ? "/admin/dashboard" : "/user/dashboard");
        }

        var isTargetAdmin = string.Equals(portal, "admin", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase));

        var model = new LoginViewModel
        {
            ReturnUrl = returnUrl,
            RememberMe = true,
            LoginType = isTargetAdmin ? "admin" : "user"
        };

        if (string.Equals(registered, "true", StringComparison.OrdinalIgnoreCase))
        {
            ViewBag.SuccessMessage = "Account registered successfully! Please sign in with your User ID and password.";
        }

        ViewBag.AdminEmail = "admin";
        ViewBag.AdminPassword = "admin123";

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
        var targetPortal = (model.LoginType ?? "user").Trim().ToLowerInvariant();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(password))
        {
            var msg = targetPortal == "admin"
                ? "Please enter both Admin ID and Password."
                : "Please enter both User ID and Password.";
            if (isAjax)
            {
                return BadRequest(new { success = false, message = msg });
            }
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var now = DateTime.UtcNow;

        bool isAdminId = string.Equals(input, "admin", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(input, "admin@visioncrm.com", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(input, "admin@nimbuscrm.com", StringComparison.OrdinalIgnoreCase);

        // Entering Admin credentials (admin / admin123) MUST strictly open the Admin Panel (/admin/dashboard)
        if (isAdminId)
        {
            targetPortal = "admin";
        }

        // =========================================================================
        // 1. ADMIN PORTAL LOGIN
        // =========================================================================
        if (targetPortal == "admin")
        {

            // Admin login must work ONLY with credentials: Admin ID: admin, Password: admin123
            if (!isAdminId || password != "admin123")
            {
                _logger.LogWarning("Admin login failed for input '{Input}' from IP {IP}", input, ip);
                const string adminFailMsg = "Invalid Admin ID or Password.";
                if (isAjax) return Unauthorized(new { success = false, message = adminFailMsg });
                model.ErrorMessage = adminFailMsg;
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return View(model);
            }

            var adminUser = await _db.Users.FirstOrDefaultAsync(u =>
                (u.UserId != null && u.UserId.ToLower() == "admin") ||
                (u.Email != null && (u.Email.ToLower() == "admin" || u.Email.ToLower() == "admin@visioncrm.com" || u.Email.ToLower() == "admin@nimbuscrm.com")) ||
                u.Role == "Admin");

            if (adminUser == null)
            {
                adminUser = new UserRoleEntry
                {
                    UserId = "admin",
                    Name = "Administrator",
                    Email = "admin@visioncrm.com",
                    Role = "Admin",
                    Status = "Active",
                    IsAuthorizedUser = false
                };
                adminUser.PasswordHash = _passwordHasher.HashPassword(adminUser, "admin123");
                _db.Users.Add(adminUser);
                await _db.SaveChangesAsync();
            }
            else
            {
                adminUser.UserId = "admin";
                adminUser.Role = "Admin";
                adminUser.Status = "Active";
                if (string.IsNullOrWhiteSpace(adminUser.PasswordHash) ||
                    _passwordHasher.VerifyHashedPassword(adminUser, adminUser.PasswordHash, "admin123") != PasswordVerificationResult.Success)
                {
                    adminUser.PasswordHash = _passwordHasher.HashPassword(adminUser, "admin123");
                    await _db.SaveChangesAsync();
                }
            }

            if (!string.Equals(adminUser.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                var inactiveMsg = "Administrator account is inactive. Please contact system management.";
                if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = inactiveMsg });
                model.ErrorMessage = inactiveMsg;
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return View(model);
            }

            adminUser.FailedLoginCount = 0;
            adminUser.LockoutUntil = null;
            adminUser.LastLoginAt = now;
            await _db.SaveChangesAsync();

            var (adminPlainToken, adminTokenExpiresAt) = await _tokenService.IssueTokenAsync(adminUser.Id, "admin-web");

            var adminClaims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, adminUser.Id.ToString()),
                new(ClaimTypes.Name, adminUser.Name ?? "Administrator"),
                new(ClaimTypes.Email, adminUser.Email ?? "admin@visioncrm.com"),
                new(ClaimTypes.Role, "Admin"),
                new("session_type", "Admin"),
                new("token_id", adminPlainToken)
            };

            var adminClaimsIdentity = new ClaimsIdentity(adminClaims, CookieAuthenticationDefaults.AuthenticationScheme);
            var adminAuthProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(12)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(adminClaimsIdentity),
                adminAuthProperties);

            await _auditLogService.LogAsync("AdminLoginSuccess", adminUser.Email, adminUser.Id, adminUser.Email, "Admin portal login", ip);
            _logger.LogInformation("Administrator logged in successfully via web from IP {IP}", ip);

            var adminDest = "/admin/dashboard";
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) &&
                !returnUrl.Contains("login", StringComparison.OrdinalIgnoreCase) &&
                returnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            {
                adminDest = returnUrl;
            }

            if (isAjax)
            {
                return Ok(new
                {
                    success = true,
                    redirectUrl = adminDest,
                    token = adminPlainToken,
                    expiresAt = adminTokenExpiresAt,
                    user = new { id = adminUser.Id, name = adminUser.Name, email = adminUser.Email, role = "Admin" }
                });
            }

            return Redirect(adminDest);
        }

        // =========================================================================
        // 2. USER PORTAL LOGIN
        // =========================================================================
        // Admin credentials must NEVER work on the User Login page
        bool isAdminAttempt = string.Equals(input, "admin", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(input, "admin@visioncrm.com", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(input, "admin@nimbuscrm.com", StringComparison.OrdinalIgnoreCase);

        if (isAdminAttempt && password == "admin123")
        {
            _logger.LogWarning("Admin credentials entered on User Login portal from IP {IP}", ip);
            await _auditLogService.LogAsync("UserLoginRejected", input, 0, input,
                "Access denied: Administrator entered credentials on User Login page", ip);

            const string adminDeniedMsg = "Access denied. Administrator credentials cannot be used on the User Login portal. Please use the Admin Portal.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = adminDeniedMsg });
            model.ErrorMessage = adminDeniedMsg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View(model);
        }

        int.TryParse(input, out var numericId);
        var inputLower = input.ToLower();

        // Query database for registered user
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
            return View(model);
        }

        // Reject admin account logging in through user portal
        if (user.IsAdminAccount)
        {
            _logger.LogWarning("User login rejected: Admin user {Email} ({Role}) from IP {IP}", user.Email, user.Role, ip);
            await _auditLogService.LogAsync("UserLoginRejected", user.Email, user.Id, user.Email,
                "Access denied: Administrator entered credentials on User Login page", ip);

            const string deniedMsg = "Access denied. Administrator credentials cannot be used on the User Login portal. Please use the Admin Portal.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = deniedMsg });
            model.ErrorMessage = deniedMsg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View(model);
        }

        // Check account lockout
        if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > now)
        {
            var remainingMinutes = Math.Ceiling((user.LockoutUntil.Value - now).TotalMinutes);
            _logger.LogWarning("Web login attempt on locked account {Email} from IP {IP}", user.Email, ip);
            var lockMsg = $"Account is temporarily locked due to multiple failed login attempts. Please try again in {remainingMinutes} minute(s).";
            if (isAjax) return StatusCode(StatusCodes.Status423Locked, new { success = false, message = lockMsg });
            model.ErrorMessage = lockMsg;
            Response.StatusCode = StatusCodes.Status423Locked;
            return View(model);
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
                _logger.LogWarning("User {Email} locked out for 15 minutes due to 5 failed attempts from IP {IP}", user.Email, ip);
            }
            await _db.SaveChangesAsync();

            const string invalidMsg = "Invalid User ID or Password. Please register first if you don't have an account.";
            if (isAjax) return Unauthorized(new { success = false, message = invalidMsg });
            model.ErrorMessage = invalidMsg;
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return View(model);
        }

        // Check account active status
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Web login attempted on inactive account {Email} from IP {IP}", user.Email, ip);
            var inactiveMsg = "Your account is currently inactive. Please contact your system administrator.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = inactiveMsg });
            model.ErrorMessage = inactiveMsg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View(model);
        }

        // Login success: reset lockout and update LastLoginAt
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        var (plainToken, tokenExpiresAt) = await _tokenService.IssueTokenAsync(user.Id, "user-web");

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

        _logger.LogInformation("User {Email} logged in successfully via web from IP {IP}", user.Email, ip);
        await _auditLogService.LogAsync("UserLoginSuccess", user.Email, user.Id, user.Email, "User portal login", ip);

        var destination = "/user/dashboard";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) &&
            !returnUrl.Contains("login", StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
        {
            destination = returnUrl;
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
                    role = user.Role ?? "User"
                }
            });
        }

        return Redirect(destination);
    }

    /// <summary>
    /// Displays the User Registration page.
    /// Route: /register, /auth/register, /user/register
    /// </summary>
    [HttpGet("register")]
    [HttpGet("auth/register")]
    [HttpGet("user/register")]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Super Admin");
            return Redirect(isAdmin ? "/admin/dashboard" : "/user/dashboard");
        }

        return View("~/Views/Auth/Register.cshtml", new RegisterViewModel());
    }

    /// <summary>
    /// Handles User Registration submission.
    /// Strictly assigns the 'User' role. Admin role can NEVER be granted via registration.
    /// Validates unique User ID and Email.
    /// Hashes passwords securely.
    /// </summary>
    [HttpPost("register")]
    [HttpPost("auth/register")]
    [HttpPost("user/register")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Register([FromForm] RegisterViewModel model)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                     Request.Headers["Accept"].ToString().Contains("application/json");

        var userId = (model.UserId ?? "").Trim();
        var name = (model.Name ?? "").Trim();
        var email = (model.Email ?? "").Trim().ToLowerInvariant();
        var password = model.Password ?? "";
        var confirmPassword = model.ConfirmPassword ?? "";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            var msg = "All fields (User ID, Name, Email, Password) are required.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("~/Views/Auth/Register.cshtml", model);
        }

        if (password != confirmPassword)
        {
            var msg = "Password and confirmation password do not match.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("~/Views/Auth/Register.cshtml", model);
        }

        if (password.Length < 6)
        {
            var msg = "Password must be at least 6 characters long.";
            if (isAjax) return BadRequest(new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("~/Views/Auth/Register.cshtml", model);
        }

        // The identifier 'admin' is strictly reserved for the Administrator account
        if (string.Equals(userId, "admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(email, "admin@visioncrm.com", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(email, "admin@nimbuscrm.com", StringComparison.OrdinalIgnoreCase))
        {
            var msg = "The User ID 'admin' is reserved for system administration. Please choose a different User ID.";
            if (isAjax) return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Auth/Register.cshtml", model);
        }

        // Prevent duplicate User IDs or duplicate Emails
        var isDuplicate = await _db.Users.AnyAsync(u =>
            (u.UserId != null && u.UserId.ToLower() == userId.ToLower()) ||
            (u.Email != null && u.Email.ToLower() == email) ||
            (u.UserId != null && u.UserId.ToLower() == email) ||
            (u.Email != null && u.Email.ToLower() == userId.ToLower()));

        if (isDuplicate)
        {
            _logger.LogWarning("Registration failed: duplicate User ID or Email '{UserId}' / '{Email}' from IP {IP}", userId, email, ip);
            var msg = "A user with this User ID or Email already exists. Please choose a different User ID or log in.";
            if (isAjax) return Conflict(new { success = false, message = msg });
            model.ErrorMessage = msg;
            Response.StatusCode = StatusCodes.Status409Conflict;
            return View("~/Views/Auth/Register.cshtml", model);
        }

        // Create new user with STRICT 'User' role (never Admin)
        var newUser = new UserRoleEntry
        {
            UserId = userId,
            Name = name,
            Email = email,
            Role = "User",
            Status = "Active",
            IsAuthorizedUser = true,
            CreatedAt = DateTime.UtcNow
        };

        newUser.PasswordHash = _passwordHasher.HashPassword(newUser, password);
        _db.Users.Add(newUser);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New user registered successfully: User ID '{UserId}', Email '{Email}' from IP {IP}", userId, email, ip);
        await _auditLogService.LogAsync("UserRegistered", email, newUser.Id, email, $"Self-registered User ID: {userId}", ip);

        var successMessage = "Registration successful! Please sign in with your User ID and password.";
        if (isAjax)
        {
            return Ok(new
            {
                success = true,
                message = successMessage,
                redirectUrl = "/login?portal=user&registered=true",
                user = new { id = newUser.Id, userId = newUser.UserId, name = newUser.Name, email = newUser.Email, role = newUser.Role }
            });
        }

        return Redirect("/login?portal=user&registered=true");
    }

    /// <summary>
    /// REST JSON User Registration API endpoint.
    /// Route: /api/auth/register
    /// </summary>
    [HttpPost("api/auth/register")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> ApiRegister([FromBody] RegisterViewModel model)
    {
        var userId = (model?.UserId ?? "").Trim();
        var name = (model?.Name ?? "").Trim();
        var email = (model?.Email ?? "").Trim().ToLowerInvariant();
        var password = model?.Password ?? "";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return BadRequest(new { success = false, message = "All fields (User ID, Name, Email, Password) are required." });
        }

        if (password.Length < 6)
        {
            return BadRequest(new { success = false, message = "Password must be at least 6 characters long." });
        }

        if (string.Equals(userId, "admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(email, "admin@visioncrm.com", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "The User ID 'admin' is reserved for system administration." });
        }

        var isDuplicate = await _db.Users.AnyAsync(u =>
            (u.UserId != null && u.UserId.ToLower() == userId.ToLower()) ||
            (u.Email != null && u.Email.ToLower() == email));

        if (isDuplicate)
        {
            return Conflict(new { success = false, message = "A user with this User ID or Email already exists." });
        }

        var newUser = new UserRoleEntry
        {
            UserId = userId,
            Name = name,
            Email = email,
            Role = "User",
            Status = "Active",
            IsAuthorizedUser = true,
            CreatedAt = DateTime.UtcNow
        };

        newUser.PasswordHash = _passwordHasher.HashPassword(newUser, password);
        _db.Users.Add(newUser);
        await _db.SaveChangesAsync();

        await _auditLogService.LogAsync("ApiUserRegistered", email, newUser.Id, email, $"API registered User ID: {userId}", ip);

        return Ok(new
        {
            success = true,
            message = "Registration successful! You can now log in.",
            user = new { id = newUser.Id, userId = newUser.UserId, name = newUser.Name, email = newUser.Email, role = newUser.Role }
        });
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
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) &&
            !returnUrl.Contains("/change-password") && !returnUrl.Contains("/login"))
        {
            if (user.IsAdminAccount && !returnUrl.StartsWith("/user", StringComparison.OrdinalIgnoreCase))
            {
                return returnUrl;
            }
            if (user.IsUserAccount && !returnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            {
                return returnUrl;
            }
        }

        if (user.IsAdminAccount)
        {
            return "/admin/dashboard";
        }

        return "/user/dashboard";
    }

    /// <summary>
    /// Signs out the user, invalidates active session and bearer tokens in database, and redirects to login page.
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
        var tokenId = User.FindFirst("token_id")?.Value;
        if (!string.IsNullOrWhiteSpace(tokenId))
        {
            await _tokenService.RevokeTokenStringAsync(tokenId);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Auth");
    }
}
