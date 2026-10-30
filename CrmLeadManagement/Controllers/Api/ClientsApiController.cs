using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Clients / Customers lookup.
/// Base route: /api/clients
/// </summary>
[ApiController]
[Route("api/clients")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class ClientsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ClientsApiController> _logger;

    public ClientsApiController(AppDbContext db, ILogger<ClientsApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET /api/clients
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientLookupDto>>> GetAll([FromQuery] string? search)
    {
        try
        {
            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            var projects = await _db.WorkProjects.AsNoTracking().ToListAsync();

            var clientMap = new Dictionary<string, ClientLookupDto>(StringComparer.OrdinalIgnoreCase);

            // Populate from Leads
            foreach (var lead in leads)
            {
                var clientName = lead.LeadName?.Trim() ?? "";
                var companyName = lead.CompanyName?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(clientName) && string.IsNullOrWhiteSpace(companyName)) continue;

                var key = !string.IsNullOrWhiteSpace(lead.LeadId)
                    ? lead.LeadId.Trim()
                    : (!string.IsNullOrWhiteSpace(clientName) ? clientName : companyName);

                if (!clientMap.ContainsKey(key))
                {
                    clientMap[key] = new ClientLookupDto
                    {
                        ClientId = !string.IsNullOrWhiteSpace(lead.LeadId) ? lead.LeadId : lead.Id.ToString(),
                        LeadId = lead.LeadId ?? "",
                        ClientName = clientName,
                        CompanyName = companyName,
                        Email = lead.Email ?? "",
                        MobileNumber = lead.MobileNumber ?? "",
                        ProjectsCount = projects.Count(p => 
                            (!string.IsNullOrEmpty(clientName) && string.Equals(p.ClientName, clientName, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(companyName) && string.Equals(p.ClientCompany, companyName, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(companyName) && string.Equals(p.ClientName, companyName, StringComparison.OrdinalIgnoreCase)))
                    };
                }
            }

            // Populate from Projects (if any client was created directly in Work module)
            foreach (var p in projects)
            {
                var clientName = p.ClientName?.Trim() ?? "";
                var companyName = p.ClientCompany?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(clientName) && string.IsNullOrWhiteSpace(companyName)) continue;

                var key = !string.IsNullOrWhiteSpace(clientName) ? clientName : companyName;
                if (!clientMap.ContainsKey(key))
                {
                    clientMap[key] = new ClientLookupDto
                    {
                        ClientId = !string.IsNullOrWhiteSpace(p.ProjectCode) ? p.ProjectCode : $"CLIENT-{p.Id}",
                        LeadId = "",
                        ClientName = clientName,
                        CompanyName = companyName,
                        Email = "",
                        MobileNumber = "",
                        ProjectsCount = projects.Count(x => 
                            (!string.IsNullOrEmpty(clientName) && string.Equals(x.ClientName, clientName, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(companyName) && string.Equals(x.ClientCompany, companyName, StringComparison.OrdinalIgnoreCase)))
                    };
                }
            }

            var list = clientMap.Values.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                list = list.Where(c => 
                    c.ClientName.ToLower().Contains(term) ||
                    c.CompanyName.ToLower().Contains(term) ||
                    c.Email.ToLower().Contains(term) ||
                    c.MobileNumber.ToLower().Contains(term));
            }

            return Ok(list
                .OrderBy(c => string.IsNullOrWhiteSpace(c.ClientName) ? 1 : 0)
                .ThenBy(c => c.ClientName)
                .ThenBy(c => c.CompanyName)
                .ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/clients failed");
            return StatusCode(500, new { message = "Failed to retrieve clients", error = ex.Message });
        }
    }

    // GET /api/clients/{clientName}/projects
    [HttpGet("{clientName}/projects")]
    public async Task<ActionResult<IEnumerable<WorkProject>>> GetClientProjects(string clientName)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(clientName).Trim();
            var projects = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Invoices)
                .Where(p => 
                    string.Equals(p.ClientName, decoded, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.ClientCompany, decoded, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Id)
                .ToListAsync();

            return Ok(projects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/clients/{ClientName}/projects failed", clientName);
            return StatusCode(500, new { message = "Failed to retrieve client projects", error = ex.Message });
        }
    }
}

public class ClientLookupDto
{
    public string ClientId { get; set; } = "";
    public string LeadId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Email { get; set; } = "";
    public string MobileNumber { get; set; } = "";
    public int ProjectsCount { get; set; }
}
