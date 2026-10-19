using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// Mobile/App-specific JSON REST API for leads.
/// Base route: /api/v1/app/leads
/// </summary>
[ApiController]
[Route("api/v1/app/leads")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppLeadsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppLeadsController> _logger;
    private readonly IAuditLogService _auditLogService;

    public AppLeadsController(AppDbContext db, ILogger<AppLeadsController> logger, IAuditLogService auditLogService)
    {
        _db = db;
        _logger = logger;
        _auditLogService = auditLogService;
    }

    private async Task<string> GenerateNextLeadIdAsync()
    {
        var existingIds = await _db.Leads
            .Select(l => l.LeadId)
            .ToListAsync();

        var maxNum = existingIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.StartsWith("LD-", StringComparison.OrdinalIgnoreCase) ? id.Substring(3) : id)
            .Select(n => int.TryParse(n, out var v) ? v : 1000)
            .DefaultIfEmpty(1000)
            .Max();

        int nextNum = Math.Max(maxNum + 1, 1001);
        while (existingIds.Any(id => string.Equals(id, $"LD-{nextNum}", StringComparison.OrdinalIgnoreCase)))
        {
            nextNum++;
        }
        return $"LD-{nextNum}";
    }

    /// <summary>
    /// Gets all leads formatted for app consumption.
    /// Route: GET /api/v1/app/leads
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Lead>>> GetLeads()
    {
        var leads = await _db.Leads.AsNoTracking().OrderByDescending(l => l.Id).ToListAsync();
        return Ok(leads);
    }

    /// <summary>
    /// Gets lead count and summary metrics formatted for mobile/app consumption.
    /// Route: GET /api/v1/app/leads/count (also /api/v1/app/leads/counts)
    /// </summary>
    [HttpGet("count")]
    [HttpGet("counts")]
    public async Task<ActionResult<LeadCountSummaryDto>> GetLeadCounts([FromQuery] string? assignedTo)
    {
        var query = _db.Leads.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(assignedTo))
        {
            query = query.Where(l => l.AssignedTo == assignedTo);
        }

        var total = await query.CountAsync();
        var assigned = await query.CountAsync(l => !string.IsNullOrEmpty(l.AssignedTo));
        var unassigned = total - assigned;

        var statusGroups = await query
            .Where(l => !string.IsNullOrEmpty(l.LeadStatus))
            .GroupBy(l => l.LeadStatus!)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        var priorityGroups = await query
            .Where(l => !string.IsNullOrEmpty(l.Priority))
            .GroupBy(l => l.Priority!)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Priority, x => x.Count);

        return Ok(new LeadCountSummaryDto
        {
            Total = total,
            Assigned = assigned,
            Unassigned = unassigned,
            ByStatus = statusGroups,
            ByPriority = priorityGroups
        });
    }

    /// <summary>
    /// Gets a single lead by LeadId code or numeric ID.
    /// Route: GET /api/v1/app/leads/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Lead>> GetLead(string id)
    {
        Lead? lead;
        if (int.TryParse(id, out var numId))
        {
            lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == numId || l.LeadId == id);
        }
        else
        {
            lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id);
        }

        if (lead == null)
        {
            return NotFound(new ApiError { Message = $"Lead '{id}' not found." });
        }

        return Ok(lead);
    }

    /// <summary>
    /// Creates a new lead.
    /// Route: POST /api/v1/app/leads
    /// </summary>
    [HttpPost]
    [HttpPost("add")]
    [HttpPost("create")]
    public async Task<IActionResult> CreateLead([FromBody] AppLeadInputDto input)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Lead", "Create"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to create leads."));
            }

            if (input == null || string.IsNullOrWhiteSpace(input.LeadName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Lead name is required."));
            }

            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var leadId = await GenerateNextLeadIdAsync();

            var lead = new Lead
            {
                LeadId = leadId,
                LeadName = input.LeadName.Trim(),
                CompanyName = input.CompanyName?.Trim() ?? "",
                MobileNumber = input.MobileNumber?.Trim() ?? "",
                Email = input.Email?.Trim() ?? "",
                Designation = input.Designation?.Trim() ?? "",
                Location = input.Location?.Trim() ?? "",
                Source = input.Source?.Trim() ?? "",
                LeadStatus = string.IsNullOrWhiteSpace(input.LeadStatus) ? "New" : input.LeadStatus.Trim(),
                AssignedTo = input.AssignedTo?.Trim() ?? "",
                Priority = string.IsNullOrWhiteSpace(input.Priority) ? "Medium" : input.Priority.Trim(),
                CreatedDate = today,
                AssignedDate = !string.IsNullOrWhiteSpace(input.AssignedTo) ? today : "",
                FollowUpDate = input.FollowUpDate?.Trim() ?? "",
                Remarks = input.Remarks?.Trim()
            };

            if (!string.IsNullOrWhiteSpace(lead.AssignedTo))
            {
                lead.AssignmentHistory.Add(new AssignmentEntry
                {
                    AssignedEmployee = lead.AssignedTo,
                    AssignmentDate = today,
                    Remarks = "Initial lead assignment upon creation"
                });
            }

            _db.Leads.Add(lead);
            await _db.SaveChangesAsync();

            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? currentUser?.Email ?? "system";
            await _auditLogService.LogAsync(
                action: "Create Lead",
                performedBy: userEmail,
                targetUserId: lead.Id,
                targetUserEmail: lead.Email,
                details: $"Created new lead '{lead.LeadName}' ({lead.LeadId}) assigned to '{lead.AssignedTo}'.",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<Lead>.Ok(lead, $"Lead '{lead.LeadName}' ({lead.LeadId}) created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create lead via App API.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating the lead: " + ex.Message));
        }
    }

    /// <summary>
    /// Updates an existing lead.
    /// Route: PUT /api/v1/app/leads/{id}
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateLead(string id, [FromBody] AppLeadInputDto input)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Lead", "Edit"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to update leads."));
            }

            Lead? existing;
            if (int.TryParse(id, out var numId))
            {
                existing = await _db.Leads.FirstOrDefaultAsync(l => l.Id == numId || l.LeadId == id);
            }
            else
            {
                existing = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id);
            }

            if (existing == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
            }

            if (input == null || string.IsNullOrWhiteSpace(input.LeadName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Lead name is required."));
            }

            var previousAssignedTo = existing.AssignedTo;
            existing.LeadName = input.LeadName.Trim();
            existing.CompanyName = input.CompanyName?.Trim() ?? "";
            existing.MobileNumber = input.MobileNumber?.Trim() ?? "";
            existing.Email = input.Email?.Trim() ?? "";
            existing.Designation = input.Designation?.Trim() ?? "";
            existing.Location = input.Location?.Trim() ?? "";
            existing.Source = input.Source?.Trim() ?? "";
            existing.LeadStatus = string.IsNullOrWhiteSpace(input.LeadStatus) ? existing.LeadStatus : input.LeadStatus.Trim();
            existing.Priority = string.IsNullOrWhiteSpace(input.Priority) ? existing.Priority : input.Priority.Trim();
            existing.FollowUpDate = input.FollowUpDate?.Trim() ?? existing.FollowUpDate;
            existing.Remarks = input.Remarks?.Trim();

            var newAssigned = input.AssignedTo?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(newAssigned) && !string.Equals(previousAssignedTo, newAssigned, StringComparison.OrdinalIgnoreCase))
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                existing.AssignedTo = newAssigned;
                existing.AssignedDate = today;
                existing.AssignmentHistory.Add(new AssignmentEntry
                {
                    AssignedEmployee = newAssigned,
                    AssignmentDate = today,
                    Remarks = $"Reassigned from '{previousAssignedTo}' to '{newAssigned}'"
                });
            }

            await _db.SaveChangesAsync();

            return Ok(AppApiResponse<Lead>.Ok(existing, $"Lead '{existing.LeadName}' ({existing.LeadId}) updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update lead '{LeadId}'.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while updating the lead."));
        }
    }

    /// <summary>
    /// Deletes a lead.
    /// Route: DELETE /api/v1/app/leads/{id}
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteLead(string id)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Lead", "Delete"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to delete leads."));
            }

            Lead? existing;
            if (int.TryParse(id, out var numId))
            {
                existing = await _db.Leads.FirstOrDefaultAsync(l => l.Id == numId || l.LeadId == id);
            }
            else
            {
                existing = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id);
            }

            if (existing == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
            }

            _db.Leads.Remove(existing);
            await _db.SaveChangesAsync();

            return Ok(AppApiResponse.Ok($"Lead '{existing.LeadName}' ({existing.LeadId}) deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lead '{LeadId}'.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while deleting the lead."));
        }
    }
}
