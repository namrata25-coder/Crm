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
