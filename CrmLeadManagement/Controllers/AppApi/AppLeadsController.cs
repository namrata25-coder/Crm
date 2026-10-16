using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos;
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
[Authorize(AuthenticationSchemes = "Bearer")]
public class AppLeadsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AppLeadsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets all leads formatted for app consumption.
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
}
