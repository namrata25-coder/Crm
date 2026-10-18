using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// REST API endpoints for Application Settings, Statuses, Sources, Users, Taxes, and Follow-up configuration.
/// Base Route: /api/v1/app/settings
/// </summary>
[ApiController]
[Route("api/v1/app/settings")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppSystemSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppSystemSettingsController> _logger;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public AppSystemSettingsController(AppDbContext db, ILogger<AppSystemSettingsController> logger)
    {
        _db = db;
        _logger = logger;
        _passwordHasher = new PasswordHasher<UserRoleEntry>();
    }

    // =========================================================================
    // 1. APP SETTINGS APIs
    // =========================================================================

    /// <summary>
    /// GET /api/v1/app/settings
    /// Retrieves all application settings (statuses, sources, sanitized users, taxes, followup).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        try
        {
            var statuses = await _db.StatusOptions
                .OrderBy(s => s.Id)
                .Select(s => s.Value)
                .ToListAsync();

            var sources = await _db.SourceOptions
                .OrderBy(s => s.Id)
                .Select(s => s.Value)
                .ToListAsync();

            var users = await _db.Users
                .OrderBy(u => u.Id)
                .Select(u => new AppSettingsUserDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Email = u.Email,
                    Role = u.Role,
                    Status = u.Status,
                    ModulePermissions = u.ModulePermissions,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            var taxes = await _db.TaxMasters
                .OrderBy(t => t.TaxType)
                .ThenBy(t => t.TaxRate)
                .ToListAsync();

            var followUp = await _db.FollowUpSettings.FirstOrDefaultAsync(f => f.Id == 1);
            if (followUp == null)
            {
                followUp = new FollowUpSettings { Id = 1 };
                _db.FollowUpSettings.Add(followUp);
                await _db.SaveChangesAsync();
            }

            var result = new AppFullSettingsDto
            {
                Statuses = statuses,
                Sources = sources,
                Users = users,
                Taxes = taxes,
                FollowUp = followUp
            };

            return Ok(AppApiResponse<AppFullSettingsDto>.Ok(result, "Application settings retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve application settings.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An unexpected error occurred while retrieving application settings."));
        }
    }

    // =========================================================================
    // 2. STATUS MANAGEMENT APIs
    // =========================================================================

    /// <summary>
    /// GET /api/v1/app/settings/statuses
    /// Retrieves all available lead statuses.
    /// </summary>
    [HttpGet("statuses")]
    public async Task<IActionResult> GetStatuses()
    {
        try
        {
            var statuses = await _db.StatusOptions
                .OrderBy(s => s.Id)
                .Select(s => s.Value)
                .ToListAsync();

            return Ok(AppApiResponse<List<string>>.Ok(statuses, "Statuses retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve statuses.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving statuses."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/settings/statuses
    /// Creates a new lead status with validation and duplicate prevention.
    /// </summary>
    [HttpPost("statuses")]
    public async Task<IActionResult> CreateStatus([FromBody] StatusInputDto input)
    {
        try
        {
            var value = input?.GetEffectiveValue();
            if (string.IsNullOrWhiteSpace(value))
            {
                return BadRequest(AppApiErrorResponse.Fail("Status name is required."));
            }

            var exists = await _db.StatusOptions
                .AnyAsync(s => s.Value.ToLower() == value.ToLower());

            if (exists)
            {
                return Conflict(AppApiErrorResponse.Fail($"Status '{value}' already exists."));
            }

            var statusOption = new LeadStatusOption { Value = value };
            _db.StatusOptions.Add(statusOption);
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<object>.Ok(new { id = statusOption.Id, value = statusOption.Value }, $"Status '{value}' created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create status.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating status."));
        }
    }

    /// <summary>
    /// DELETE /api/v1/app/settings/statuses/{status}
    /// Deletes a status using its name or integer identifier, preventing deletion if currently assigned to leads.
    /// </summary>
    [HttpDelete("statuses/{status}")]
    public async Task<IActionResult> DeleteStatus(string status)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return BadRequest(AppApiErrorResponse.Fail("Status identifier is required."));
            }

            status = status.Trim();
            LeadStatusOption? existing = null;

            if (int.TryParse(status, out var id))
            {
                existing = await _db.StatusOptions.FirstOrDefaultAsync(s => s.Id == id);
            }

            if (existing == null)
            {
                existing = await _db.StatusOptions.FirstOrDefaultAsync(s => s.Value.ToLower() == status.ToLower());
            }

            if (existing == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Status '{status}' not found."));
            }

            // Prevent deletion if the status is currently assigned to existing leads
            var inUse = await _db.Leads.AnyAsync(l => l.LeadStatus != null && l.LeadStatus.ToLower() == existing.Value.ToLower());
            if (inUse)
            {
                return Conflict(AppApiErrorResponse.Fail(
                    $"Status '{existing.Value}' cannot be deleted because it is currently assigned to one or more leads. Please reassign those leads before deleting this status."));
            }

            _db.StatusOptions.Remove(existing);
            await _db.SaveChangesAsync();

            return Ok(AppApiResponse.Ok($"Status '{existing.Value}' deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete status '{Status}'.", status);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while deleting the status."));
        }
    }

    // =========================================================================
    // 3. LEAD SOURCE MANAGEMENT APIs
    // =========================================================================

    /// <summary>
    /// GET /api/v1/app/settings/sources
    /// Retrieves all lead sources.
    /// </summary>
    [HttpGet("sources")]
    public async Task<IActionResult> GetSources()
    {
        try
        {
            var sources = await _db.SourceOptions
                .OrderBy(s => s.Id)
                .Select(s => s.Value)
                .ToListAsync();

            return Ok(AppApiResponse<List<string>>.Ok(sources, "Lead sources retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve lead sources.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving lead sources."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/settings/sources
    /// Creates a new lead source with duplicate prevention.
    /// </summary>
    [HttpPost("sources")]
    public async Task<IActionResult> CreateSource([FromBody] SourceInputDto input)
    {
        try
        {
            var value = input?.GetEffectiveValue();
            if (string.IsNullOrWhiteSpace(value))
            {
                return BadRequest(AppApiErrorResponse.Fail("Source name is required."));
            }

            var exists = await _db.SourceOptions
                .AnyAsync(s => s.Value.ToLower() == value.ToLower());

            if (exists)
            {
                return Conflict(AppApiErrorResponse.Fail($"Source '{value}' already exists."));
            }

            var sourceOption = new LeadSourceOption { Value = value };
            _db.SourceOptions.Add(sourceOption);
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<object>.Ok(new { id = sourceOption.Id, value = sourceOption.Value }, $"Lead source '{value}' created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create lead source.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating lead source."));
        }
    }

    /// <summary>
    /// DELETE /api/v1/app/settings/sources/{source}
    /// Safely deletes a lead source, verifying it is not in use by existing leads.
    /// </summary>
    [HttpDelete("sources/{source}")]
    public async Task<IActionResult> DeleteSource(string source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return BadRequest(AppApiErrorResponse.Fail("Source identifier is required."));
            }

            source = source.Trim();
            LeadSourceOption? existing = null;

            if (int.TryParse(source, out var id))
            {
                existing = await _db.SourceOptions.FirstOrDefaultAsync(s => s.Id == id);
            }

            if (existing == null)
            {
                existing = await _db.SourceOptions.FirstOrDefaultAsync(s => s.Value.ToLower() == source.ToLower());
            }

            if (existing == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Source '{source}' not found."));
            }

            // Check if source is currently in use
            var inUse = await _db.Leads.AnyAsync(l => l.Source != null && l.Source.ToLower() == existing.Value.ToLower());
            if (inUse)
            {
                return Conflict(AppApiErrorResponse.Fail(
                    $"Source '{existing.Value}' cannot be deleted because it is currently assigned to one or more leads. Please reassign the leads before deleting this source."));
            }

            _db.SourceOptions.Remove(existing);
            await _db.SaveChangesAsync();

            return Ok(AppApiResponse.Ok($"Lead source '{existing.Value}' deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lead source '{Source}'.", source);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while deleting lead source."));
        }
    }

    // =========================================================================
    // 4. SETTINGS USERS APIs
    // =========================================================================

    /// <summary>
    /// GET /api/v1/app/settings/users
    /// Retrieves users available in application settings (excluding sensitive password hashes).
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        try
        {
            var users = await _db.Users
                .OrderBy(u => u.Id)
                .Select(u => new AppSettingsUserDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Email = u.Email,
                    Role = u.Role,
                    Status = u.Status,
                    ModulePermissions = u.ModulePermissions,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            return Ok(AppApiResponse<List<AppSettingsUserDto>>.Ok(users, "Users retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve users.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving users."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/settings/users
    /// Creates or configures a user with validation, duplicate email prevention, and password hashing.
    /// </summary>
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateSettingsUserDto input)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.HasModulePermission(_db, currentUser, "Users & Roles", "Create")
                && !string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to create users."));
            }

            if (input == null)
            {
                return BadRequest(AppApiErrorResponse.Fail("Request payload is required."));
            }

            if (string.IsNullOrWhiteSpace(input.Name))
            {
                return BadRequest(AppApiErrorResponse.Fail("User name is required."));
            }

            var email = input.Email?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
            {
                return BadRequest(AppApiErrorResponse.Fail("A valid email address is required."));
            }

            var duplicate = await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email);
            if (duplicate)
            {
                return Conflict(AppApiErrorResponse.Fail("A user with that email address already exists."));
            }

            var roleName = string.IsNullOrWhiteSpace(input.Role) ? "Sales Executive" : input.Role.Trim();
            var roleExists = await _db.RoleMasters.AnyAsync(r => r.RoleName == roleName);
            if (!roleExists)
            {
                return BadRequest(AppApiErrorResponse.Fail($"Role '{roleName}' does not exist."));
            }

            var user = new UserRoleEntry
            {
                Name = input.Name.Trim(),
                Email = email,
                Role = roleName,
                Status = string.IsNullOrWhiteSpace(input.Status) ? "Active" : input.Status.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = currentUser?.Email ?? "System"
            };

            // Derive module permissions if not explicitly provided
            if (string.IsNullOrWhiteSpace(input.ModulePermissions))
            {
                var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == roleName);
                if (role != null)
                {
                    var perms = role.GetPermissions();
                    var permitted = perms.Where(p => p.View).Select(p => p.Module);
                    user.ModulePermissions = string.Join(",", permitted);
                }
            }
            else
            {
                user.ModulePermissions = input.ModulePermissions.Trim();
            }

            var passwordToUse = !string.IsNullOrWhiteSpace(input.Password)
                ? input.Password.Trim()
                : CrmLeadManagement.Security.PasswordGenerator.Generate();

            user.PasswordHash = _passwordHasher.HashPassword(user, passwordToUse);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var userDto = new AppSettingsUserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                Status = user.Status,
                ModulePermissions = user.ModulePermissions,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt
            };

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<object>.Ok(new
                {
                    user = userDto,
                    generatedPassword = string.IsNullOrWhiteSpace(input.Password) ? passwordToUse : null
                }, "User created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create user.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating the user."));
        }
    }

    /// <summary>
    /// DELETE /api/v1/app/settings/users/{email}
    /// Safely deletes or deactivates a user, preventing self-deletion and removal of the last administrator.
    /// </summary>
    [HttpDelete("users/{email}")]
    public async Task<IActionResult> DeleteUser(string email)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.HasModulePermission(_db, currentUser, "Users & Roles", "Delete")
                && !string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to delete users."));
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest(AppApiErrorResponse.Fail("User email is required."));
            }

            email = email.Trim().ToLowerInvariant();
            var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);
            if (targetUser == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"User with email '{email}' not found."));
            }

            // Prevent self-deletion
            if (currentUser != null && currentUser.Id == targetUser.Id)
            {
                return BadRequest(AppApiErrorResponse.Fail("You cannot delete your own account."));
            }

            // Prevent deleting the last administrator
            var isTargetAdmin = string.Equals(targetUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(targetUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase);

            if (isTargetAdmin)
            {
                var remainingAdmins = await _db.Users
                    .CountAsync(u => u.Id != targetUser.Id &&
                                     u.Status == "Active" &&
                                     (u.Role == "Admin" || u.Role == "Super Admin"));

                if (remainingAdmins == 0)
                {
                    return Conflict(AppApiErrorResponse.Fail("Cannot delete the last remaining active administrator account."));
                }
            }

            // Check if user is referenced in active leads, projects, or tasks
            var hasAssignedLeads = await _db.Leads.AnyAsync(l => l.AssignedTo == targetUser.Name || l.AssignedTo == targetUser.Email);
            var hasAssignedProjects = await _db.WorkProjects.AnyAsync(p => p.ProjectManager == targetUser.Name || (p.AssignedMembers != null && p.AssignedMembers.Contains(targetUser.Name!)));

            if (hasAssignedLeads || hasAssignedProjects)
            {
                // Soft deactivate instead of hard delete to preserve historical integrity
                targetUser.Status = "Inactive";
                await _db.SaveChangesAsync();
                return Ok(AppApiResponse.Ok($"User '{targetUser.Email}' has assigned records and has been safely deactivated instead of deleted."));
            }

            _db.Users.Remove(targetUser);
            await _db.SaveChangesAsync();

            return Ok(AppApiResponse.Ok($"User '{email}' deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user '{Email}'.", email);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while deleting the user."));
        }
    }

    // =========================================================================
    // 5. TAX MANAGEMENT APIs
    // =========================================================================

    /// <summary>
    /// GET /api/v1/app/settings/taxes
    /// Retrieves all tax configurations.
    /// </summary>
    [HttpGet("taxes")]
    public async Task<IActionResult> GetTaxes()
    {
        try
        {
            var taxes = await _db.TaxMasters
                .OrderBy(t => t.TaxType)
                .ThenBy(t => t.TaxRate)
                .ToListAsync();

            return Ok(AppApiResponse<List<TaxMaster>>.Ok(taxes, "Taxes retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve taxes.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving taxes."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/settings/taxes/active
    /// Retrieves active taxes only.
    /// IMPORTANT: Declared BEFORE taxes/{id} to prevent routing conflict.
    /// </summary>
    [HttpGet("taxes/active")]
    public async Task<IActionResult> GetActiveTaxes()
    {
        try
        {
            var activeTaxes = await _db.TaxMasters
                .Where(t => t.Status == "Active")
                .OrderBy(t => t.TaxType)
                .ThenBy(t => t.TaxRate)
                .ToListAsync();

            return Ok(AppApiResponse<List<TaxMaster>>.Ok(activeTaxes, "Active taxes retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve active taxes.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving active taxes."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/settings/taxes
    /// Creates a new tax configuration with rate validation (0-100%).
    /// </summary>
    [HttpPost("taxes")]
    public async Task<IActionResult> CreateTax([FromBody] TaxInputDto input)
    {
        try
        {
            if (input == null || string.IsNullOrWhiteSpace(input.TaxName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Tax name is required."));
            }

            if (input.TaxRate < 0 || input.TaxRate > 100)
            {
                return BadRequest(AppApiErrorResponse.Fail("Tax rate must be between 0% and 100%."));
            }

            var taxName = input.TaxName.Trim();
            var taxType = string.IsNullOrWhiteSpace(input.TaxType) ? "GST" : input.TaxType.Trim();

            var exists = await _db.TaxMasters.AnyAsync(t =>
                t.TaxName.ToLower() == taxName.ToLower() &&
                t.TaxRate == input.TaxRate &&
                t.TaxType.ToLower() == taxType.ToLower());

            if (exists)
            {
                return Conflict(AppApiErrorResponse.Fail($"A tax configuration for '{taxName}' with rate {input.TaxRate}% already exists."));
            }

            var tax = new TaxMaster
            {
                TaxName = taxName,
                TaxType = taxType,
                TaxRate = input.TaxRate,
                Description = input.Description?.Trim() ?? "",
                Status = string.IsNullOrWhiteSpace(input.Status) ? "Active" : input.Status.Trim()
            };

            _db.TaxMasters.Add(tax);
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<TaxMaster>.Ok(tax, $"Tax '{tax.TaxName}' created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tax configuration.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating tax configuration."));
        }
    }

    /// <summary>
    /// PUT /api/v1/app/settings/taxes/{id}
    /// Updates a tax configuration while preserving historical invoice calculations.
    /// </summary>
    [HttpPut("taxes/{id:int}")]
    public async Task<IActionResult> UpdateTax(int id, [FromBody] TaxInputDto input)
    {
        try
        {
            if (input == null || string.IsNullOrWhiteSpace(input.TaxName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Tax name is required."));
            }

            if (input.TaxRate < 0 || input.TaxRate > 100)
            {
                return BadRequest(AppApiErrorResponse.Fail("Tax rate must be between 0% and 100%."));
            }

            var tax = await _db.TaxMasters.FindAsync(id);
            if (tax == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Tax with ID {id} not found."));
            }

            tax.TaxName = input.TaxName.Trim();
            tax.TaxType = string.IsNullOrWhiteSpace(input.TaxType) ? tax.TaxType : input.TaxType.Trim();
            tax.TaxRate = input.TaxRate;
            tax.Description = input.Description?.Trim() ?? "";
            tax.Status = string.IsNullOrWhiteSpace(input.Status) ? tax.Status : input.Status.Trim();

            await _db.SaveChangesAsync();

            return Ok(AppApiResponse<TaxMaster>.Ok(tax, $"Tax '{tax.TaxName}' updated successfully. Historical invoice values are preserved."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update tax ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while updating tax configuration."));
        }
    }

    /// <summary>
    /// DELETE /api/v1/app/settings/taxes/{id}
    /// Safely deletes a tax configuration if not referenced by active invoices.
    /// </summary>
    [HttpDelete("taxes/{id:int}")]
    public async Task<IActionResult> DeleteTax(int id)
    {
        try
        {
            var tax = await _db.TaxMasters.FindAsync(id);
            if (tax == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Tax with ID {id} not found."));
            }

            // Check if any invoice references this tax name
            var referenced = await _db.WorkInvoices.AnyAsync(i => i.TaxName == tax.TaxName);
            if (referenced)
            {
                return Conflict(AppApiErrorResponse.Fail(
                    $"Tax '{tax.TaxName}' is referenced in one or more invoices. To preserve audit and invoice history, consider disabling it via toggle rather than deleting."));
            }

            _db.TaxMasters.Remove(tax);
            await _db.SaveChangesAsync();

            return Ok(AppApiResponse.Ok($"Tax '{tax.TaxName}' deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tax ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while deleting tax configuration."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/settings/taxes/{id}/toggle
    /// Enables or disables a tax configuration.
    /// </summary>
    [HttpPost("taxes/{id:int}/toggle")]
    public async Task<IActionResult> ToggleTax(int id)
    {
        try
        {
            var tax = await _db.TaxMasters.FindAsync(id);
            if (tax == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Tax with ID {id} not found."));
            }

            tax.Status = string.Equals(tax.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
            await _db.SaveChangesAsync();

            return Ok(AppApiResponse<TaxMaster>.Ok(tax, $"Tax '{tax.TaxName}' status toggled to '{tax.Status}'."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle tax ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while toggling tax status."));
        }
    }

    // =========================================================================
    // 6. FOLLOW-UP SETTINGS APIs
    // =========================================================================

    /// <summary>
    /// GET /api/v1/app/settings/followup
    /// Retrieves follow-up settings configuration.
    /// </summary>
    [HttpGet("followup")]
    public async Task<IActionResult> GetFollowUp()
    {
        try
        {
            var followUp = await _db.FollowUpSettings.FirstOrDefaultAsync(f => f.Id == 1);
            if (followUp == null)
            {
                followUp = new FollowUpSettings { Id = 1 };
                _db.FollowUpSettings.Add(followUp);
                await _db.SaveChangesAsync();
            }

            return Ok(AppApiResponse<FollowUpSettings>.Ok(followUp, "Follow-up settings retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve follow-up settings.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving follow-up settings."));
        }
    }

    /// <summary>
    /// PUT /api/v1/app/settings/followup
    /// Updates follow-up settings configuration with field validation.
    /// </summary>
    [HttpPut("followup")]
    public async Task<IActionResult> UpdateFollowUp([FromBody] FollowUpSettingsInputDto input)
    {
        try
        {
            if (input == null)
            {
                return BadRequest(AppApiErrorResponse.Fail("Request payload is required."));
            }

            if (input.DefaultReminderDaysBefore < 0)
            {
                return BadRequest(AppApiErrorResponse.Fail("Default reminder days cannot be negative."));
            }

            if (input.AutoEscalateAfterDaysOverdue < 0)
            {
                return BadRequest(AppApiErrorResponse.Fail("Auto escalate days cannot be negative."));
            }

            if (string.IsNullOrWhiteSpace(input.DefaultFollowUpType))
            {
                return BadRequest(AppApiErrorResponse.Fail("Default follow-up type is required."));
            }

            var followUp = await _db.FollowUpSettings.FirstOrDefaultAsync(f => f.Id == 1);
            if (followUp == null)
            {
                followUp = new FollowUpSettings { Id = 1 };
                _db.FollowUpSettings.Add(followUp);
            }

            followUp.DefaultReminderDaysBefore = input.DefaultReminderDaysBefore;
            followUp.EnableEmailReminders = input.EnableEmailReminders;
            followUp.EnableSmsReminders = input.EnableSmsReminders;
            followUp.DefaultFollowUpType = input.DefaultFollowUpType.Trim();
            followUp.AutoEscalateAfterDaysOverdue = input.AutoEscalateAfterDaysOverdue;

            await _db.SaveChangesAsync();

            return Ok(AppApiResponse<FollowUpSettings>.Ok(followUp, "Follow-up settings updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update follow-up settings.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while updating follow-up settings."));
        }
    }
}
