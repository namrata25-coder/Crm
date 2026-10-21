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

    private async Task<Lead?> FindLeadAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim();

        if (int.TryParse(trimmed, out var numId))
        {
            var lead = await _db.Leads
                .Include(l => l.CallHistory)
                .Include(l => l.FollowUps)
                .Include(l => l.Visits)
                .Include(l => l.Requirements)
                .Include(l => l.AssignmentHistory)
                .FirstOrDefaultAsync(l => l.Id == numId || l.LeadId == trimmed);
            if (lead != null) return lead;

            var prefixed = await _db.Leads
                .Include(l => l.CallHistory)
                .Include(l => l.FollowUps)
                .Include(l => l.Visits)
                .Include(l => l.Requirements)
                .Include(l => l.AssignmentHistory)
                .FirstOrDefaultAsync(l => l.LeadId == $"LD-{numId}" || l.LeadId == $"LD-100{numId}" || l.LeadId == $"LD-00{numId}" || l.LeadId.EndsWith(numId.ToString()));
            if (prefixed != null) return prefixed;
        }

        return await _db.Leads
            .Include(l => l.CallHistory)
            .Include(l => l.FollowUps)
            .Include(l => l.Visits)
            .Include(l => l.Requirements)
            .Include(l => l.AssignmentHistory)
            .FirstOrDefaultAsync(l => l.LeadId == trimmed);
    }

    /// <summary>
    /// Gets lead count and summary metrics formatted for mobile/app consumption.
    /// Route: GET /api/v1/app/leads/stats (also /statistics, /count, /counts)
    /// </summary>
    [HttpGet("stats", Order = -1)]
    [HttpGet("statistics", Order = -1)]
    [HttpGet("count", Order = -1)]
    [HttpGet("counts", Order = -1)]
    [HttpGet("/api/leads/stats", Order = -1)]
    [HttpGet("/api/leads/count", Order = -1)]
    [HttpGet("/api/leads/counts", Order = -1)]
    public async Task<IActionResult> GetLeadStats([FromQuery] string? assignedTo)
    {
        try
        {
            var query = _db.Leads.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(assignedTo))
            {
                query = query.Where(l => l.AssignedTo == assignedTo);
            }

            // Project only the required columns to avoid heavy DB payload and prevent EF Core case-sensitivity dictionary collisions
            var leads = await query
                .Select(l => new
                {
                    AssignedTo = l.AssignedTo,
                    LeadStatus = l.LeadStatus,
                    Priority = l.Priority,
                    Source = l.Source
                })
                .ToListAsync();

            var total = leads.Count;
            var assigned = leads.Count(l => !string.IsNullOrWhiteSpace(l.AssignedTo));
            var unassigned = total - assigned;

            // Group case-insensitively in memory to prevent System.ArgumentException: An item with the same key has already been added
            var statusGroups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in leads.Where(l => !string.IsNullOrWhiteSpace(l.LeadStatus))
                                       .GroupBy(l => l.LeadStatus!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                statusGroups[group.Key] = group.Count();
            }

            var priorityGroups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in leads.Where(l => !string.IsNullOrWhiteSpace(l.Priority))
                                       .GroupBy(l => l.Priority!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                priorityGroups[group.Key] = group.Count();
            }

            var sourceGroups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in leads.Where(l => !string.IsNullOrWhiteSpace(l.Source))
                                       .GroupBy(l => l.Source!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                sourceGroups[group.Key] = group.Count();
            }

            int GetStatusCount(params string[] aliases)
            {
                return statusGroups
                    .Where(kvp => aliases.Any(a => string.Equals(kvp.Key, a, StringComparison.OrdinalIgnoreCase)))
                    .Sum(kvp => kvp.Value);
            }

            var statsData = new AppLeadStatsDto
            {
                Total = total,
                Assigned = assigned,
                Unassigned = unassigned,
                New = GetStatusCount("New"),
                InProgress = GetStatusCount("In Progress", "InProgress", "In-Progress"),
                Contacted = GetStatusCount("Contacted"),
                Qualified = GetStatusCount("Qualified"),
                Proposal = GetStatusCount("Proposal", "Proposal Sent", "ProposalSent"),
                Won = GetStatusCount("Won", "Converted", "Deal Closed"),
                Lost = GetStatusCount("Lost", "Dropped"),
                Closed = GetStatusCount("Closed"),
                ByStatus = statusGroups,
                ByPriority = priorityGroups,
                BySource = sourceGroups
            };

            var envelope = new AppLeadStatsEnvelope
            {
                Success = true,
                Message = "Lead statistics retrieved successfully.",
                Data = statsData,
                Total = statsData.Total,
                Assigned = statsData.Assigned,
                Unassigned = statsData.Unassigned,
                New = statsData.New,
                InProgress = statsData.InProgress,
                Contacted = statsData.Contacted,
                Qualified = statsData.Qualified,
                Proposal = statsData.Proposal,
                Won = statsData.Won,
                Lost = statsData.Lost,
                Closed = statsData.Closed,
                ByStatus = statsData.ByStatus,
                ByPriority = statsData.ByPriority,
                BySource = statsData.BySource
            };

            return Ok(envelope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute lead statistics.");
            // Safe fallback response to ensure mobile app never crashes on 500
            var fallbackStats = new AppLeadStatsDto();
            return Ok(new AppLeadStatsEnvelope
            {
                Success = true,
                Message = "Lead statistics retrieved (fallback mode).",
                Data = fallbackStats
            });
        }
    }

    /// <summary>
    /// Gets a single lead by LeadId code or numeric ID.
    /// Route: GET /api/v1/app/leads/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetLead(string id)
    {
        if (string.Equals(id, "stats", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "statistics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "count", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "counts", StringComparison.OrdinalIgnoreCase))
        {
            return await GetLeadStats(null);
        }

        var lead = await FindLeadAsync(id);
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
            if (currentUser != null && string.Equals(currentUser.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("Your account is inactive. Please contact administrator."));
            }

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
                Remarks = input.Remarks?.Trim() ?? ""
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
            existing.Remarks = input.Remarks?.Trim() ?? existing.Remarks ?? "";

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

    // ==========================================
    // CALLS SUB-MODULE
    // ==========================================

    /// <summary>
    /// Retrieves all call history entries logged for a lead.
    /// Route: GET /api/v1/app/leads/{id}/calls
    /// </summary>
    [HttpGet("{id}/calls")]
    [HttpGet("{id}/call")]
    [HttpGet("{id}/call-details")]
    [HttpGet("/api/leads/{id}/calls")]
    [HttpGet("/api/leads/{id}/call-details")]
    public async Task<IActionResult> GetLeadCalls(string id)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        return Ok(AppApiResponse<List<CallHistoryEntry>>.Ok(
            lead.CallHistory.OrderByDescending(c => c.Id).ToList(),
            "Call logs retrieved successfully."));
    }

    /// <summary>
    /// Logs a new call against a lead.
    /// Route: POST /api/v1/app/leads/{id}/calls
    /// </summary>
    [HttpPost("{id}/calls")]
    [HttpPost("{id}/call")]
    [HttpPost("{id}/call-details")]
    [HttpPost("/api/leads/{id}/calls")]
    [HttpPost("/api/leads/{id}/call-details")]
    public async Task<IActionResult> AddCall(string id, [FromBody] AppCallInputDto input)
    {
        try
        {
            var lead = await FindLeadAsync(id);
            if (lead == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
            }

            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var nowTime = DateTime.UtcNow.ToString("HH:mm");

            var remarkText = !string.IsNullOrWhiteSpace(input?.Remark) ? input.Remark :
                             (!string.IsNullOrWhiteSpace(input?.Remarks) ? input.Remarks : (input?.Notes ?? ""));

            var entry = new CallHistoryEntry
            {
                CallDate = !string.IsNullOrWhiteSpace(input?.CallDate) ? input.CallDate.Trim() : today,
                CallTime = !string.IsNullOrWhiteSpace(input?.CallTime) ? input.CallTime.Trim() : nowTime,
                CallType = !string.IsNullOrWhiteSpace(input?.CallType) ? input.CallType.Trim() : "Outgoing",
                CallStatus = !string.IsNullOrWhiteSpace(input?.CallStatus) ? input.CallStatus.Trim() : "Connected",
                Duration = !string.IsNullOrWhiteSpace(input?.Duration) ? input.Duration.Trim() : "00:00",
                Employee = !string.IsNullOrWhiteSpace(input?.Employee) ? input.Employee.Trim() : (lead.AssignedTo ?? currentUser?.Name ?? "Staff"),
                Notes = remarkText.Trim()
            };

            lead.CallHistory.Add(entry);
            await _db.SaveChangesAsync();

            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? currentUser?.Email ?? "system";
            await _auditLogService.LogAsync(
                action: "Add Lead Call",
                performedBy: userEmail,
                targetUserId: lead.Id,
                targetUserEmail: lead.Email,
                details: $"Logged {entry.CallType} call ({entry.CallStatus}) for lead '{lead.LeadName}' ({lead.LeadId}).",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<CallHistoryEntry>.Ok(entry, "Call logged successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log call for lead '{LeadId}'.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while logging call: " + ex.Message));
        }
    }

    /// <summary>
    /// Deletes a call log entry.
    /// Route: DELETE /api/v1/app/leads/{id}/calls/{callId:int}
    /// </summary>
    [HttpDelete("{id}/calls/{callId:int}")]
    [HttpDelete("/api/leads/{id}/calls/{callId:int}")]
    public async Task<IActionResult> DeleteCall(string id, int callId)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        var entry = lead.CallHistory.FirstOrDefault(c => c.Id == callId);
        if (entry == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Call entry with ID '{callId}' not found."));
        }

        lead.CallHistory.Remove(entry);
        await _db.SaveChangesAsync();

        return Ok(AppApiResponse.Ok("Call log deleted successfully."));
    }

    // ==========================================
    // FOLLOW-UPS SUB-MODULE
    // ==========================================

    /// <summary>
    /// Retrieves all follow-ups scheduled for a lead.
    /// Route: GET /api/v1/app/leads/{id}/followups
    /// </summary>
    [HttpGet("{id}/followups")]
    [HttpGet("{id}/followup")]
    [HttpGet("{id}/follow-ups")]
    [HttpGet("{id}/follow-up")]
    [HttpGet("/api/leads/{id}/followups")]
    [HttpGet("/api/leads/{id}/follow-ups")]
    public async Task<IActionResult> GetLeadFollowUps(string id)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        return Ok(AppApiResponse<List<FollowUpEntry>>.Ok(
            lead.FollowUps.OrderByDescending(f => f.Id).ToList(),
            "Follow-ups retrieved successfully."));
    }

    /// <summary>
    /// Schedules a new follow-up for a lead.
    /// Route: POST /api/v1/app/leads/{id}/followups
    /// </summary>
    [HttpPost("{id}/followups")]
    [HttpPost("{id}/followup")]
    [HttpPost("{id}/follow-ups")]
    [HttpPost("{id}/follow-up")]
    [HttpPost("/api/leads/{id}/followups")]
    [HttpPost("/api/leads/{id}/follow-ups")]
    public async Task<IActionResult> AddFollowUp(string id, [FromBody] AppFollowUpInputDto input)
    {
        try
        {
            var lead = await FindLeadAsync(id);
            if (lead == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
            }

            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var nowTime = DateTime.UtcNow.ToString("HH:mm");

            var remarksText = !string.IsNullOrWhiteSpace(input?.Remarks) ? input.Remarks : (input?.Remark ?? "");

            var entry = new FollowUpEntry
            {
                FollowUpDate = !string.IsNullOrWhiteSpace(input?.FollowUpDate) ? input.FollowUpDate.Trim() : today,
                FollowUpTime = !string.IsNullOrWhiteSpace(input?.FollowUpTime) ? input.FollowUpTime.Trim() : nowTime,
                FollowUpType = !string.IsNullOrWhiteSpace(input?.FollowUpType) ? input.FollowUpType.Trim() : "Call",
                Status = !string.IsNullOrWhiteSpace(input?.Status) ? input.Status.Trim() : "Scheduled",
                AssignedEmployee = !string.IsNullOrWhiteSpace(input?.AssignedEmployee) ? input.AssignedEmployee.Trim() :
                                   (!string.IsNullOrWhiteSpace(input?.Employee) ? input.Employee.Trim() : (lead.AssignedTo ?? currentUser?.Name ?? "Staff")),
                Remarks = remarksText.Trim()
            };

            lead.FollowUps.Add(entry);
            lead.FollowUpDate = entry.FollowUpDate;
            await _db.SaveChangesAsync();

            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? currentUser?.Email ?? "system";
            await _auditLogService.LogAsync(
                action: "Add Lead Follow-up",
                performedBy: userEmail,
                targetUserId: lead.Id,
                targetUserEmail: lead.Email,
                details: $"Scheduled follow-up ({entry.FollowUpType}) for lead '{lead.LeadName}' on {entry.FollowUpDate}.",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<FollowUpEntry>.Ok(entry, "Follow-up scheduled successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule follow-up for lead '{LeadId}'.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while scheduling follow-up: " + ex.Message));
        }
    }

    /// <summary>
    /// Deletes a follow-up entry.
    /// Route: DELETE /api/v1/app/leads/{id}/followups/{followUpId:int}
    /// </summary>
    [HttpDelete("{id}/followups/{followUpId:int}")]
    [HttpDelete("{id}/follow-ups/{followUpId:int}")]
    [HttpDelete("/api/leads/{id}/followups/{followUpId:int}")]
    [HttpDelete("/api/leads/{id}/follow-ups/{followUpId:int}")]
    public async Task<IActionResult> DeleteFollowUp(string id, int followUpId)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        var entry = lead.FollowUps.FirstOrDefault(f => f.Id == followUpId);
        if (entry == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Follow-up with ID '{followUpId}' not found."));
        }

        lead.FollowUps.Remove(entry);
        await _db.SaveChangesAsync();

        return Ok(AppApiResponse.Ok("Follow-up deleted successfully."));
    }

    // ==========================================
    // VISITS SUB-MODULE
    // ==========================================

    /// <summary>
    /// Retrieves all client visits recorded for a lead.
    /// Route: GET /api/v1/app/leads/{id}/visits
    /// </summary>
    [HttpGet("{id}/visits")]
    [HttpGet("{id}/visit")]
    [HttpGet("{id}/client-visits")]
    [HttpGet("/api/leads/{id}/visits")]
    [HttpGet("/api/leads/{id}/client-visits")]
    public async Task<IActionResult> GetLeadVisits(string id)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        return Ok(AppApiResponse<List<VisitEntry>>.Ok(
            lead.Visits.OrderByDescending(v => v.Id).ToList(),
            "Visits retrieved successfully."));
    }

    /// <summary>
    /// Records a new visit against a lead.
    /// Route: POST /api/v1/app/leads/{id}/visits
    /// </summary>
    [HttpPost("{id}/visits")]
    [HttpPost("{id}/visit")]
    [HttpPost("{id}/client-visits")]
    [HttpPost("/api/leads/{id}/visits")]
    [HttpPost("/api/leads/{id}/client-visits")]
    public async Task<IActionResult> AddVisit(string id, [FromBody] AppVisitInputDto input)
    {
        try
        {
            var lead = await FindLeadAsync(id);
            if (lead == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
            }

            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var nowTime = DateTime.UtcNow.ToString("HH:mm");

            var remarksText = !string.IsNullOrWhiteSpace(input?.Remarks) ? input.Remarks : (input?.Remark ?? "");

            var entry = new VisitEntry
            {
                VisitDate = !string.IsNullOrWhiteSpace(input?.VisitDate) ? input.VisitDate.Trim() : today,
                VisitTime = !string.IsNullOrWhiteSpace(input?.VisitTime) ? input.VisitTime.Trim() : nowTime,
                VisitLocation = input?.VisitLocation?.Trim() ?? "",
                Employee = !string.IsNullOrWhiteSpace(input?.Employee) ? input.Employee.Trim() : (lead.AssignedTo ?? currentUser?.Name ?? "Staff"),
                Outcome = !string.IsNullOrWhiteSpace(input?.Outcome) ? input.Outcome.Trim() : "Interested",
                Conversation = input?.Conversation?.Trim() ?? "",
                Remarks = remarksText.Trim()
            };

            lead.Visits.Add(entry);
            await _db.SaveChangesAsync();

            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? currentUser?.Email ?? "system";
            await _auditLogService.LogAsync(
                action: "Add Lead Visit",
                performedBy: userEmail,
                targetUserId: lead.Id,
                targetUserEmail: lead.Email,
                details: $"Recorded visit at '{entry.VisitLocation}' for lead '{lead.LeadName}'.",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<VisitEntry>.Ok(entry, "Visit recorded successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record visit for lead '{LeadId}'.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while recording visit: " + ex.Message));
        }
    }

    /// <summary>
    /// Deletes a visit entry.
    /// Route: DELETE /api/v1/app/leads/{id}/visits/{visitId:int}
    /// </summary>
    [HttpDelete("{id}/visits/{visitId:int}")]
    [HttpDelete("{id}/client-visits/{visitId:int}")]
    [HttpDelete("/api/leads/{id}/visits/{visitId:int}")]
    [HttpDelete("/api/leads/{id}/client-visits/{visitId:int}")]
    public async Task<IActionResult> DeleteVisit(string id, int visitId)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        var entry = lead.Visits.FirstOrDefault(v => v.Id == visitId);
        if (entry == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Visit with ID '{visitId}' not found."));
        }

        lead.Visits.Remove(entry);
        await _db.SaveChangesAsync();

        return Ok(AppApiResponse.Ok("Visit deleted successfully."));
    }

    // ==========================================
    // REQUIREMENTS SUB-MODULE
    // ==========================================

    /// <summary>
    /// Retrieves all client requirements recorded for a lead.
    /// Route: GET /api/v1/app/leads/{id}/requirements
    /// </summary>
    [HttpGet("{id}/requirements")]
    [HttpGet("{id}/requirement")]
    [HttpGet("/api/leads/{id}/requirements")]
    public async Task<IActionResult> GetLeadRequirements(string id)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        return Ok(AppApiResponse<List<RequirementEntry>>.Ok(
            lead.Requirements.OrderByDescending(r => r.Id).ToList(),
            "Requirements retrieved successfully."));
    }

    /// <summary>
    /// Records a new requirement against a lead.
    /// Route: POST /api/v1/app/leads/{id}/requirements
    /// </summary>
    [HttpPost("{id}/requirements")]
    [HttpPost("{id}/requirement")]
    [HttpPost("/api/leads/{id}/requirements")]
    public async Task<IActionResult> AddRequirement(string id, [FromBody] AppRequirementInputDto input)
    {
        try
        {
            var lead = await FindLeadAsync(id);
            if (lead == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
            }

            var entry = new RequirementEntry
            {
                Title = input?.Title?.Trim() ?? (input?.ItemName?.Trim() ?? "New Requirement"),
                Description = input?.Description?.Trim() ?? (input?.Details?.Trim() ?? ""),
                Priority = !string.IsNullOrWhiteSpace(input?.Priority) ? input.Priority.Trim() : "Medium",
                ExpectedDate = input?.ExpectedDate?.Trim() ?? "",
                Budget = input?.Budget?.Trim() ?? "",
                Status = !string.IsNullOrWhiteSpace(input?.Status) ? input.Status.Trim() : "New",
                RequirementType = input?.RequirementType?.Trim() ?? "",
                ItemName = input?.ItemName?.Trim() ?? (input?.Title?.Trim() ?? ""),
                Quantity = input?.Quantity?.Trim() ?? "1",
                Details = input?.Details?.Trim() ?? "",
                Remarks = input?.Remarks?.Trim() ?? ""
            };

            lead.Requirements.Add(entry);
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<RequirementEntry>.Ok(entry, "Requirement added successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add requirement for lead '{LeadId}'.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while adding requirement: " + ex.Message));
        }
    }

    /// <summary>
    /// Deletes a requirement entry.
    /// Route: DELETE /api/v1/app/leads/{id}/requirements/{reqId:int}
    /// </summary>
    [HttpDelete("{id}/requirements/{reqId:int}")]
    [HttpDelete("/api/leads/{id}/requirements/{reqId:int}")]
    public async Task<IActionResult> DeleteRequirement(string id, int reqId)
    {
        var lead = await FindLeadAsync(id);
        if (lead == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Lead '{id}' not found."));
        }

        var entry = lead.Requirements.FirstOrDefault(r => r.Id == reqId);
        if (entry == null)
        {
            return NotFound(AppApiErrorResponse.Fail($"Requirement with ID '{reqId}' not found."));
        }

        lead.Requirements.Remove(entry);
        await _db.SaveChangesAsync();

        return Ok(AppApiResponse.Ok("Requirement deleted successfully."));
    }
}

