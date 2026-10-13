using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    public TeamApiController(AppDbContext db)
    {
        _db = db;
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
}
