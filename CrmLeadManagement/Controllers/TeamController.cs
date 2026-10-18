using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class TeamController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration? _configuration;

    public TeamController(AppDbContext db, IConfiguration? configuration = null)
    {
        _db = db;
        _configuration = configuration;
    }

    // GET /Team or /Team/Index - Team Members overview
    public IActionResult Index(string? search = "", string? role = "", string? status = "")
    {
        ViewData["Title"] = "Team";
        ViewData["ActivePage"] = "TeamMembers";

        RoleStore.SeedIfEmpty(_db);

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Team Members.";
            return RedirectToAction("List", "Lead");
        }

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

    // POST /Team/SaveMember - Add or Update a team member
    [HttpPost]
    public async Task<IActionResult> SaveMember(int id, string name, string email, string role, string status, string? password = null)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        var requiredAction = id == 0 ? "Create" : "Edit";

        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", requiredAction))
        {
            TempData["ErrorMessage"] = $"Access Denied: You do not have permission to {requiredAction.ToLower()} team members.";
            return RedirectToAction(nameof(Index));
        }

        if (!isAdmin && string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "Access Denied: Only administrators can assign the Admin role.";
            return RedirectToAction(nameof(Index));
        }

        var adminPassword = _configuration?["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        if (!string.IsNullOrWhiteSpace(password) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(password.Trim(), adminPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "User password must be different from the administrator password.";
            return RedirectToAction(nameof(Index));
        }

        name = name?.Trim() ?? "";
        email = email?.Trim().ToLowerInvariant() ?? "";
        role = string.IsNullOrWhiteSpace(role) ? "Sales Executive" : role.Trim();
        status = string.IsNullOrWhiteSpace(status) ? "Active" : status.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "Full name is required.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
        {
            TempData["ErrorMessage"] = "A valid email address is required.";
            return RedirectToAction(nameof(Index));
        }

        // Check duplicate email (excluding current member if updating)
        var duplicate = await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email && u.Id != id);
        if (duplicate)
        {
            TempData["ErrorMessage"] = $"Email address '{email}' is already in use by another team member.";
            return RedirectToAction(nameof(Index));
        }

        // Fetch Role from RoleMasters to synchronize ModulePermissions
        var roleMaster = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName.ToLower() == role.ToLower());
        if (roleMaster == null)
        {
            roleMaster = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == "Sales Executive")
                         ?? await _db.RoleMasters.FirstOrDefaultAsync();
        }

        var permsList = roleMaster != null ? roleMaster.GetPermissions() : RoleStore.GetDefaultMatrixTemplate();
        var modulePermsString = string.Join(",", permsList.Where(p => p.View).Select(p => p.Module));
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<UserRoleEntry>();

        if (id == 0)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 6)
            {
                TempData["ErrorMessage"] = "Password must be at least 6 characters for a new team member.";
                return RedirectToAction(nameof(Index));
            }

            var newUser = new UserRoleEntry
            {
                Name = name,
                Email = email,
                Role = roleMaster != null ? roleMaster.RoleName : role,
                Status = status,
                ModulePermissions = modulePermsString,
                CreatedAt = DateTime.UtcNow,
                FailedLoginCount = 0,
                MustChangePassword = false
            };
            newUser.PasswordHash = hasher.HashPassword(newUser, password.Trim());

            _db.Users.Add(newUser);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Team member '{name}' added successfully with role '{newUser.Role}'.";
        }
        else
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Team member not found.";
                return RedirectToAction(nameof(Index));
            }

            user.Name = name;
            user.Email = email;
            user.Role = roleMaster != null ? roleMaster.RoleName : role;
            user.Status = status;
            user.ModulePermissions = modulePermsString;

            if (!string.IsNullOrWhiteSpace(password) && password.Trim().Length >= 6)
            {
                user.PasswordHash = hasher.HashPassword(user, password.Trim());
                user.LastPasswordChangeAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Team member '{name}' updated successfully.";
        }

        return RedirectToAction(nameof(Index));
    }

    // POST /Team/DeleteMember - Remove a team member
    [HttpPost]
    public async Task<IActionResult> DeleteMember(int id)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);

        if (currentUser != null && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete team members.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "Team member not found.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.IsNullOrEmpty(authEmail) && string.Equals(user.Email, authEmail, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "You cannot delete your own account while logged in.";
            return RedirectToAction(nameof(Index));
        }

        if (user.Role == "Super Admin" || user.Role == "Admin")
        {
            var adminCount = await _db.Users.CountAsync(u => (u.Role == "Super Admin" || u.Role == "Admin") && u.Id != user.Id);
            if (adminCount == 0)
            {
                TempData["ErrorMessage"] = "Cannot delete the last remaining Administrator account.";
                return RedirectToAction(nameof(Index));
            }
        }

        var memberName = user.Name ?? user.Email;
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Team member '{memberName}' removed successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Team/Roles - Team > Roles & Permissions
    public IActionResult Roles(string? role = null)
    {
        ViewData["Title"] = "Roles & Permissions";
        ViewData["ActivePage"] = "TeamRoles";

        RoleStore.SeedIfEmpty(_db);

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Roles & Permissions", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: Only administrators can access Roles & Permissions.";
            return RedirectToAction(nameof(Index));
        }

        var allRoles = _db.RoleMasters.OrderBy(r => r.Id).ToList();
        if (!allRoles.Any())
        {
            RoleStore.SeedIfEmpty(_db);
            allRoles = _db.RoleMasters.OrderBy(r => r.Id).ToList();
        }

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

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Roles & Permissions", "Edit"))
        {
            return Forbid();
        }

        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == dto.RoleName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{dto.RoleName}' not found." });

        role.DataAccess = string.Equals(dto.DataAccess, "All", StringComparison.OrdinalIgnoreCase) ? "All" : "OnlyAssigned";
        role.SetPermissions(dto.Permissions);

        // Update the comma-separated permitted modules on assigned users so existing filters work seamlessly
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

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Roles & Permissions", "Create"))
        {
            return Forbid();
        }

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

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Roles & Permissions", "Edit"))
        {
            return Forbid();
        }

        var oldName = dto.OldRoleName.Trim();
        var newName = dto.NewRoleName.Trim();

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "The new role name is identical to the current name." });

        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == oldName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{oldName}' not found." });

        if (role.IsSystem || string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(role.RoleName, "Super Admin", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "System roles cannot be renamed." });

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

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Roles & Permissions", "Delete"))
        {
            return Forbid();
        }

        var roleName = dto.RoleName.Trim();
        var role = _db.RoleMasters.FirstOrDefault(r => r.RoleName == roleName);
        if (role == null)
            return NotFound(new { success = false, message = $"Role '{roleName}' not found." });

        if (role.IsSystem || string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(role.RoleName, "Super Admin", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "System roles cannot be deleted." });

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

            var targetPerms = reassignTarget.GetPermissions();
            var targetModulePermsString = string.Join(",", targetPerms.Where(p => p.View).Select(p => p.Module));

            foreach (var u in assignedUsers)
            {
                u.Role = reassignTarget.RoleName;
                u.ModulePermissions = targetModulePermsString;
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

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        if (currentUser != null && !PermissionChecker.HasModulePermission(_db, currentUser, "Roles & Permissions", "Edit"))
        {
            return Forbid();
        }

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
