using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CrmLeadManagement.Models;

namespace CrmLeadManagement.Security;

/// <summary>
/// Strict backend authorization attribute for Admin-only routes and actions.
/// Enforces that the authenticated user actually exists in the database, is Active,
/// is not locked out, and has an actual database role of Admin or Super Admin.
/// Rejects regular users with HTTP 403 Forbidden.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public class AdminAuthorizeAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var userPrincipal = httpContext.User;

        var isApi = httpContext.Request.Path.StartsWithSegments("/api") ||
                    httpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                    httpContext.Request.Headers["Accept"].ToString().Contains("application/json");

        // 1. Must be authenticated (try Bearer token if cookie session is not present)
        if (userPrincipal?.Identity?.IsAuthenticated != true)
        {
            var authResult = await httpContext.AuthenticateAsync(BearerTokenAuthenticationHandler.SchemeName);
            if (authResult.Succeeded && authResult.Principal != null)
            {
                httpContext.User = authResult.Principal;
                userPrincipal = authResult.Principal;
            }
        }

        if (userPrincipal?.Identity?.IsAuthenticated != true)
        {
            if (isApi)
            {
                context.Result = new ObjectResult(new { message = "Unauthorized", success = false })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return;
            }

            var returnUrl = Uri.EscapeDataString(httpContext.Request.Path + httpContext.Request.QueryString);
            context.Result = new RedirectResult($"/admin/login?returnUrl={returnUrl}");
            return;
        }

        // 2. Validate user ID claim
        var userIdClaim = userPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            context.Result = BuildForbiddenResult(isApi, "Access denied. Valid Admin credentials are required.");
            return;
        }

        // 3. Query the database directly to inspect the actual database role
        var db = httpContext.RequestServices.GetRequiredService<AppDbContext>();
        var dbUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (dbUser == null)
        {
            context.Result = BuildForbiddenResult(isApi, "Access denied. Valid Admin credentials are required.");
            return;
        }

        if (!string.Equals(dbUser.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = BuildForbiddenResult(isApi, "Account is inactive. Please contact system administrator.");
            return;
        }

        if (dbUser.LockoutUntil.HasValue && dbUser.LockoutUntil.Value > DateTime.UtcNow)
        {
            context.Result = BuildForbiddenResult(isApi, "Account is temporarily locked.");
            return;
        }

        // 4. Strict role check: account must have Admin or Super Admin role in database
        if (!dbUser.IsAdminAccount)
        {
            context.Result = BuildForbiddenResult(isApi, "Access denied. Valid Admin credentials are required.");
            return;
        }

        // 5. Verify session_type claim if present
        var sessionType = userPrincipal.FindFirst("session_type")?.Value;
        if (!string.IsNullOrEmpty(sessionType) && !string.Equals(sessionType, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = BuildForbiddenResult(isApi, "Access denied. Valid Admin credentials are required.");
            return;
        }

        await next();
    }

    private static IActionResult BuildForbiddenResult(bool isApi, string message)
    {
        if (isApi)
        {
            return new ObjectResult(new { message, success = false })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        return new ViewResult
        {
            ViewName = "~/Views/Shared/AccessDenied.cshtml",
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
            {
                ["Message"] = message,
                ["Title"] = "Access Denied · VisionCRM Admin Portal",
                ["RequiredRole"] = "Admin",
                ["LoginUrl"] = "/admin/login"
            },
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
