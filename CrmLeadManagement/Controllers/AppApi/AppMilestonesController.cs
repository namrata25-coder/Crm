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
/// REST API endpoints for Milestone Management and Dependency Tracking under /api/v1/app/milestones.
/// </summary>
[ApiController]
[Route("api/v1/app/milestones")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppMilestonesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppMilestonesController> _logger;

    public AppMilestonesController(AppDbContext db, ILogger<AppMilestonesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string NextMilestoneCode()
    {
        var codes = _db.WorkMilestones.Select(m => m.MilestoneCode).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => { var n = c!.StartsWith("MLS-", StringComparison.OrdinalIgnoreCase) ? c.Substring(4) : c; return int.TryParse(n, out var v) ? v : 1000; })
            .DefaultIfEmpty(1000).Max();
        int next = Math.Max(max + 1, 1001);
        while (codes.Any(c => string.Equals(c, $"MLS-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"MLS-{next}";
    }

    /// <summary>
    /// GET /api/v1/app/milestones
    /// Retrieves milestones with pagination, dependency status, and filtering.
    /// Supports: page, pageSize, projectId, status, search.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMilestones(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? projectId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Milestone", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view milestones."));
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var query = _db.WorkMilestones
                .Include(m => m.Project)
                .Include(m => m.Tasks)
                .AsQueryable();

            if (currentUser != null)
            {
                var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == currentUser.Role);
                if (role != null && string.Equals(role.DataAccess, "OnlyAssigned", StringComparison.OrdinalIgnoreCase))
                {
                    var uName = currentUser.Name ?? "";
                    var uEmail = currentUser.Email ?? "";
                    query = query.Where(m =>
                        (m.AssignedMembers != null && (m.AssignedMembers.Contains(uName) || m.AssignedMembers.Contains(uEmail))) ||
                        (m.Project != null && ((m.Project.ProjectManager != null && (m.Project.ProjectManager == uName || m.Project.ProjectManager == uEmail)) ||
                                               (m.Project.AssignedMembers != null && (m.Project.AssignedMembers.Contains(uName) || m.Project.AssignedMembers.Contains(uEmail))))));
                }
            }

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(m => m.ProjectId == projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(m => m.Status != null && m.Status.ToLower() == status.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(m =>
                    (m.MilestoneName != null && m.MilestoneName.ToLower().Contains(term)) ||
                    (m.MilestoneCode != null && m.MilestoneCode.ToLower().Contains(term)) ||
                    (m.Project != null && m.Project.ProjectName != null && m.Project.ProjectName.ToLower().Contains(term)) ||
                    (m.AssignedMembers != null && m.AssignedMembers.ToLower().Contains(term)));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var milestones = await query
                .OrderBy(m => m.ProjectId)
                .ThenBy(m => m.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var projectIds = milestones.Select(m => m.ProjectId).Distinct().ToList();
            var allProjectMilestones = await _db.WorkMilestones
                .Where(m => projectIds.Contains(m.ProjectId))
                .OrderBy(m => m.Id)
                .ToListAsync();

            var invoices = await _db.WorkInvoices
                .Include(i => i.Payments)
                .Where(i => projectIds.Contains(i.ProjectId))
                .ToListAsync();

            var projectGroups = allProjectMilestones.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

            var items = milestones.Select(m =>
            {
                var projMilestones = projectGroups.ContainsKey(m.ProjectId) ? projectGroups[m.ProjectId] : new List<WorkMilestone>();
                var projInvoices = invoices.Where(i => i.ProjectId == m.ProjectId).ToList();
                var depInfo = WorkDependencyHelper.EvaluateMilestone(m, projMilestones, projInvoices);
                var inv = projInvoices.FirstOrDefault(i => i.MilestoneId == m.Id);

                return new AppMilestoneItemDto
                {
                    Id = m.Id,
                    ProjectId = m.ProjectId,
                    ProjectName = m.Project?.ProjectName ?? "",
                    ProjectCode = m.Project?.ProjectCode ?? "",
                    MilestoneCode = m.MilestoneCode,
                    MilestoneName = m.MilestoneName,
                    Description = m.Description,
                    StartDate = m.StartDate,
                    DueDate = m.DueDate,
                    Amount = m.Amount,
                    CompletedAmount = m.CompletedAmount,
                    PendingAmount = m.PendingAmount,
                    ProgressPercent = m.ProgressPercent,
                    AssignedMembers = m.AssignedMembers,
                    Priority = m.Priority,
                    Status = m.Status,
                    Notes = m.Notes,
                    TasksCount = m.Tasks.Count,
                    IsLocked = depInfo.IsLocked,
                    LockReason = depInfo.LockReason,
                    DependencyType = depInfo.DependencyType,
                    SequenceIndex = depInfo.SequenceIndex,
                    InvoiceNumber = inv?.InvoiceNumber,
                    InvoiceStatus = inv?.PaymentStatus
                };
            }).ToList();

            var result = new PaginatedResult<AppMilestoneItemDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = items
            };

            return Ok(AppApiResponse<PaginatedResult<AppMilestoneItemDto>>.Ok(result, "Milestones retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve milestones.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving milestones."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/milestones/project-dependency/{projectId}
    /// Retrieves sequential milestone dependencies, lock status, and invoices for a specific project.
    /// IMPORTANT: Declared BEFORE milestones/{id} to prevent routing collisions.
    /// </summary>
    [HttpGet("project-dependency/{projectId:int}")]
    public async Task<IActionResult> GetProjectMilestoneDependency(int projectId)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Milestone", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view milestone dependencies."));
            }

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Project with ID {projectId} not found."));
            }

            var milestones = project.Milestones.OrderBy(m => m.Id).ToList();
            var invoices = project.Invoices.ToList();

            var evaluatedMilestones = milestones.Select(m =>
            {
                var dep = WorkDependencyHelper.EvaluateMilestone(m, milestones, invoices);
                var inv = invoices.FirstOrDefault(i => i.MilestoneId == m.Id);
                return new
                {
                    id = m.Id,
                    milestoneCode = m.MilestoneCode,
                    milestoneName = m.MilestoneName,
                    status = m.Status,
                    amount = m.Amount,
                    completedAmount = m.CompletedAmount,
                    progressPercent = m.ProgressPercent,
                    startDate = m.StartDate,
                    dueDate = m.DueDate,
                    assignedMembers = m.AssignedMembers,
                    sequenceIndex = dep.SequenceIndex,
                    isLocked = dep.IsLocked,
                    lockReason = dep.LockReason,
                    dependencyType = dep.DependencyType,
                    invoice = inv != null ? new
                    {
                        id = inv.Id,
                        invoiceNumber = inv.InvoiceNumber,
                        paymentStatus = inv.PaymentStatus,
                        grandTotal = inv.GrandTotal,
                        paidAmount = inv.EffectivePaidAmount,
                        isPaid = string.Equals(inv.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) || (inv.GrandTotal > 0 && inv.EffectivePaidAmount >= inv.GrandTotal)
                    } : null
                };
            }).ToList();

            var dummyNext = new WorkMilestone { Id = 0, ProjectId = projectId };
            var nextDep = WorkDependencyHelper.EvaluateMilestone(dummyNext, milestones, invoices);

            var responseData = new
            {
                project = new
                {
                    id = project.Id,
                    projectCode = project.ProjectCode,
                    projectName = project.ProjectName,
                    clientName = project.ClientName,
                    clientCompany = project.ClientCompany,
                    budget = project.Budget,
                    status = project.Status,
                    totalMilestones = milestones.Count,
                    completedMilestones = milestones.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                },
                milestones = evaluatedMilestones,
                nextMilestoneDependency = new
                {
                    isLocked = nextDep.IsLocked,
                    lockReason = nextDep.LockReason,
                    sequenceIndex = nextDep.SequenceIndex,
                    dependencyType = nextDep.DependencyType,
                    prerequisiteMilestoneCode = nextDep.PrerequisiteMilestoneCode,
                    prerequisiteMilestoneName = nextDep.PrerequisiteMilestoneName
                }
            };

            return Ok(AppApiResponse<object>.Ok(responseData, $"Project dependencies for project {projectId} retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve milestone dependencies for project ID {ProjectId}.", projectId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving milestone dependencies."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/milestones/{id}
    /// Retrieves a single milestone by ID with dependency evaluation and tasks.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMilestoneById(int id)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Milestone", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view milestones."));
            }

            var milestone = await _db.WorkMilestones
                .Include(m => m.Project)
                .Include(m => m.Tasks)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (milestone == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Milestone with ID {id} not found."));
            }

            var projectMilestones = await _db.WorkMilestones
                .Where(m => m.ProjectId == milestone.ProjectId)
                .OrderBy(m => m.Id)
                .ToListAsync();

            var projectInvoices = await _db.WorkInvoices
                .Include(i => i.Payments)
                .Where(i => i.ProjectId == milestone.ProjectId)
                .ToListAsync();

            var depInfo = WorkDependencyHelper.EvaluateMilestone(milestone, projectMilestones, projectInvoices);
            var inv = projectInvoices.FirstOrDefault(i => i.MilestoneId == milestone.Id);

            var responseData = new
            {
                id = milestone.Id,
                projectId = milestone.ProjectId,
                projectName = milestone.Project?.ProjectName ?? "",
                projectCode = milestone.Project?.ProjectCode ?? "",
                milestoneCode = milestone.MilestoneCode,
                milestoneName = milestone.MilestoneName,
                description = milestone.Description,
                startDate = milestone.StartDate,
                dueDate = milestone.DueDate,
                amount = milestone.Amount,
                completedAmount = milestone.CompletedAmount,
                pendingAmount = milestone.PendingAmount,
                progressPercent = milestone.ProgressPercent,
                assignedMembers = milestone.AssignedMembers,
                priority = milestone.Priority,
                status = milestone.Status,
                notes = milestone.Notes,
                tasks = milestone.Tasks,
                isLocked = depInfo.IsLocked,
                lockReason = depInfo.LockReason,
                sequenceIndex = depInfo.SequenceIndex,
                dependencyType = depInfo.DependencyType,
                prerequisiteMilestoneCode = depInfo.PrerequisiteMilestoneCode,
                prerequisiteMilestoneName = depInfo.PrerequisiteMilestoneName,
                invoice = inv != null ? new
                {
                    id = inv.Id,
                    invoiceNumber = inv.InvoiceNumber,
                    paymentStatus = inv.PaymentStatus,
                    grandTotal = inv.GrandTotal,
                    paidAmount = inv.EffectivePaidAmount
                } : null
            };

            return Ok(AppApiResponse<object>.Ok(responseData, "Milestone retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve milestone ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving the milestone."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/milestones
    /// Creates a milestone associated with a project, validating project existence and dependencies.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateMilestone([FromBody] AppMilestoneInputDto input)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Milestone", "Create"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to create milestones."));
            }

            if (input == null || string.IsNullOrWhiteSpace(input.MilestoneName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Milestone name is required."));
            }

            if (input.ProjectId <= 0)
            {
                return BadRequest(AppApiErrorResponse.Fail("A valid project ID is required."));
            }

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == input.ProjectId);

            if (project == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Project with ID {input.ProjectId} not found."));
            }

            if (input.Amount < 0)
            {
                return BadRequest(AppApiErrorResponse.Fail("Milestone amount cannot be negative."));
            }

            var dummyNext = new WorkMilestone { Id = 0, ProjectId = input.ProjectId };
            var depInfo = WorkDependencyHelper.EvaluateMilestone(dummyNext, project.Milestones, project.Invoices);

            var requestedStatus = string.IsNullOrWhiteSpace(input.Status) ? "Pending" : input.Status.Trim();
            if (depInfo.IsLocked && (string.Equals(requestedStatus, "In Progress", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(requestedStatus, "Completed", StringComparison.OrdinalIgnoreCase)))
            {
                requestedStatus = "Pending";
            }

            var milestone = new WorkMilestone
            {
                ProjectId = input.ProjectId,
                MilestoneCode = NextMilestoneCode(),
                MilestoneName = input.MilestoneName.Trim(),
                Description = input.Description?.Trim() ?? "",
                StartDate = input.StartDate?.Trim() ?? "",
                DueDate = input.DueDate?.Trim() ?? "",
                Amount = input.Amount,
                CompletedAmount = depInfo.IsLocked ? 0 : Math.Max(0, input.CompletedAmount),
                AssignedMembers = input.AssignedMembers?.Trim() ?? "",
                Priority = string.IsNullOrWhiteSpace(input.Priority) ? "Medium" : input.Priority.Trim(),
                Status = requestedStatus,
                Notes = input.Notes?.Trim() ?? ""
            };

            _db.WorkMilestones.Add(milestone);
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created,
                AppApiResponse<WorkMilestone>.Ok(milestone, $"Milestone '{milestone.MilestoneName}' created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create milestone.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating the milestone."));
        }
    }

    /// <summary>
    /// PUT /api/v1/app/milestones/{id}
    /// Updates an existing milestone, preventing invalid dependency transitions.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateMilestone(int id, [FromBody] AppMilestoneInputDto input)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Milestone", "Edit"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to edit milestones."));
            }

            var milestone = await _db.WorkMilestones.FindAsync(id);
            if (milestone == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Milestone with ID {id} not found."));
            }

            if (input == null || string.IsNullOrWhiteSpace(input.MilestoneName))
            {
                return BadRequest(AppApiErrorResponse.Fail("Milestone name is required."));
            }

            var targetProjectId = input.ProjectId > 0 ? input.ProjectId : milestone.ProjectId;
            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == targetProjectId);

            if (project == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Project with ID {targetProjectId} not found."));
            }

            milestone.ProjectId = targetProjectId;

            var depInfo = WorkDependencyHelper.EvaluateMilestone(milestone, project.Milestones, project.Invoices);
            var newStatus = string.IsNullOrWhiteSpace(input.Status) ? milestone.Status : input.Status.Trim();

            // Enforce sequential milestone dependency locks
            if (depInfo.IsLocked && (string.Equals(newStatus, "In Progress", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(newStatus, "Completed", StringComparison.OrdinalIgnoreCase)))
            {
                return BadRequest(AppApiErrorResponse.Fail(
                    $"Cannot set milestone to '{newStatus}'. {depInfo.LockReason}",
                    new { isLocked = true, lockReason = depInfo.LockReason }));
            }

            milestone.MilestoneName = input.MilestoneName.Trim();
            milestone.Description = input.Description?.Trim() ?? "";
            milestone.StartDate = input.StartDate?.Trim() ?? "";
            milestone.DueDate = input.DueDate?.Trim() ?? "";
            milestone.Amount = Math.Max(0, input.Amount);
            milestone.CompletedAmount = depInfo.IsLocked ? 0 : Math.Max(0, input.CompletedAmount);
            milestone.AssignedMembers = input.AssignedMembers?.Trim() ?? "";
            milestone.Priority = string.IsNullOrWhiteSpace(input.Priority) ? milestone.Priority : input.Priority.Trim();
            milestone.Status = newStatus;
            milestone.Notes = input.Notes?.Trim() ?? "";

            await _db.SaveChangesAsync();

            return Ok(AppApiResponse<WorkMilestone>.Ok(milestone, $"Milestone '{milestone.MilestoneName}' updated successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update milestone ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while updating the milestone."));
        }
    }

    /// <summary>
    /// DELETE /api/v1/app/milestones/{id}
    /// Safely deletes a milestone if not associated with active or paid invoices.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMilestone(int id)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Milestone", "Delete"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to delete milestones."));
            }

            var milestone = await _db.WorkMilestones
                .Include(m => m.Tasks)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (milestone == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Milestone with ID {id} not found."));
            }

            // Check if milestone has associated invoices
            var hasInvoices = await _db.WorkInvoices.AnyAsync(i => i.MilestoneId == id);
            if (hasInvoices)
            {
                return Conflict(AppApiErrorResponse.Fail(
                    $"Milestone '{milestone.MilestoneName}' cannot be deleted because one or more invoices are linked to it."));
            }

            _db.WorkMilestones.Remove(milestone);
            await _db.SaveChangesAsync();

            return Ok(AppApiResponse.Ok($"Milestone '{milestone.MilestoneName}' deleted successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete milestone ID {Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while deleting the milestone."));
        }
    }
}
