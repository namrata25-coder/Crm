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
/// REST API endpoints for Project Management under /api/v1/app/projects.
/// </summary>
[ApiController]
[Route("api/v1/app/projects")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppProjectsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppProjectsController> _logger;

    public AppProjectsController(AppDbContext db, ILogger<AppProjectsController> logger)
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

    /// <summary>
    /// GET /api/v1/app/projects
    /// Retrieves projects with pagination, searching, and filtering.
    /// Supports: page, pageSize, status, projectType, priority, search.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProjects(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? projectType = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? search = null)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view projects."));
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var query = _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Tasks)
                .AsQueryable();

            // Role-based data access restriction
            if (currentUser != null)
            {
                var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == currentUser.Role);
                if (role != null && string.Equals(role.DataAccess, "OnlyAssigned", StringComparison.OrdinalIgnoreCase))
                {
                    var uName = currentUser.Name ?? "";
                    var uEmail = currentUser.Email ?? "";
                    query = query.Where(p =>
                        (p.ProjectManager != null && (p.ProjectManager == uName || p.ProjectManager == uEmail)) ||
                        (p.AssignedMembers != null && (p.AssignedMembers.Contains(uName) || p.AssignedMembers.Contains(uEmail))));
                }
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status != null && p.Status.ToLower() == status.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(projectType))
            {
                query = query.Where(p => p.ProjectType != null && p.ProjectType.ToLower() == projectType.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(priority))
            {
                query = query.Where(p => p.Priority != null && p.Priority.ToLower() == priority.Trim().ToLower());
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

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var items = await query
                .OrderByDescending(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = new PaginatedResult<WorkProject>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = items
            };

            return Ok(AppApiResponse<PaginatedResult<WorkProject>>.Ok(result, "Projects retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve projects.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving projects."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/projects/{id}
    /// Retrieves a single project by ID with its milestones and tasks.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetProjectById(int id)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view projects."));
            }

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.Tasks)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Project with ID {id} not found."));
            }

            // Role-based data access restriction
            if (currentUser != null)
            {
                var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == currentUser.Role);
                if (role != null && string.Equals(role.DataAccess, "OnlyAssigned", StringComparison.OrdinalIgnoreCase))
                {
                    var uName = currentUser.Name ?? "";
                    var uEmail = currentUser.Email ?? "";
                    bool isAssigned = (project.ProjectManager != null && (project.ProjectManager == uName || project.ProjectManager == uEmail)) ||
                                      (project.AssignedMembers != null && (project.AssignedMembers.Contains(uName) || project.AssignedMembers.Contains(uEmail)));
                    if (!isAssigned)
                    {
                        return StatusCode(StatusCodes.Status403Forbidden,
                            AppApiErrorResponse.Fail("You are not authorized to access this project."));
                    }
                }
            }

            return Ok(AppApiResponse<WorkProject>.Ok(project, "Project retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve project ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving the project."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/projects
    /// Creates a new project with auto-generated project code and validation.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateProject([FromBody] AppProjectInputDto input)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "Create"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to create projects."));
            }

            if (input == null || string.IsNullOrWhiteSpace(input.ProjectName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Project name is required."));
            }

            if (input.Budget < 0)
            {
                return BadRequest(AppApiErrorResponse.Fail("Project budget cannot be negative."));
            }

            var project = new WorkProject
            {
                ProjectCode = NextProjectCode(),
                ProjectName = input.ProjectName.Trim(),
                ClientName = input.ClientName?.Trim() ?? "",
                ClientCompany = input.ClientCompany?.Trim() ?? "",
                ProjectType = string.IsNullOrWhiteSpace(input.ProjectType) ? "Direct Project" : input.ProjectType.Trim(),
                Description = input.Description?.Trim() ?? "",
                StartDate = input.StartDate?.Trim() ?? "",
                Deadline = input.Deadline?.Trim() ?? "",
                ProjectManager = input.ProjectManager?.Trim() ?? "",
                AssignedMembers = input.AssignedMembers?.Trim() ?? "",
                Budget = input.Budget,
                Priority = string.IsNullOrWhiteSpace(input.Priority) ? "Medium" : input.Priority.Trim(),
                Status = string.IsNullOrWhiteSpace(input.Status) ? "Active" : input.Status.Trim(),
                Notes = input.Notes?.Trim() ?? "",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            _db.WorkProjects.Add(project);
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<WorkProject>.Ok(project, $"Project '{project.ProjectName}' created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating the project."));
        }
    }

    /// <summary>
    /// PUT /api/v1/app/projects/{id}
    /// Updates an existing project.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateProject(int id, [FromBody] AppProjectInputDto input)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "Edit"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to edit projects."));
            }

            var project = await _db.WorkProjects.FindAsync(id);
            if (project == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Project with ID {id} not found."));
            }

            if (input == null || string.IsNullOrWhiteSpace(input.ProjectName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Project name is required."));
            }

            if (input.Budget < 0)
            {
                return BadRequest(AppApiErrorResponse.Fail("Project budget cannot be negative."));
            }

            project.ProjectName = input.ProjectName.Trim();
            project.ClientName = input.ClientName?.Trim() ?? "";
            project.ClientCompany = input.ClientCompany?.Trim() ?? "";
            project.ProjectType = string.IsNullOrWhiteSpace(input.ProjectType) ? project.ProjectType : input.ProjectType.Trim();
            project.Description = input.Description?.Trim() ?? "";
            project.StartDate = input.StartDate?.Trim() ?? "";
            project.Deadline = input.Deadline?.Trim() ?? "";
            project.ProjectManager = input.ProjectManager?.Trim() ?? "";
            project.AssignedMembers = input.AssignedMembers?.Trim() ?? "";
            project.Budget = input.Budget;
            project.Priority = string.IsNullOrWhiteSpace(input.Priority) ? project.Priority : input.Priority.Trim();
            project.Status = string.IsNullOrWhiteSpace(input.Status) ? project.Status : input.Status.Trim();
            project.Notes = input.Notes?.Trim() ?? "";

            await _db.SaveChangesAsync();

            return Ok(AppApiResponse<WorkProject>.Ok(project, $"Project '{project.ProjectName}' updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update project ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while updating the project."));
        }
    }

    /// <summary>
    /// DELETE /api/v1/app/projects/{id}
    /// Safely deletes a project and cascades related tasks and milestones.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProject(int id)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "Delete"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to delete projects."));
            }

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Project with ID {id} not found."));
            }

            // Check if project has paid invoices
            var hasPaidInvoices = project.Invoices.Any(i => string.Equals(i.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) || i.PaidAmount > 0);
            if (hasPaidInvoices)
            {
                return Conflict(AppApiErrorResponse.Fail(
                    $"Project '{project.ProjectName}' has paid invoices associated with it. To preserve financial integrity, projects with paid invoices cannot be deleted."));
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.WorkProjects.Remove(project);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(AppApiResponse.Ok($"Project '{project.ProjectName}' deleted successfully."));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction failed while deleting project ID {Id}.", id);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete project ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while deleting the project."));
        }
    }
}
