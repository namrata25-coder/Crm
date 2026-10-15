using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrmLeadManagement.Models;

/// <summary>
/// Represents a single permission row in the Roles &amp; Permissions matrix.
/// </summary>
public class ModulePermissionItem
{
    public string Section { get; set; } = ""; // LEADS, FINANCE, PROJECTS, REPORTS, SETTINGS
    public string Module { get; set; } = "";  // e.g. Lead List, Assigned Leads, etc.
    
    // View
    public bool HasView { get; set; } = true;
    public bool View { get; set; } = false;

    // Create
    public bool HasCreate { get; set; } = true;
    public bool Create { get; set; } = false;

    // Edit
    public bool HasEdit { get; set; } = true;
    public bool Edit { get; set; } = false;

    // Delete
    public bool HasDelete { get; set; } = true;
    public bool Delete { get; set; } = false;

    // Extra action (e.g., Assign, Send, Approve, Download, Export)
    public string? ExtraName { get; set; } = null;
    public bool Extra { get; set; } = false;

    // Row-level All helper
    [JsonIgnore]
    public bool IsAllChecked
    {
        get
        {
            if (HasView && !View) return false;
            if (HasCreate && !Create) return false;
            if (HasEdit && !Edit) return false;
            if (HasDelete && !Delete) return false;
            if (!string.IsNullOrWhiteSpace(ExtraName) && !Extra) return false;
            return true;
        }
    }
}

/// <summary>
/// Database entity storing role master configuration and permissions JSON.
/// </summary>
public class RoleMaster
{
    public int Id { get; set; }
    public string RoleName { get; set; } = "";
    public string? Description { get; set; } = "";
    public bool IsSystem { get; set; } = false;
    public string DataAccess { get; set; } = "OnlyAssigned"; // "All" or "OnlyAssigned"
    public string PermissionsJson { get; set; } = "[]";

    public List<ModulePermissionItem> GetPermissions()
    {
        if (string.IsNullOrWhiteSpace(PermissionsJson))
            return RoleStore.GetDefaultMatrixTemplate();

        try
        {
            var list = JsonSerializer.Deserialize<List<ModulePermissionItem>>(PermissionsJson);
            if (list == null || list.Count == 0)
                return RoleStore.GetDefaultMatrixTemplate();

            // Re-merge with standard matrix template to ensure all standard modules & Extra names are up to date
            var template = RoleStore.GetDefaultMatrixTemplate();
            var dict = list.ToDictionary(x => x.Module, x => x, StringComparer.OrdinalIgnoreCase);

            foreach (var item in template)
            {
                if (dict.TryGetValue(item.Module, out var existing))
                {
                    item.View = item.HasView && existing.View;
                    item.Create = item.HasCreate && existing.Create;
                    item.Edit = item.HasEdit && existing.Edit;
                    item.Delete = item.HasDelete && existing.Delete;
                    item.Extra = !string.IsNullOrWhiteSpace(item.ExtraName) && existing.Extra;
                }
            }
            return template;
        }
        catch
        {
            return RoleStore.GetDefaultMatrixTemplate();
        }
    }

    public void SetPermissions(List<ModulePermissionItem> items)
    {
        PermissionsJson = JsonSerializer.Serialize(items);
    }
}

/// <summary>
/// DTO for saving permissions for a role.
/// </summary>
public class SaveRolePermissionsDto
{
    public string RoleName { get; set; } = "";
    public string DataAccess { get; set; } = "OnlyAssigned"; // "All" or "OnlyAssigned"
    public List<ModulePermissionItem> Permissions { get; set; } = new();
}

/// <summary>
/// DTO for creating a new role.
/// </summary>
public class CreateRoleDto
{
    public string RoleName { get; set; } = "";
    public string? Description { get; set; } = "";
    public string? CopyFromRole { get; set; } = "";
    public string DataAccess { get; set; } = "OnlyAssigned";
}

/// <summary>
/// DTO for renaming a role.
/// </summary>
public class RenameRoleDto
{
    public string OldRoleName { get; set; } = "";
    public string NewRoleName { get; set; } = "";
}

