using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// REST API endpoints for Team and Role &amp; Permission management.
/// </summary>
[ApiController]
[Route("api/team")]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class TeamApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration? _configuration;

    public TeamApiController(AppDbContext db, IConfiguration? configuration = null)
    {
        _db = db;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets all roles with their assigned users count.
    /// </summary>
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        RoleStore.SeedIfEmpty(_db);

        var roles = await _db.RoleMasters.OrderBy(r => r.Id).ToListAsync();
        var counts = await _db.Users
            .GroupBy(u => u.Role)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Role ?? "", x => x.Count);

        var result = roles.Select(r => new
        {
            r.Id,
            r.RoleName,
            r.Description,
            r.IsSystem,
            r.DataAccess,
            AssignedUsersCount = counts.GetValueOrDefault(r.RoleName, 0)
        });

        return Ok(result);
    }

    /// <summary>
    /// Gets permissions and configuration for a specific role by name.
    /// </summary>
    [HttpGet("roles/{roleName}")]
    public async Task<IActionResult> GetRoleDetails(string roleName)
    {
        RoleStore.SeedIfEmpty(_db);

        var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == roleName);
        if (role == null)
            return NotFound(new { message = $"Role '{roleName}' not found." });

        var assignedUsers = await _db.Users
            .Where(u => u.Role == role.RoleName)
            .Select(u => new { u.Id, u.Name, u.Email, u.Status })
            .ToListAsync();

        return Ok(new
        {
            role.Id,
            role.RoleName,
            role.Description,
            role.IsSystem,
            role.DataAccess,
            Permissions = role.GetPermissions(),
            AssignedUsers = assignedUsers
        });
    }

    /// <summary>
    /// Saves permissions and data access configuration for a role.
    /// </summary>
    [HttpPost("roles/save")]
    public async Task<IActionResult> SavePermissions([FromBody] SaveRolePermissionsDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.RoleName))
            return BadRequest(new { message = "Role name is required." });

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);
        if (currentUser != null && !PermissionChecker.HasModulePermission(_db, currentUser, "Roles & Permissions", "Edit"))
            return StatusCode(403, new { message = "You do not have permission to modify roles." });

        var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == dto.RoleName);
        if (role == null)
            return NotFound(new { message = $"Role '{dto.RoleName}' not found." });

        role.DataAccess = string.Equals(dto.DataAccess, "All", StringComparison.OrdinalIgnoreCase) ? "All" : "OnlyAssigned";
        role.SetPermissions(dto.Permissions);

        var permittedModules = dto.Permissions.Where(p => p.View).Select(p => p.Module).ToList();
        var modulePermsString = string.Join(",", permittedModules);

        var assignedUsers = await _db.Users.Where(u => u.Role == role.RoleName).ToListAsync();
        foreach (var u in assignedUsers)
        {
            u.ModulePermissions = modulePermsString;
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = $"Permissions saved for '{dto.RoleName}'." });
    }

    /// <summary>
    /// Gets all team members with their role details.
    /// </summary>
    [HttpGet("members")]
    public async Task<IActionResult> GetMembers([FromQuery] string? search, [FromQuery] string? role, [FromQuery] string? status)
    {
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => (u.Name != null && u.Name.ToLower().Contains(s)) ||
                                     (u.Email != null && u.Email.ToLower().Contains(s)) ||
                                     (u.Role != null && u.Role.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Role == role);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);

        var members = await query.OrderBy(u => u.Name).Select(u => new
        {
            u.Id,
            u.Name,
            u.Email,
            u.Role,
            u.Status,
            u.CreatedAt,
            u.LastLoginAt
        }).ToListAsync();

        return Ok(members);
    }

    /// <summary>
    /// Creates or updates a team member.
    /// </summary>
    [HttpPost("members/save")]
    public async Task<IActionResult> SaveMemberApi([FromBody] SaveMemberApiDto dto)
    {
        if (dto == null) return BadRequest(new { message = "Invalid data." });

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        var requiredAction = dto.Id == 0 ? "Create" : "Edit";

        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", requiredAction))
            return StatusCode(403, new { message = $"You do not have permission to {requiredAction.ToLower()} team members." });

        var name = dto.Name?.Trim() ?? "";
        var email = dto.Email?.Trim().ToLowerInvariant() ?? "";
        var role = string.IsNullOrWhiteSpace(dto.Role) ? "Sales Executive" : dto.Role.Trim();
        var status = string.IsNullOrWhiteSpace(dto.Status) ? "Active" : dto.Status.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Full name is required." });

        if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "A valid email address is required." });

        if (!isAdmin && string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "Only administrators can assign the Admin role." });

        var adminPassword = _configuration?["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        if (!string.IsNullOrWhiteSpace(dto.Password) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(dto.Password.Trim(), adminPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "User password must be different from the administrator password." });
        }

        var duplicate = await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email && u.Id != dto.Id);
        if (duplicate)
            return BadRequest(new { message = $"Email address '{email}' is already in use by another team member." });

        var roleMaster = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName.ToLower() == role.ToLower());
        var permsList = roleMaster != null ? roleMaster.GetPermissions() : RoleStore.GetDefaultMatrixTemplate();
        var modulePermsString = string.Join(",", permsList.Where(p => p.View).Select(p => p.Module));
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<UserRoleEntry>();

        if (dto.Id == 0)
        {
            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Trim().Length < 6)
                return BadRequest(new { message = "Password must be at least 6 characters for a new team member." });

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
            newUser.PasswordHash = hasher.HashPassword(newUser, dto.Password.Trim());

            _db.Users.Add(newUser);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = $"Team member '{name}' added successfully.", member = newUser });
        }
        else
        {
            var user = await _db.Users.FindAsync(dto.Id);
            if (user == null)
                return NotFound(new { message = "Team member not found." });

            if (!isAdmin && (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase)))
            {
                return StatusCode(403, new { message = "Only administrators can modify an Admin account." });
            }

            user.Name = name;
            user.Email = email;
            user.Role = roleMaster != null ? roleMaster.RoleName : role;
            user.Status = status;
            user.ModulePermissions = modulePermsString;

            if (!string.IsNullOrWhiteSpace(dto.Password) && dto.Password.Trim().Length >= 6)
            {
                user.PasswordHash = hasher.HashPassword(user, dto.Password.Trim());
                user.LastPasswordChangeAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = $"Team member '{name}' updated successfully.", member = user });
        }
    }

    /// <summary>
    /// Deletes a team member by id.
    /// </summary>
    [HttpDelete("members/{id:int}")]
    [HttpPost("members/delete")]
    public async Task<IActionResult> DeleteMemberApi(int id)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", "Delete"))
            return StatusCode(403, new { message = "You do not have permission to delete team members." });

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { message = "Team member not found." });

        if (!string.IsNullOrEmpty(authEmail) && string.Equals(user.Email, authEmail, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "You cannot delete your own account while logged in." });

        if (user.Role == "Super Admin" || user.Role == "Admin")
        {
            if (!isAdmin)
                return StatusCode(403, new { message = "Only administrators can delete an Admin account." });

            var adminCount = await _db.Users.CountAsync(u => (u.Role == "Super Admin" || u.Role == "Admin") && u.Id != user.Id);
            if (adminCount == 0)
                return BadRequest(new { message = "Cannot delete the last remaining Administrator account." });
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = $"Team member '{user.Name}' deleted successfully." });
    }
}

public class SaveMemberApiDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string? Password { get; set; }
}
