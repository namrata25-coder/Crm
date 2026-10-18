using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// REST API endpoints for Team Roles and Module Permissions under /api/v1/app/team/roles.
/// </summary>
[ApiController]
[Route("api/v1/app/team/roles")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppTeamRolesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppTeamRolesController> _logger;

    public AppTeamRolesController(AppDbContext db, ILogger<AppTeamRolesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/app/team/roles
    /// Retrieves all roles with their assigned user count.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Roles & Permissions", "View")
                && !string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view roles."));
            }

            RoleStore.SeedIfEmpty(_db);

            var roles = await _db.RoleMasters.OrderBy(r => r.Id).ToListAsync();
            var counts = await _db.Users
                .GroupBy(u => u.Role)
                .Select(g => new { Role = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Role ?? "", x => x.Count);

            var result = roles.Select(r => new
            {
                id = r.Id,
                roleName = r.RoleName,
                description = r.Description,
                isSystem = r.IsSystem,
                dataAccess = r.DataAccess,
                assignedUsersCount = counts.GetValueOrDefault(r.RoleName, 0)
            }).ToList();

            return Ok(AppApiResponse<object>.Ok(result, "Roles retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve roles.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving roles."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/team/roles/{roleName}
    /// Retrieves detailed role configuration, permissions matrix, and assigned users.
    /// </summary>
    [HttpGet("{roleName}")]
    public async Task<IActionResult> GetRoleByName(string roleName)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Roles & Permissions", "View")
                && !string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view role details."));
            }

            if (string.IsNullOrWhiteSpace(roleName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Role name is required."));
            }

            RoleStore.SeedIfEmpty(_db);

            var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName.ToLower() == roleName.Trim().ToLower());
            if (role == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Role '{roleName}' not found."));
            }

            var assignedUsers = await _db.Users
                .Where(u => u.Role != null && u.Role.ToLower() == role.RoleName.ToLower())
                .Select(u => new
                {
                    id = u.Id,
                    name = u.Name,
                    email = u.Email,
                    status = u.Status
                })
                .ToListAsync();

            var data = new
            {
                id = role.Id,
                roleName = role.RoleName,
                description = role.Description,
                isSystem = role.IsSystem,
                dataAccess = role.DataAccess,
                permissions = role.GetPermissions(),
                assignedUsers = assignedUsers
            };

            return Ok(AppApiResponse<object>.Ok(data, $"Role details for '{role.RoleName}' retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve role '{RoleName}'.", roleName);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving role details."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/team/roles/save
    /// Creates or updates a role, stores permissions matrix, and synchronizes assigned users.
    /// </summary>
    [HttpPost("save")]
    public async Task<IActionResult> SaveRole([FromBody] SaveRolePermissionsDto dto)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Roles & Permissions", "Edit")
                && !string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to modify roles and permissions."));
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.RoleName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Role name is required."));
            }

            var roleName = dto.RoleName.Trim();
            var dataAccess = string.Equals(dto.DataAccess, "All", StringComparison.OrdinalIgnoreCase) ? "All" : "OnlyAssigned";

            RoleStore.SeedIfEmpty(_db);

            var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName.ToLower() == roleName.ToLower());
            bool isNew = role == null;

            if (isNew)
            {
                role = new RoleMaster
                {
                    RoleName = roleName,
                    Description = $"{roleName} role configuration",
                    IsSystem = false,
                    DataAccess = dataAccess
                };
                _db.RoleMasters.Add(role);
            }
            else
            {
                // Prevent removing system status from system roles
                if (!role!.IsSystem)
                {
                    role.RoleName = roleName;
                }
                role.DataAccess = dataAccess;
            }

            // If permissions list is provided, validate and update
            if (dto.Permissions != null && dto.Permissions.Count > 0)
            {
                role.SetPermissions(dto.Permissions);
            }
            else if (isNew)
            {
                role.SetPermissions(RoleStore.GetDefaultMatrixTemplate());
            }

            // Sync assigned users with the new permitted modules list
            var currentPerms = role.GetPermissions();
            var permittedModules = currentPerms.Where(p => p.View).Select(p => p.Module).ToList();
            var modulePermsString = string.Join(",", permittedModules);

            var assignedUsers = await _db.Users.Where(u => u.Role == role.RoleName).ToListAsync();
            foreach (var u in assignedUsers)
            {
                u.ModulePermissions = modulePermsString;
            }

            await _db.SaveChangesAsync();

            var responseData = new
            {
                id = role.Id,
                roleName = role.RoleName,
                description = role.Description,
                isSystem = role.IsSystem,
                dataAccess = role.DataAccess,
                permissions = role.GetPermissions(),
                updatedAssignedUsersCount = assignedUsers.Count
            };

            var message = isNew
                ? $"Role '{role.RoleName}' created successfully."
                : $"Role '{role.RoleName}' updated successfully and applied to {assignedUsers.Count} assigned user(s).";

            return isNew
                ? StatusCode(StatusCodes.Status201Created, AppApiResponse<object>.Ok(responseData, message))
                : Ok(AppApiResponse<object>.Ok(responseData, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save role permissions.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while saving role permissions."));
        }
    }
}