/// <summary>
/// DTO for deleting a role.
/// </summary>
public class DeleteRoleDto
{
    public string RoleName { get; set; } = "";
    public string? ReassignToRole { get; set; } = "";
}

/// <summary>
/// View model for the Roles &amp; Permissions page.
/// </summary>
public class RolesAndPermissionsViewModel
{
    public List<RoleMaster> AllRoles { get; set; } = new();
    public RoleMaster SelectedRole { get; set; } = new();
    public List<ModulePermissionItem> Permissions { get; set; } = new();
    public int AssignedUsersCount { get; set; }
    public List<string> AssignedUserNames { get; set; } = new();
}

/// <summary>
/// Static helper and seeder for Roles and default permission templates.
/// </summary>
public static class RoleStore
{
    /// <summary>
    /// Returns the exact standard template matching the requested Roles &amp; Permissions table layout.
    /// </summary>
    public static List<ModulePermissionItem> GetDefaultMatrixTemplate()
    {
        return new List<ModulePermissionItem>
        {
            // LEADS
            new ModulePermissionItem { Section = "LEADS", Module = "Lead List", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = "Assign" },
            new ModulePermissionItem { Section = "LEADS", Module = "Assigned Leads", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "LEADS", Module = "Calls", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "LEADS", Module = "Follow-ups", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "LEADS", Module = "Requirements", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },

            // FINANCE
            new ModulePermissionItem { Section = "FINANCE", Module = "Estimation", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "FINANCE", Module = "Quotation", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "FINANCE", Module = "Proposal", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "FINANCE", Module = "Invoice", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = "Send" },
            new ModulePermissionItem { Section = "FINANCE", Module = "Expenses", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = "Approve" },

            // PROJECTS
            new ModulePermissionItem { Section = "PROJECTS", Module = "Project", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "PROJECTS", Module = "Milestone", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = "Approve" },
            new ModulePermissionItem { Section = "PROJECTS", Module = "Task", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = "Assign" },
            new ModulePermissionItem { Section = "PROJECTS", Module = "Timesheet", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = "Approve" },
            new ModulePermissionItem { Section = "PROJECTS", Module = "Note", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "PROJECTS", Module = "File", HasView = true, HasCreate = true, HasEdit = false, HasDelete = true, ExtraName = "Download" },
            new ModulePermissionItem { Section = "PROJECTS", Module = "Member", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },

            // REPORTS
            new ModulePermissionItem { Section = "REPORTS", Module = "Report", HasView = true, HasCreate = false, HasEdit = false, HasDelete = false, ExtraName = "Export" },

            // SETTINGS
            new ModulePermissionItem { Section = "SETTINGS", Module = "Lead Status/Source", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "SETTINGS", Module = "Users & Roles", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "SETTINGS", Module = "Follow-up Settings", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null },
            new ModulePermissionItem { Section = "SETTINGS", Module = "Roles & Permissions", HasView = true, HasCreate = true, HasEdit = true, HasDelete = true, ExtraName = null }
        };
    }

    /// <summary>
    /// Creates the predefined permissions specifically configured for "Sales Executive"
    /// exactly matching the prompt's ASCII diagram!
    /// </summary>
    public static List<ModulePermissionItem> GetSalesExecutivePermissions()
    {
        var list = GetDefaultMatrixTemplate();
        foreach (var item in list)
        {
            switch (item.Module)
            {
                case "Lead List":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false; item.Extra = false;
                    break;
                case "Assigned Leads":
                    item.View = true; item.Create = false; item.Edit = true; item.Delete = false;
                    break;
                case "Calls":
                    item.View = true; item.Create = true; item.Edit = false; item.Delete = false;
                    break;
                case "Follow-ups":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
                    break;
                case "Requirements":
                    item.View = true; item.Create = true; item.Edit = false; item.Delete = false;
                    break;

                case "Estimation":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
                    break;
                case "Quotation":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
                    break;
                case "Proposal":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
                    break;
                case "Invoice":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false; item.Extra = false;
                    break;
                case "Expenses":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false; item.Extra = false;
                    break;

                case "Project":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
                    break;
                case "Milestone":
                    item.View = true; item.Create = false; item.Edit = true; item.Delete = false; item.Extra = false;
                    break;
                case "Task":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false; item.Extra = false;
                    break;
                case "Timesheet":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false; item.Extra = false;
                    break;
                case "Note":
                    item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
                    break;
                case "File":
                    item.View = true; item.Create = true; item.Delete = false; item.Extra = true; // Download is checked!
                    break;
                case "Member":
                    item.View = true; item.Create = false; item.Edit = false; item.Delete = false;
                    break;

                case "Report":
                    item.View = true; item.Extra = false;
                    break;

                case "Lead Status/Source":
                case "Users & Roles":
                case "Follow-up Settings":
                case "Roles & Permissions":
                    item.View = false; item.Create = false; item.Edit = false; item.Delete = false;
                    break;
            }
        }
        return list;
    }

