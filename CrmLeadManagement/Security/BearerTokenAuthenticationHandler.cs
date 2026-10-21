using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CrmLeadManagement.Security;

/// <summary>
/// Custom authentication handler for opaque Bearer tokens.
/// Validates tokens against the AccessTokens database table via ITokenService.
/// Scheme Name: "Bearer"
/// </summary>
public class BearerTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BearerTokenAuthenticationHandler> _logger;

    public BearerTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
        : base(options, loggerFactory, encoder)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = loggerFactory.CreateLogger<BearerTokenAuthenticationHandler>();
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Read Authorization header
        if (!Request.Headers.TryGetValue("Authorization", out var authHeaderValues))
        {
            return AuthenticateResult.NoResult();
        }

        var authHeader = authHeaderValues.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        if (token.Contains('{'))
        {
            token = token.Substring(0, token.IndexOf('{')).Trim();
        }
        var firstSpace = token.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        if (firstSpace > 0)
        {
            token = token.Substring(0, firstSpace).Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.Fail("Invalid or expired token");
        }

        // 2. Look up and validate token via ITokenService
        using var scope = _serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        AccessToken? accessToken;
        try
        {
            accessToken = await tokenService.ValidateTokenAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate bearer token due to database or connection timeout.");
            return AuthenticateResult.Fail("Authentication service temporarily unavailable.");
        }

        if (accessToken == null)
        {
            return AuthenticateResult.Fail("Invalid or expired token");
        }

        var user = accessToken.User;
        if (user == null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Invalid or expired token");
        }

        // 3. Sliding refresh & LastUsedAt throttling (update at most once per minute)
        var now = DateTime.UtcNow;
        bool shouldUpdateLastUsed = !accessToken.LastUsedAt.HasValue || (now - accessToken.LastUsedAt.Value).TotalMinutes >= 1;

        int tokenHours = _configuration.GetValue("BearerAuth:TokenHours", 12);
        bool slidingExpiration = _configuration.GetValue("BearerAuth:SlidingExpiration", true);
        int maxDays = _configuration.GetValue("BearerAuth:MaxDays", 30);

        DateTime? newExpires = null;
        if (slidingExpiration && (accessToken.ExpiresAt - now).TotalHours < (tokenHours / 2.0))
        {
            var potentialExpiry = now.AddHours(tokenHours);
            var absoluteMax = accessToken.CreatedAt.AddDays(maxDays);
            var computedExpiry = potentialExpiry > absoluteMax ? absoluteMax : potentialExpiry;
            if (computedExpiry > accessToken.ExpiresAt)
            {
                newExpires = computedExpiry;
            }
        }

        if (shouldUpdateLastUsed || newExpires.HasValue)
        {
            var tokenId = accessToken.Id;
            var updatedExpiry = newExpires;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var updateScope = _serviceProvider.CreateScope();
                    var asyncTokenService = updateScope.ServiceProvider.GetRequiredService<ITokenService>();
                    await asyncTokenService.TouchTokenAsync(tokenId, updatedExpiry);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to touch AccessToken {TokenId}", tokenId);
                }
            });
        }

        // 4. Build ClaimsPrincipal
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name ?? ""),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.Role ?? ""),
            new("token_id", accessToken.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Response.HasStarted) return;

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = "Bearer";
        Response.ContentType = "application/json";

        var payload = new AppApiError
        {
            Message = "Unauthorized. Missing or invalid token."
        };

        await Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (Response.HasStarted) return;

        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";

        var payload = new AppApiError
        {
            Message = "You do not have permission."
        };

        await Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
