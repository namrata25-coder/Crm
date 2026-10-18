using System.Security.Claims;
using CrmLeadManagement.Models;

namespace CrmLeadManagement.Security;

/// <summary>
/// Centralized authorization and permission checker for NimbusCRM.
/// Evaluates module permissions based on RoleMaster definitions and user assignments.
/// </summary>
public static class PermissionChecker
{
    public static UserRoleEntry? GetCurrentUser(AppDbContext db, ClaimsPrincipal? userPrincipal)
    {
        if (userPrincipal == null) return null;

        var email = userPrincipal.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(email))
        {
            var user = db.Users.FirstOrDefault(u => u.Email != null && u.Email.ToLower() == email.ToLower());
            if (user != null) return user;
        }

        var idStr = userPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idStr, out var id))
        {
            var user = db.Users.FirstOrDefault(u => u.Id == id);
            if (user != null) return user;
        }

        // If user is Admin authenticated principal, construct admin entry
        if (userPrincipal.IsInRole("Admin"))
        {
            return new UserRoleEntry
            {
                Name = userPrincipal.Identity?.Name ?? "Administrator",
                Email = email ?? "admin@nimbuscrm.com",
                Role = "Admin"
            };
        }

        return null;
    }

    public static bool IsAdmin(ClaimsPrincipal? userPrincipal, UserRoleEntry? user = null)
    {
        if (userPrincipal?.IsInRole("Admin") == true) return true;
        if (user != null && (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return false;
    }

    public static bool CanViewModule(AppDbContext db, ClaimsPrincipal? userPrincipal, string moduleName)
    {
        var user = GetCurrentUser(db, userPrincipal);
        if (user == null) return false;
        return HasModulePermission(db, user, moduleName, "View");
    }

    public static bool CanPerformAction(AppDbContext db, ClaimsPrincipal? userPrincipal, string moduleName, string action)
    {
        var user = GetCurrentUser(db, userPrincipal);
        if (user == null) return false;
        return HasModulePermission(db, user, moduleName, action);
    }

    public static bool HasModulePermission(AppDbContext db, UserRoleEntry? user, string moduleName, string action = "View")
    {
        if (user == null) return false;

        // Super Admin and Admin always possess unrestricted authority
        if (string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(user.Role)) return false;

        // Find the RoleMaster for the user's assigned role
        var role = db.RoleMasters.FirstOrDefault(r => r.RoleName == user.Role);
        if (role == null)
        {
            // Fallback: check comma-separated user.ModulePermissions if present
            if (!string.IsNullOrWhiteSpace(user.ModulePermissions))
            {
                var list = user.ModulePermissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return list.Any(m => string.Equals(m, moduleName, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        var perms = role.GetPermissions();
        var item = perms.FirstOrDefault(p => string.Equals(p.Module, moduleName, StringComparison.OrdinalIgnoreCase));

        // Module aliases mapping
        if (item == null)
        {
            if (string.Equals(moduleName, "Payment", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Payment", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Expenses", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "Team Members", StringComparison.OrdinalIgnoreCase) 
                  || string.Equals(moduleName, "Team", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(moduleName, "Members", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Member", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Users & Roles", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "Roles", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(moduleName, "Roles & Permissions", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Roles & Permissions", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                var settingsModules = new[] { "Lead Status/Source", "Users & Roles", "Follow-up Settings", "Roles & Permissions" };
                return perms.Any(p => settingsModules.Contains(p.Module, StringComparer.OrdinalIgnoreCase) &&
                    (string.Equals(action, "create", StringComparison.OrdinalIgnoreCase) ? p.Create :
                     string.Equals(action, "edit", StringComparison.OrdinalIgnoreCase) ? p.Edit :
                     string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase) ? p.Delete : p.View));
            }
        }

        if (item == null) return false;

        return action.ToLowerInvariant() switch
        {
            "view" => item.View,
            "create" => item.Create,
            "edit" => item.Edit,
            "delete" => item.Delete,
            "extra" => item.Extra,
            _ => item.View
        };
    }
}
