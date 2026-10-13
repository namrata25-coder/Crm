using CrmLeadManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class TeamController : Controller
{
    private readonly AppDbContext _db;

    public TeamController(AppDbContext db)
    {
        _db = db;
    }

    // GET /Team or /Team/Index - Team Members overview
    public IActionResult Index(string? search = "", string? role = "", string? status = "")
    {
        ViewData["Title"] = "Team";
        ViewData["ActivePage"] = "TeamMembers";

        RoleStore.SeedIfEmpty(_db);

        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => (u.Name != null && u.Name.ToLower().Contains(s)) ||
                                     (u.Email != null && u.Email.ToLower().Contains(s)) ||
                                     (u.Role != null && u.Role.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Role == role);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.Status == status);
        }

        var users = query.OrderBy(u => u.Name).ToList();
        var allRoles = _db.RoleMasters.OrderBy(r => r.RoleName).Select(r => r.RoleName).ToList();

        ViewBag.Search = search;
        ViewBag.Role = role;
        ViewBag.Status = status;
        ViewBag.AllRoles = allRoles;

        return View(users);
    }

    // GET /Team/Roles - Team > Roles & Permissions
    public IActionResult Roles(string? role = null)
    {
        ViewData["Title"] = "Roles & Permissions";
        ViewData["ActivePage"] = "TeamRoles";

        RoleStore.SeedIfEmpty(_db);

        var allRoles = _db.RoleMasters.OrderBy(r => r.Id).ToList();
        if (!allRoles.Any())
        {
            RoleStore.SeedIfEmpty(_db);
            allRoles = _db.RoleMasters.OrderBy(r => r.Id).ToList();
        }

        // Default to "Sales Executive" if not specified, matching the design prompt
        var selectedRole = allRoles.FirstOrDefault(r => string.Equals(r.RoleName, role, StringComparison.OrdinalIgnoreCase))
                           ?? allRoles.FirstOrDefault(r => string.Equals(r.RoleName, "Sales Executive", StringComparison.OrdinalIgnoreCase))
                           ?? allRoles.First();

        var assignedUsers = _db.Users
            .Where(u => u.Role == selectedRole.RoleName)
            .Select(u => u.Name ?? u.Email ?? "Unknown")
            .ToList();

        var vm = new RolesAndPermissionsViewModel
        {
            AllRoles = allRoles,
            SelectedRole = selectedRole,
            Permissions = selectedRole.GetPermissions(),
            AssignedUsersCount = assignedUsers.Count,
            AssignedUserNames = assignedUsers
        };

        return View(vm);
    }

    // GET /Team/GetRoleData?roleName=...
    [HttpGet]
    public IActionResult GetRoleData(string roleName)
    {
        RoleStore.SeedIfEmpty(_db);

        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == roleName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{roleName}' not found." });

        var assignedUsers = _db.Users
            .Where(u => u.Role == role.RoleName)
            .Select(u => u.Name ?? u.Email ?? "Unknown")
            .ToList();

        return Json(new
        {
            success = true,
            id = role.Id,
            roleName = role.RoleName,
            description = role.Description,
            isSystem = role.IsSystem,
            dataAccess = role.DataAccess,
            permissions = role.GetPermissions(),
            assignedUsersCount = assignedUsers.Count,
            assignedUserNames = assignedUsers
        });
    }

    // POST /Team/SavePermissions
    [HttpPost]
    public IActionResult SavePermissions([FromBody] SaveRolePermissionsDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.RoleName))
            return BadRequest(new { success = false, message = "Invalid role data." });

        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == dto.RoleName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{dto.RoleName}' not found." });

        role.DataAccess = string.Equals(dto.DataAccess, "All", StringComparison.OrdinalIgnoreCase) ? "All" : "OnlyAssigned";
        role.SetPermissions(dto.Permissions);

        // Also update the comma-separated permitted modules on assigned users so existing filters work seamlessly
        var permittedModules = dto.Permissions
            .Where(p => p.View)
            .Select(p => p.Module)
            .ToList();
        var modulePermsString = string.Join(",", permittedModules);

        var assignedUsers = _db.Users.Where(u => u.Role == role.RoleName).ToList();
        foreach (var u in assignedUsers)
        {
            u.ModulePermissions = modulePermsString;
        }

        _db.SaveChanges();

        return Json(new
        {
            success = true,
            message = $"Permissions and data access scope for '{dto.RoleName}' saved successfully!",
            assignedUsersUpdated = assignedUsers.Count
        });
    }

    // POST /Team/CreateRole
    [HttpPost]
    public IActionResult CreateRole([FromBody] CreateRoleDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.RoleName))
            return BadRequest(new { success = false, message = "Role name is required." });

        var cleanName = dto.RoleName.Trim();
        if (_db.RoleMasters.Any(r => r.RoleName.ToLower() == cleanName.ToLower()))
            return BadRequest(new { success = false, message = $"A role named '{cleanName}' already exists." });

        List<ModulePermissionItem> perms;
        string dataAccess = string.IsNullOrWhiteSpace(dto.DataAccess) ? "OnlyAssigned" : dto.DataAccess;

        if (!string.IsNullOrWhiteSpace(dto.CopyFromRole))
        {
            var sourceRole = _db.RoleMasters.FirstOrDefault(r => r.RoleName == dto.CopyFromRole);
            if (sourceRole != null)
            {
                perms = sourceRole.GetPermissions();
                dataAccess = sourceRole.DataAccess;
            }
            else
            {
                perms = RoleStore.GetDefaultMatrixTemplate();
            }
        }
        else
        {
            perms = RoleStore.GetDefaultMatrixTemplate();
        }

        var newRole = new RoleMaster
        {
            RoleName = cleanName,
            Description = dto.Description?.Trim() ?? "",
            IsSystem = false,
            DataAccess = dataAccess
        };
        newRole.SetPermissions(perms);

        _db.RoleMasters.Add(newRole);
        _db.SaveChanges();

        return Json(new
        {
            success = true,
            message = $"Role '{cleanName}' created successfully!",
            role = newRole
        });
    }

    // POST /Team/RenameRole
    [HttpPost]
    public IActionResult RenameRole([FromBody] RenameRoleDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.OldRoleName) || string.IsNullOrWhiteSpace(dto.NewRoleName))
            return BadRequest(new { success = false, message = "Both old and new role names are required." });

        var oldName = dto.OldRoleName.Trim();
        var newName = dto.NewRoleName.Trim();

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "The new role name is identical to the current name." });

        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == oldName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{oldName}' not found." });

        if (_db.RoleMasters.Any(r => r.RoleName.ToLower() == newName.ToLower() && r.Id != role.Id))
            return BadRequest(new { success = false, message = $"A role named '{newName}' already exists." });

        role.RoleName = newName;

        // Cascade rename to Users assigned to this role
        var assignedUsers = _db.Users.Where(u => u.Role == oldName).ToList();
        foreach (var u in assignedUsers)
        {
            u.Role = newName;
        }

        _db.SaveChanges();

        return Json(new
        {
            success = true,
            message = $"Role renamed to '{newName}' successfully. {assignedUsers.Count} team member(s) updated.",
            newRoleName = newName
        });
    }

    // POST /Team/DeleteRole
    [HttpPost]
    public IActionResult DeleteRole([FromBody] DeleteRoleDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.RoleName))
            return BadRequest(new { success = false, message = "Role name is required." });

        var roleName = dto.RoleName.Trim();
        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == roleName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{roleName}' not found." });

        if (role.IsSystem || string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "The 'Admin' system role cannot be deleted." });

        var assignedUsers = _db.Users.Where(u => u.Role == roleName).ToList();
        if (assignedUsers.Any())
        {
            if (string.IsNullOrWhiteSpace(dto.ReassignToRole))
            {
                return BadRequest(new
                {
                    success = false,
                    requiresReassign = true,
                    assignedCount = assignedUsers.Count,
                    message = $"Cannot delete '{roleName}' because {assignedUsers.Count} team member(s) are assigned to it. Please select a replacement role."
                });
            }

            var reassignTarget = _db.RoleMasters.FirstOrDefault(r => r.RoleName == dto.ReassignToRole);
            if (reassignTarget == null)
                return BadRequest(new { success = false, message = $"Target role '{dto.ReassignToRole}' does not exist." });

            foreach (var u in assignedUsers)
            {
                u.Role = reassignTarget.RoleName;
            }
        }

        _db.RoleMasters.Remove(role);
        _db.SaveChanges();

        return Json(new
        {
            success = true,
            message = $"Role '{roleName}' deleted successfully."
        });
    }

    // POST /Team/ResetRole
    [HttpPost]
    public IActionResult ResetRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return BadRequest(new { success = false, message = "Role name is required." });

        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == roleName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{roleName}' not found." });

        List<ModulePermissionItem> resetPerms;
        if (string.Equals(role.RoleName, "Sales Executive", StringComparison.OrdinalIgnoreCase))
        {
            resetPerms = RoleStore.GetSalesExecutivePermissions();
            role.DataAccess = "OnlyAssigned";
        }
        else if (string.Equals(role.RoleName, "Sales Manager", StringComparison.OrdinalIgnoreCase))
        {
            resetPerms = RoleStore.GetSalesManagerPermissions();
            role.DataAccess = "All";
        }
        else if (string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            resetPerms = RoleStore.GetAdminPermissions();
            role.DataAccess = "All";
        }
        else if (string.Equals(role.RoleName, "Telecaller", StringComparison.OrdinalIgnoreCase))
        {
            resetPerms = RoleStore.GetTelecallerPermissions();
            role.DataAccess = "OnlyAssigned";
        }
        else
        {
            resetPerms = RoleStore.GetDefaultMatrixTemplate();
            role.DataAccess = "OnlyAssigned";
        }

        role.SetPermissions(resetPerms);
        _db.SaveChanges();

        return Json(new
        {
            success = true,
            message = $"Permissions for '{roleName}' have been reset to default values.",
            permissions = resetPerms,
            dataAccess = role.DataAccess
        });
    }
}
