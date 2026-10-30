using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Projects module.
/// Base route: /api/projects
/// </summary>
[ApiController]
[Route("api/projects")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class ProjectsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProjectsApiController> _logger;

    public ProjectsApiController(
        AppDbContext db,
        ILogger<ProjectsApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string NextProjectCode()
    {
        var codes = _db.WorkProjects.Select(p => p.ProjectCode).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => { var n = c!.StartsWith("PRJ-", StringComparison.OrdinalIgnoreCase) ? c.Substring(4) : c; return int.TryParse(n, out var v) ? v : 1000; })
            .DefaultIfEmpty(1000).Max();
        int next = Math.Max(max + 1, 1001);
        while (codes.Any(c => string.Equals(c, $"PRJ-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"PRJ-{next}";
    }

    // GET /api/projects
    // GET /api/projects?status=Active&projectType=Direct+Project&search=web
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkProject>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? projectType,
        [FromQuery] string? client,
        [FromQuery] string? clientId,
        [FromQuery] string? search)
    {
        try
        {
            var query = _db.WorkProjects
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.Tasks)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(p => p.Status == status);

            if (!string.IsNullOrWhiteSpace(projectType))
                query = query.Where(p => p.ProjectType == projectType);

            if (!string.IsNullOrWhiteSpace(client))
            {
                var cTerm = client.Trim().ToLower();
                query = query.Where(p =>
                    (p.ClientName != null && p.ClientName.ToLower() == cTerm) ||
                    (p.ClientCompany != null && p.ClientCompany.ToLower() == cTerm) ||
                    (p.ClientName != null && p.ClientName.ToLower().Contains(cTerm)) ||
                    (p.ClientCompany != null && p.ClientCompany.ToLower().Contains(cTerm)));
            }

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var idTerm = clientId.Trim();
                var matchedLead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == idTerm || l.Id.ToString() == idTerm);
                if (matchedLead != null)
                {
                    var lName = (matchedLead.LeadName ?? "").ToLower();
                    var cName = (matchedLead.CompanyName ?? "").ToLower();
                    query = query.Where(p =>
                        (p.ClientName != null && (p.ClientName.ToLower() == lName || p.ClientName.ToLower() == cName)) ||
                        (p.ClientCompany != null && (p.ClientCompany.ToLower() == cName || p.ClientCompany.ToLower() == lName)));
                }
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(p =>
                    (p.ProjectName != null && p.ProjectName.ToLower().Contains(term)) ||
                    (p.ProjectCode != null && p.ProjectCode.ToLower().Contains(term)) ||
                    (p.ClientName != null && p.ClientName.ToLower().Contains(term)) ||
                    (p.ClientCompany != null && p.ClientCompany.ToLower().Contains(term)) ||
                    (p.ProjectManager != null && p.ProjectManager.ToLower().Contains(term)));
            }

            var projects = await query.OrderByDescending(p => p.Id).ToListAsync();
            return Ok(ProjectAccessPolicy.FilterAccessible(_db, User, projects));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/projects failed");
            return StatusCode(500, new { message = "Failed to retrieve projects", error = ex.Message });
        }
    }

    // GET /api/projects/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkProject>> GetById(int id)
    {
        try
        {
            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.Tasks)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project is null || !ProjectAccessPolicy.CanAccess(_db, User, project))
                return NotFound(new { message = $"Project with ID {id} not found." });

            return Ok(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/projects/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to retrieve project", error = ex.Message });
        }
    }

    // POST /api/projects
    [HttpPost]
    public async Task<ActionResult<WorkProject>> Create([FromBody] ProjectDto input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input.ProjectName))
                return BadRequest(new { message = "ProjectName is required." });

            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            var isAdmin = PermissionChecker.IsAdminOrManager(User, currentUser);
            if (!isAdmin && currentUser is null)
                return Forbid();

            var project = new WorkProject
            {
                ProjectCode     = NextProjectCode(),
                ProjectName     = input.ProjectName?.Trim() ?? "New Project",
                ClientName      = input.ClientName?.Trim() ?? "",
                ClientCompany   = input.ClientCompany?.Trim() ?? "",
                ProjectType     = string.IsNullOrWhiteSpace(input.ProjectType) ? "Direct Project" : input.ProjectType,
                Description     = input.Description?.Trim() ?? "",
                StartDate       = input.StartDate?.Trim() ?? "",
                Deadline        = input.Deadline?.Trim() ?? "",
                ProjectManager  = isAdmin ? input.ProjectManager?.Trim() ?? "" : currentUser!.Name?.Trim() ?? "",
                AssignedMembers = input.AssignedMembers?.Trim() ?? "",
                Budget          = Math.Max(0, input.Budget),
                Priority        = string.IsNullOrWhiteSpace(input.Priority) ? "Medium" : input.Priority,
                Status          = string.IsNullOrWhiteSpace(input.Status) ? "Active" : input.Status,
                Notes           = input.Notes?.Trim() ?? "",
                CreatedDate     = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            _db.WorkProjects.Add(project);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/projects failed");
            return StatusCode(500, new { message = "Failed to create project", error = ex.Message });
        }
    }

    // PUT /api/projects/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkProject>> Update(int id, [FromBody] ProjectDto input)
    {
        try
        {
            var project = await _db.WorkProjects.FindAsync(id);
            if (project is null || !ProjectAccessPolicy.CanAccess(_db, User, project))
                return NotFound(new { message = $"Project with ID {id} not found." });

            if (string.IsNullOrWhiteSpace(input.ProjectName))
                return BadRequest(new { message = "ProjectName is required." });

            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            var isAdmin = PermissionChecker.IsAdminOrManager(User, currentUser);
            project.ProjectName     = input.ProjectName?.Trim() ?? project.ProjectName;
            project.ClientName      = input.ClientName?.Trim() ?? "";
            project.ClientCompany   = input.ClientCompany?.Trim() ?? "";
            project.ProjectType     = string.IsNullOrWhiteSpace(input.ProjectType) ? project.ProjectType : input.ProjectType;
            project.Description     = input.Description?.Trim() ?? "";
            project.StartDate       = input.StartDate?.Trim() ?? "";
            project.Deadline        = input.Deadline?.Trim() ?? "";
            project.ProjectManager  = isAdmin ? input.ProjectManager?.Trim() ?? "" : currentUser!.Name?.Trim() ?? "";
            project.AssignedMembers = input.AssignedMembers?.Trim() ?? "";
            project.Budget          = Math.Max(0, input.Budget);
            project.Priority        = string.IsNullOrWhiteSpace(input.Priority) ? project.Priority : input.Priority;
            project.Status          = string.IsNullOrWhiteSpace(input.Status) ? project.Status : input.Status;
            project.Notes           = input.Notes?.Trim() ?? "";

            await _db.SaveChangesAsync();

            return Ok(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /api/projects/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to update project", error = ex.Message });
        }
    }

    // DELETE /api/projects/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project is null || !ProjectAccessPolicy.CanAccess(_db, User, project))
                return NotFound(new { message = $"Project with ID {id} not found." });

            _db.WorkProjects.Remove(project);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Project deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /api/projects/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to delete project", error = ex.Message });
        }
    }
}

public class ProjectDto
{
    public int Id { get; set; }
    public string? ProjectCode { get; set; }
    public string ProjectName { get; set; } = "";
    public string? ClientName { get; set; } = "";
    public string? ClientCompany { get; set; } = "";
    public string? ProjectType { get; set; } = "Direct Project";
    public string? Description { get; set; } = "";
    public string? StartDate { get; set; } = "";
    public string? Deadline { get; set; } = "";
    public string? ProjectManager { get; set; } = "";
    public string? AssignedMembers { get; set; } = "";
    public decimal Budget { get; set; }
    public string? Priority { get; set; } = "Medium";
    public string? Status { get; set; } = "Active";
    public string? Notes { get; set; } = "";
}