    /// <summary>
    /// Creates the predefined permissions for "Sales Manager".
    /// </summary>
    public static List<ModulePermissionItem> GetSalesManagerPermissions()
    {
        var list = GetDefaultMatrixTemplate();
        foreach (var item in list)
        {
            if (item.Section == "LEADS" || item.Section == "FINANCE")
            {
                item.View = true; item.Create = true; item.Edit = true; item.Delete = true;
                item.Extra = true;
            }
            else if (item.Section == "PROJECTS")
            {
                item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
                item.Extra = true;
            }
            else if (item.Section == "REPORTS")
            {
                item.View = true; item.Extra = true;
            }
            else if (item.Section == "SETTINGS")
            {
                item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
            }
        }
        return list;
    }

    /// <summary>
    /// Creates the predefined permissions for "Admin" (full access to all).
    /// </summary>
    public static List<ModulePermissionItem> GetAdminPermissions()
    {
        var list = GetDefaultMatrixTemplate();
        foreach (var item in list)
        {
            if (item.HasView) item.View = true;
            if (item.HasCreate) item.Create = true;
            if (item.HasEdit) item.Edit = true;
            if (item.HasDelete) item.Delete = true;
            if (!string.IsNullOrWhiteSpace(item.ExtraName)) item.Extra = true;
        }
        return list;
    }

    /// <summary>
    /// Creates the predefined permissions for "Telecaller".
    /// </summary>
    public static List<ModulePermissionItem> GetTelecallerPermissions()
    {
        var list = GetDefaultMatrixTemplate();
        foreach (var item in list)
        {
            if (item.Module == "Lead List" || item.Module == "Assigned Leads" || item.Module == "Calls" || item.Module == "Follow-ups" || item.Module == "Requirements")
            {
                item.View = true; item.Create = true; item.Edit = true; item.Delete = false;
            }
            else
            {
                item.View = false; item.Create = false; item.Edit = false; item.Delete = false; item.Extra = false;
            }
        }
        return list;
    }

    /// <summary>
    /// Seed initial roles if RoleMasters is empty.
    /// </summary>
    public static void SeedIfEmpty(AppDbContext db)
    {
        if (!db.RoleMasters.Any())
        {
            var salesExec = new RoleMaster
            {
                RoleName = "Sales Executive",
                Description = "Standard sales team member handling assigned leads, follow-ups, and quotations.",
                IsSystem = false,
                DataAccess = "OnlyAssigned"
            };
            salesExec.SetPermissions(GetSalesExecutivePermissions());

            var salesMgr = new RoleMaster
            {
                RoleName = "Sales Manager",
                Description = "Sales manager with access to all records, approval workflows, and assignments.",
                IsSystem = false,
                DataAccess = "All"
            };
            salesMgr.SetPermissions(GetSalesManagerPermissions());

            var admin = new RoleMaster
            {
                RoleName = "Admin",
                Description = "System Administrator with unrestricted access to all modules and configurations.",
                IsSystem = true,
                DataAccess = "All"
            };
            admin.SetPermissions(GetAdminPermissions());

            var telecaller = new RoleMaster
            {
                RoleName = "Telecaller",
                Description = "Focused on calling leads, recording requirements, and follow-ups.",
                IsSystem = false,
                DataAccess = "OnlyAssigned"
            };
            telecaller.SetPermissions(GetTelecallerPermissions());

            db.RoleMasters.AddRange(salesExec, salesMgr, admin, telecaller);
            db.SaveChanges();
        }
    }
}
