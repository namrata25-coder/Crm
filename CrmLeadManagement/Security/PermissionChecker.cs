using System.Security.Claims;
using CrmLeadManagement.Models;

namespace CrmLeadManagement.Security;

/// <summary>
/// Centralized authorization and permission checker for VisionCRM.
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

        return null;
    }

    public static bool IsAdmin(ClaimsPrincipal? userPrincipal, UserRoleEntry? user = null)
    {
        if (user != null)
        {
            return user.IsAdminAccount;
        }
        return userPrincipal?.IsInRole("Admin") == true;
    }

    public static bool IsAdmin(AppDbContext db, ClaimsPrincipal? userPrincipal)
    {
        var user = GetCurrentUser(db, userPrincipal);
        return user != null && user.IsAdminAccount;
    }

    public static bool IsUser(AppDbContext db, ClaimsPrincipal? userPrincipal)
    {
        var user = GetCurrentUser(db, userPrincipal);
        return user != null && user.IsUserAccount;
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

        if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Super Admin and Admin always possess unrestricted authority
        if (string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
            user.IsAdminAccount)
        {
            return true;
        }

        // Find the RoleMaster for the user's assigned role
        var role = db.RoleMasters.FirstOrDefault(r => r.RoleName == user.Role);

        bool isLeadOrActivityModule = string.Equals(moduleName, "Lead", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Leads", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Call", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Calls", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "FollowUp", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "FollowUps", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Follow-up", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Follow-ups", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Visit", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Visits", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Requirement", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(moduleName, "Requirements", StringComparison.OrdinalIgnoreCase);

        // Core operational actions on leads and interactions (View, Create, Edit) are permitted for active users by default
        if (isLeadOrActivityModule && (string.Equals(action, "create", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(action, "view", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(action, "edit", StringComparison.OrdinalIgnoreCase)))
        {
            if (role != null)
            {
                var rolePerms = role.GetPermissions();
                var modItem = rolePerms.FirstOrDefault(p => string.Equals(p.Module, moduleName, StringComparison.OrdinalIgnoreCase))
                           ?? rolePerms.FirstOrDefault(p => string.Equals(p.Module, "Lead List", StringComparison.OrdinalIgnoreCase));
                if (modItem != null)
                {
                    if (string.Equals(action, "create", StringComparison.OrdinalIgnoreCase)) return modItem.Create;
                    if (string.Equals(action, "view", StringComparison.OrdinalIgnoreCase)) return modItem.View;
                    if (string.Equals(action, "edit", StringComparison.OrdinalIgnoreCase)) return modItem.Edit;
                }
            }
            return true;
        }

        if (string.IsNullOrWhiteSpace(user.Role))
        {
            return !string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase);
        }

        if (role == null)
        {
            // Fallback: check comma-separated user.ModulePermissions if present
            if (!string.IsNullOrWhiteSpace(user.ModulePermissions))
            {
                var list = user.ModulePermissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool hasPerm = list.Any(m => string.Equals(m, moduleName, StringComparison.OrdinalIgnoreCase)
                                          || ((string.Equals(moduleName, "Lead", StringComparison.OrdinalIgnoreCase) || string.Equals(moduleName, "Leads", StringComparison.OrdinalIgnoreCase)) && m.Contains("Lead", StringComparison.OrdinalIgnoreCase)));
                if (hasPerm) return true;
            }
            return !string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase);
        }

        var perms = role.GetPermissions();
        var item = perms.FirstOrDefault(p => string.Equals(p.Module, moduleName, StringComparison.OrdinalIgnoreCase));

        // Module aliases mapping
        if (item == null)
        {
            if (string.Equals(moduleName, "Lead", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(moduleName, "Leads", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Lead List", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Assigned Leads", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Lead", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Leads", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "Call", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(moduleName, "Calls", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Calls", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Lead List", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "FollowUp", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(moduleName, "Follow-up", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(moduleName, "FollowUps", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(moduleName, "Follow-ups", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Follow-ups", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Lead List", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "Requirement", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(moduleName, "Requirements", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Requirements", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.Module, "Lead List", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "Visit", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(moduleName, "Visits", StringComparison.OrdinalIgnoreCase))
            {
                item = perms.FirstOrDefault(p => string.Equals(p.Module, "Lead List", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(moduleName, "Payment", StringComparison.OrdinalIgnoreCase))
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

        if (item == null)
        {
            return !string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase);
        }

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
