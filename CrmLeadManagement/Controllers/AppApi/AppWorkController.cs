using System.Security.Claims;
using CrmLeadManagement.Controllers.Api;
using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// REST API endpoints for Work Summary, Work Statistics, Work Reports, and Work Tasks.
/// Base Routes: /api/v1/app/work and /api/app/work
/// </summary>
[ApiController]
[Route("api/v1/app/work")]
[Route("api/app/work")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppWorkController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppWorkController> _logger;

    public AppWorkController(AppDbContext db, ILogger<AppWorkController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Helper to resolve the effective assigned user based on user role and query param.
    /// </summary>
    private async Task<string?> ResolveAssignedUser(UserRoleEntry? currentUser, string? requestedAssignedUser)
    {
        if (!string.IsNullOrWhiteSpace(requestedAssignedUser))
        {
            return requestedAssignedUser.Trim();
        }

        if (currentUser != null)
        {
            var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == currentUser.Role);
            if (role != null && string.Equals(role.DataAccess, "OnlyAssigned", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(currentUser.Name) ? currentUser.Name : currentUser.Email;
            }
        }

        return null;
    }

    /// <summary>
    /// GET /api/v1/app/work/summary (or /api/v1/app/work)
    /// Returns aggregated counts and financial metrics calculated from real database records.
    /// Supports filtering by assignedUser, projectId, and date range (from, to).
    /// </summary>
    [HttpGet]
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? assignedUser = null,
        [FromQuery] int? projectId = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "View") &&
                                       !PermissionChecker.CanPerformAction(_db, User, "Task", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view work items."));
            }

            var effectiveUser = await ResolveAssignedUser(currentUser, assignedUser);

            var projectsQuery = _db.WorkProjects.AsQueryable();
            var milestonesQuery = _db.WorkMilestones.Include(m => m.Project).AsQueryable();
            var tasksQuery = _db.WorkTasks.Include(t => t.Project).AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                projectsQuery = projectsQuery.Where(p => p.Id == projectId.Value);
                milestonesQuery = milestonesQuery.Where(m => m.ProjectId == projectId.Value);
                tasksQuery = tasksQuery.Where(t => t.ProjectId == projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(effectiveUser))
            {
                var u = effectiveUser.Trim().ToLower();
                projectsQuery = projectsQuery.Where(p =>
                    (p.ProjectManager != null && p.ProjectManager.ToLower().Contains(u)) ||
                    (p.AssignedMembers != null && p.AssignedMembers.ToLower().Contains(u)));

                milestonesQuery = milestonesQuery.Where(m =>
                    (m.AssignedMembers != null && m.AssignedMembers.ToLower().Contains(u)) ||
                    (m.Project != null && m.Project.AssignedMembers != null && m.Project.AssignedMembers.ToLower().Contains(u)) ||
                    (m.Project != null && m.Project.ProjectManager != null && m.Project.ProjectManager.ToLower().Contains(u)));

                tasksQuery = tasksQuery.Where(t =>
                    (t.AssignedMember != null && t.AssignedMember.ToLower().Contains(u)) ||
                    (t.Project != null && t.Project.AssignedMembers != null && t.Project.AssignedMembers.ToLower().Contains(u)) ||
                    (t.Project != null && t.Project.ProjectManager != null && t.Project.ProjectManager.ToLower().Contains(u)));
            }

            // Project lightweight scalar projection to prevent LINQ SQL translation errors
            var projects = await projectsQuery.Select(p => new
            {
                p.Id,
                Status = p.Status ?? "Active",
                p.StartDate,
                p.CreatedDate,
                p.Deadline,
                p.Budget
            }).ToListAsync();

            var milestones = await milestonesQuery.Select(m => new
            {
                m.Id,
                m.ProjectId,
                Status = m.Status ?? "Pending",
                m.StartDate,
                m.DueDate,
                m.Amount,
                m.CompletedAmount
            }).ToListAsync();

            var tasks = await tasksQuery.Select(t => new
            {
                t.Id,
                t.ProjectId,
                Status = t.Status ?? "Pending",
                t.StartDate,
                t.DueDate,
                t.CreatedDate,
                t.AssignedMember,
                t.TaskAmount,
                t.CompletedAmount
            }).ToListAsync();

            // Apply date filters in memory (prevents string.Compare EF SQL translation errors)
            if (!string.IsNullOrWhiteSpace(from))
            {
                var f = from.Trim();
                projects = projects.Where(p =>
                    (!string.IsNullOrWhiteSpace(p.StartDate) && string.Compare(p.StartDate, f, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(p.CreatedDate) && string.Compare(p.CreatedDate, f, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();

                milestones = milestones.Where(m =>
                    !string.IsNullOrWhiteSpace(m.StartDate) && string.Compare(m.StartDate, f, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();

                tasks = tasks.Where(t =>
                    (!string.IsNullOrWhiteSpace(t.StartDate) && string.Compare(t.StartDate, f, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(t.CreatedDate) && string.Compare(t.CreatedDate, f, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                var targetTo = to.Trim();
                projects = projects.Where(p =>
                    !string.IsNullOrWhiteSpace(p.Deadline) && string.Compare(p.Deadline, targetTo, StringComparison.OrdinalIgnoreCase) <= 0
                ).ToList();

                milestones = milestones.Where(m =>
                    !string.IsNullOrWhiteSpace(m.DueDate) && string.Compare(m.DueDate, targetTo, StringComparison.OrdinalIgnoreCase) <= 0
                ).ToList();

                tasks = tasks.Where(tk =>
                    !string.IsNullOrWhiteSpace(tk.DueDate) && string.Compare(tk.DueDate, targetTo, StringComparison.OrdinalIgnoreCase) <= 0
                ).ToList();
            }

            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // Project metrics
            var totalProjects = projects.Count;
            var activeProjects = projects.Count(p => string.Equals(p.Status, "Active", StringComparison.OrdinalIgnoreCase));
            var completedProjects = projects.Count(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var onHoldProjects = projects.Count(p => string.Equals(p.Status, "On Hold", StringComparison.OrdinalIgnoreCase));
            var overdueProjects = projects.Count(p =>
                !string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(p.Deadline) && string.Compare(p.Deadline, todayStr, StringComparison.OrdinalIgnoreCase) < 0);
            var totalBudget = projects.Sum(p => p.Budget);

            // Milestone metrics
            var totalMilestones = milestones.Count;
            var pendingMilestones = milestones.Count(m => string.Equals(m.Status, "Pending", StringComparison.OrdinalIgnoreCase));
            var inProgressMilestones = milestones.Count(m => string.Equals(m.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            var completedMilestones = milestones.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var overdueMilestones = milestones.Count(m =>
                !string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(m.DueDate) && string.Compare(m.DueDate, todayStr, StringComparison.OrdinalIgnoreCase) < 0);
            var completedMilestonesValue = milestones.Sum(m => m.CompletedAmount);

            // Task metrics
            var totalTasks = tasks.Count;
            var assignedTasks = tasks.Count(t => !string.IsNullOrWhiteSpace(t.AssignedMember));
            var pendingTasks = tasks.Count(t => string.Equals(t.Status, "Pending", StringComparison.OrdinalIgnoreCase));
            var inProgressTasks = tasks.Count(t => string.Equals(t.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            var completedTasks = tasks.Count(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var overdueTasks = tasks.Count(t =>
                !string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(t.DueDate) && string.Compare(t.DueDate, todayStr, StringComparison.OrdinalIgnoreCase) < 0);
            var completedTasksValue = tasks.Sum(t => t.CompletedAmount);

            var completedValue = completedTasksValue > 0 ? completedTasksValue : completedMilestonesValue;
            var pendingValue = Math.Max(0, totalBudget - completedValue);

            var summary = new AppWorkSummaryDto
            {
                TotalProjects = totalProjects,
                ActiveProjects = activeProjects,
                CompletedProjects = completedProjects,
                OnHoldProjects = onHoldProjects,
                OverdueProjects = overdueProjects,

                TotalMilestones = totalMilestones,
                PendingMilestones = pendingMilestones,
                InProgressMilestones = inProgressMilestones,
                CompletedMilestones = completedMilestones,
                OverdueMilestones = overdueMilestones,

                TotalTasks = totalTasks,
                AssignedTasks = assignedTasks,
                PendingTasks = pendingTasks,
                InProgressTasks = inProgressTasks,
                CompletedTasks = completedTasks,
                OverdueTasks = overdueTasks,

                TotalBudget = totalBudget,
                CompletedValue = completedValue,
                PendingValue = pendingValue
            };

            return Ok(AppApiResponse<AppWorkSummaryDto>.Ok(summary, "Work summary retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve work summary.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving work summary."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/work/stats
    /// Detailed work statistics with distributions and completion percentages.
    /// Supports: assignedUser, projectId, from, to.
    /// </summary>
    [HttpGet("stats")]
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStats(
        [FromQuery] string? assignedUser = null,
        [FromQuery] int? projectId = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "View") &&
                                       !PermissionChecker.CanPerformAction(_db, User, "Task", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view work statistics."));
            }

            var effectiveUser = await ResolveAssignedUser(currentUser, assignedUser);

            var projectsQuery = _db.WorkProjects.AsQueryable();
            var milestonesQuery = _db.WorkMilestones.Include(m => m.Project).AsQueryable();
            var tasksQuery = _db.WorkTasks.Include(t => t.Project).AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                projectsQuery = projectsQuery.Where(p => p.Id == projectId.Value);
                milestonesQuery = milestonesQuery.Where(m => m.ProjectId == projectId.Value);
                tasksQuery = tasksQuery.Where(t => t.ProjectId == projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(effectiveUser))
            {
                var u = effectiveUser.Trim().ToLower();
                projectsQuery = projectsQuery.Where(p =>
                    (p.ProjectManager != null && p.ProjectManager.ToLower().Contains(u)) ||
                    (p.AssignedMembers != null && p.AssignedMembers.ToLower().Contains(u)));

                milestonesQuery = milestonesQuery.Where(m =>
                    (m.AssignedMembers != null && m.AssignedMembers.ToLower().Contains(u)) ||
                    (m.Project != null && m.Project.AssignedMembers != null && m.Project.AssignedMembers.ToLower().Contains(u)) ||
                    (m.Project != null && m.Project.ProjectManager != null && m.Project.ProjectManager.ToLower().Contains(u)));

                tasksQuery = tasksQuery.Where(t =>
                    (t.AssignedMember != null && t.AssignedMember.ToLower().Contains(u)) ||
                    (t.Project != null && t.Project.AssignedMembers != null && t.Project.AssignedMembers.ToLower().Contains(u)) ||
                    (t.Project != null && t.Project.ProjectManager != null && t.Project.ProjectManager.ToLower().Contains(u)));
            }

            // Scalar projections in SQL
            var projects = await projectsQuery.Select(p => new
            {
                p.Id,
                Status = string.IsNullOrWhiteSpace(p.Status) ? "Active" : p.Status.Trim(),
                p.Priority,
                p.StartDate,
                p.CreatedDate,
                p.Deadline,
                p.Budget
            }).ToListAsync();

            var milestones = await milestonesQuery.Select(m => new
            {
                m.Id,
                m.ProjectId,
                Status = string.IsNullOrWhiteSpace(m.Status) ? "Pending" : m.Status.Trim(),
                m.StartDate,
                m.DueDate,
                m.Amount,
                m.CompletedAmount,
                m.AssignedMembers
            }).ToListAsync();

            var tasks = await tasksQuery.Select(t => new
            {
                t.Id,
                t.ProjectId,
                Status = string.IsNullOrWhiteSpace(t.Status) ? "Pending" : t.Status.Trim(),
                Priority = string.IsNullOrWhiteSpace(t.Priority) ? "Medium" : t.Priority.Trim(),
                t.StartDate,
                t.DueDate,
                t.CreatedDate,
                t.AssignedMember,
                t.TaskAmount,
                t.CompletedAmount
            }).ToListAsync();

            // Date filtering in memory
            if (!string.IsNullOrWhiteSpace(from))
            {
                var f = from.Trim();
                projects = projects.Where(p =>
                    (!string.IsNullOrWhiteSpace(p.StartDate) && string.Compare(p.StartDate, f, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(p.CreatedDate) && string.Compare(p.CreatedDate, f, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();

                milestones = milestones.Where(m =>
                    !string.IsNullOrWhiteSpace(m.StartDate) && string.Compare(m.StartDate, f, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();

                tasks = tasks.Where(t =>
                    (!string.IsNullOrWhiteSpace(t.StartDate) && string.Compare(t.StartDate, f, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(t.CreatedDate) && string.Compare(t.CreatedDate, f, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                var targetTo = to.Trim();
                projects = projects.Where(p =>
                    !string.IsNullOrWhiteSpace(p.Deadline) && string.Compare(p.Deadline, targetTo, StringComparison.OrdinalIgnoreCase) <= 0
                ).ToList();

                milestones = milestones.Where(m =>
                    !string.IsNullOrWhiteSpace(m.DueDate) && string.Compare(m.DueDate, targetTo, StringComparison.OrdinalIgnoreCase) <= 0
                ).ToList();

                tasks = tasks.Where(tk =>
                    !string.IsNullOrWhiteSpace(tk.DueDate) && string.Compare(tk.DueDate, targetTo, StringComparison.OrdinalIgnoreCase) <= 0
                ).ToList();
            }

            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // Aggregated distributions in memory (guarantees no DB translation or duplicate key errors)
            var taskStatusDist = tasks
                .GroupBy(t => t.Status, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var taskPriorityDist = tasks
                .GroupBy(t => t.Priority, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var milestoneStatusDist = milestones
                .GroupBy(m => m.Status, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var projectStatusDist = projects
                .GroupBy(p => p.Status, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var totalTasks = tasks.Count;
            var completedTasks = tasks.Count(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var totalMilestones = milestones.Count;
            var completedMilestones = milestones.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var totalProjects = projects.Count;
            var activeProjects = projects.Count(p => string.Equals(p.Status, "Active", StringComparison.OrdinalIgnoreCase));
            var completedProjects = projects.Count(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase));

            var overdueTasks = tasks.Count(t =>
                !string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(t.DueDate) && string.Compare(t.DueDate, todayStr, StringComparison.OrdinalIgnoreCase) < 0);

            var overdueMilestones = milestones.Count(m =>
                !string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(m.DueDate) && string.Compare(m.DueDate, todayStr, StringComparison.OrdinalIgnoreCase) < 0);

            var overdueProjects = projects.Count(p =>
                !string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(p.Deadline) && string.Compare(p.Deadline, todayStr, StringComparison.OrdinalIgnoreCase) < 0);

            var totalBudget = projects.Sum(p => p.Budget);
            var completedTasksValue = tasks.Sum(t => t.CompletedAmount);
            var completedMilestonesValue = milestones.Sum(m => m.CompletedAmount);
            var completedValue = completedTasksValue > 0 ? completedTasksValue : completedMilestonesValue;
            var pendingValue = Math.Max(0, totalBudget - completedValue);

            double completionRate = totalTasks > 0
                ? Math.Round((completedTasks / (double)totalTasks) * 100, 2)
                : (totalMilestones > 0 ? Math.Round((completedMilestones / (double)totalMilestones) * 100, 2) : 0);

            var stats = new AppWorkStatsDto
            {
                Summary = new AppWorkSummaryDto
                {
                    TotalProjects = totalProjects,
                    ActiveProjects = activeProjects,
                    CompletedProjects = completedProjects,
                    OnHoldProjects = projectStatusDist.GetValueOrDefault("On Hold", 0),
                    OverdueProjects = overdueProjects,

                    TotalMilestones = totalMilestones,
                    PendingMilestones = milestoneStatusDist.GetValueOrDefault("Pending", 0),
                    InProgressMilestones = milestoneStatusDist.GetValueOrDefault("In Progress", 0),
                    CompletedMilestones = completedMilestones,
                    OverdueMilestones = overdueMilestones,

                    TotalTasks = totalTasks,
                    AssignedTasks = tasks.Count(t => !string.IsNullOrWhiteSpace(t.AssignedMember)),
                    PendingTasks = taskStatusDist.GetValueOrDefault("Pending", 0),
                    InProgressTasks = taskStatusDist.GetValueOrDefault("In Progress", 0),
                    CompletedTasks = completedTasks,
                    OverdueTasks = overdueTasks,

                    TotalBudget = totalBudget,
                    CompletedValue = completedValue,
                    PendingValue = pendingValue
                },
                TaskStatusDistribution = taskStatusDist,
                MilestoneStatusDistribution = milestoneStatusDist,
                ProjectStatusDistribution = projectStatusDist,
                TaskPriorityDistribution = taskPriorityDist,
                CompletionRatePercent = completionRate
            };

            return Ok(AppApiResponse<AppWorkStatsDto>.Ok(stats, "Work statistics retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve work statistics.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving work statistics."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/work/reports
    /// Returns comprehensive work reports with pagination, summary totals, and multi-criteria filters.
    /// Supports: page, pageSize, projectId, status, assignedUser, from, to.
    /// </summary>
    [HttpGet("reports")]
    [HttpGet("report")]
    public async Task<IActionResult> GetReports(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? projectId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? assignedUser = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Project", "View") &&
                                       !PermissionChecker.CanPerformAction(_db, User, "Report", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view work reports."));
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var effectiveUser = await ResolveAssignedUser(currentUser, assignedUser);

            var query = _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                .Include(p => p.Expenses)
                .Include(p => p.Timesheets)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(p => p.Id == projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status != null && p.Status.ToLower() == status.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(effectiveUser))
            {
                var u = effectiveUser.Trim().ToLower();
                query = query.Where(p =>
                    (p.ProjectManager != null && p.ProjectManager.ToLower().Contains(u)) ||
                    (p.AssignedMembers != null && p.AssignedMembers.ToLower().Contains(u)));
            }

            // Fetch matching projects with relations
            var allProjects = await query.ToListAsync();

            // Date filtering in memory
            if (!string.IsNullOrWhiteSpace(from))
            {
                var f = from.Trim();
                allProjects = allProjects.Where(p =>
                    (!string.IsNullOrWhiteSpace(p.StartDate) && string.Compare(p.StartDate, f, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(p.CreatedDate) && string.Compare(p.CreatedDate, f, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                var targetTo = to.Trim();
                allProjects = allProjects.Where(p =>
                    !string.IsNullOrWhiteSpace(p.Deadline) && string.Compare(p.Deadline, targetTo, StringComparison.OrdinalIgnoreCase) <= 0
                ).ToList();
            }

            var totalCount = allProjects.Count;
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Compute summary totals safely in memory
            var totalBudget = allProjects.Sum(p => p.Budget);
            var totalCompleted = allProjects.Sum(p => p.CompletedAmount);
            var totalPending = Math.Max(0, totalBudget - totalCompleted);
            var totalInvoices = allProjects.Sum(p => p.Invoices.Sum(i => i.GrandTotal));
            var totalExpenses = allProjects.Sum(p => p.Expenses.Sum(e => e.TotalAmount));
            var totalHours = allProjects.Sum(p => p.Timesheets.Sum(t => t.HoursWorked));

            // Paginated items
            var pagedProjects = allProjects
                .OrderByDescending(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var items = pagedProjects.Select(p =>
            {
                var compAmt = p.CompletedAmount;
                var pendAmt = Math.Max(0, p.Budget - compAmt);
                var progPct = p.Budget <= 0 ? 0 : (int)Math.Min(100, Math.Max(0, Math.Round((compAmt / p.Budget) * 100)));

                return new AppWorkReportItemDto
                {
                    Id = p.Id,
                    ProjectCode = p.ProjectCode,
                    ProjectName = p.ProjectName,
                    ClientName = p.ClientName,
                    ProjectManager = p.ProjectManager,
                    AssignedMembers = p.AssignedMembers,
                    Status = p.Status,
                    Priority = p.Priority,
                    StartDate = p.StartDate,
                    Deadline = p.Deadline,
                    Budget = p.Budget,
                    CompletedAmount = compAmt,
                    PendingAmount = pendAmt,
                    ProgressPercent = progPct,
                    MilestonesCount = p.Milestones.Count,
                    CompletedMilestonesCount = p.Milestones.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
                    TasksCount = p.Tasks.Count,
                    CompletedTasksCount = p.Tasks.Count(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
                    InvoicesGrandTotal = p.Invoices.Sum(i => i.GrandTotal),
                    ExpensesTotal = p.Expenses.Sum(e => e.TotalAmount),
                    TimesheetHoursTotal = p.Timesheets.Sum(t => t.HoursWorked)
                };
            }).ToList();

            var response = new AppWorkReportResponseDto
            {
                Summary = new AppWorkReportSummaryDto
                {
                    TotalProjects = totalCount,
                    TotalBudget = totalBudget,
                    TotalCompletedAmount = totalCompleted,
                    TotalPendingAmount = totalPending,
                    TotalInvoicedAmount = totalInvoices,
                    TotalExpenses = totalExpenses,
                    TotalLoggedHours = totalHours
                },
                Report = new PaginatedResult<AppWorkReportItemDto>
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = items
                }
            };

            return Ok(AppApiResponse<AppWorkReportResponseDto>.Ok(response, "Work reports retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve work reports.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving work reports."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/work/tasks
    /// Retrieves work tasks with pagination, filtering, and role-based permissions.
    /// Supports: page, pageSize, projectId, milestoneId, status, priority, assignedUser, search.
    /// </summary>
    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? projectId = null,
        [FromQuery] int? milestoneId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? assignedUser = null,
        [FromQuery] string? search = null)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Task", "View") &&
                                       !PermissionChecker.CanPerformAction(_db, User, "Project", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view tasks."));
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var effectiveUser = await ResolveAssignedUser(currentUser, assignedUser);

            var query = _db.WorkTasks
                .Include(t => t.Project)
                .Include(t => t.Milestone)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(t => t.ProjectId == projectId.Value);
            }

            if (milestoneId.HasValue && milestoneId.Value > 0)
            {
                query = query.Where(t => t.MilestoneId == milestoneId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(t => t.Status != null && t.Status.ToLower() == status.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(priority))
            {
                query = query.Where(t => t.Priority != null && t.Priority.ToLower() == priority.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(effectiveUser))
            {
                var u = effectiveUser.Trim().ToLower();
                query = query.Where(t =>
                    (t.AssignedMember != null && t.AssignedMember.ToLower().Contains(u)) ||
                    (t.Project != null && t.Project.AssignedMembers != null && t.Project.AssignedMembers.ToLower().Contains(u)) ||
                    (t.Project != null && t.Project.ProjectManager != null && t.Project.ProjectManager.ToLower().Contains(u)));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(t =>
                    (t.TaskName != null && t.TaskName.ToLower().Contains(term)) ||
                    (t.TaskCode != null && t.TaskCode.ToLower().Contains(term)) ||
                    (t.Description != null && t.Description.ToLower().Contains(term)) ||
                    (t.AssignedMember != null && t.AssignedMember.ToLower().Contains(term)));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var tasks = await query
                .OrderByDescending(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = tasks.Select(t => new TaskResponseDto
            {
                Id = t.Id,
                ProjectId = t.ProjectId,
                MilestoneId = t.MilestoneId,
                TaskCode = t.TaskCode,
                TaskName = t.TaskName ?? "",
                Description = t.Description ?? "",
                AssignedMember = t.AssignedMember ?? "",
                StartDate = t.StartDate ?? "",
                DueDate = t.DueDate ?? "",
                TaskAmount = t.TaskAmount,
                CompletedAmount = t.CompletedAmount,
                Priority = t.Priority ?? "Medium",
                Status = t.Status ?? "Pending",
                Notes = t.Notes ?? "",
                ProjectName = t.Project?.ProjectName ?? "",
                ProjectCode = t.Project?.ProjectCode ?? "",
                MilestoneName = t.Milestone?.MilestoneName ?? "",
                MilestoneCode = t.Milestone?.MilestoneCode ?? "",
                PendingAmount = t.PendingAmount,
                ProgressPercent = t.ProgressPercent,
                CreatedDate = t.CreatedDate ?? ""
            }).ToList();

            var result = new PaginatedResult<TaskResponseDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = items
            };

            return Ok(AppApiResponse<PaginatedResult<TaskResponseDto>>.Ok(result, "Work tasks retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve work tasks.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving work tasks."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/work/tasks/{id}
    /// Retrieves a single work task by its ID.
    /// </summary>
    [HttpGet("tasks/{id:int}")]
    public async Task<IActionResult> GetTaskById(int id)
    {
        try
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            if (currentUser != null && !PermissionChecker.CanPerformAction(_db, User, "Task", "View") &&
                                       !PermissionChecker.CanPerformAction(_db, User, "Project", "View"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    AppApiErrorResponse.Fail("You do not have permission to view tasks."));
            }

            var task = await _db.WorkTasks
                .Include(t => t.Project)
                .Include(t => t.Milestone)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Task with ID {id} not found."));
            }

            var response = new TaskResponseDto
            {
                Id = task.Id,
                ProjectId = task.ProjectId,
                MilestoneId = task.MilestoneId,
                TaskCode = task.TaskCode,
                TaskName = task.TaskName ?? "",
                Description = task.Description ?? "",
                AssignedMember = task.AssignedMember ?? "",
                StartDate = task.StartDate ?? "",
                DueDate = task.DueDate ?? "",
                TaskAmount = task.TaskAmount,
                CompletedAmount = task.CompletedAmount,
                Priority = task.Priority ?? "Medium",
                Status = task.Status ?? "Pending",
                Notes = task.Notes ?? "",
                ProjectName = task.Project?.ProjectName ?? "",
                ProjectCode = task.Project?.ProjectCode ?? "",
                MilestoneName = task.Milestone?.MilestoneName ?? "",
                MilestoneCode = task.Milestone?.MilestoneCode ?? "",
                PendingAmount = task.PendingAmount,
                ProgressPercent = task.ProgressPercent,
                CreatedDate = task.CreatedDate ?? ""
            };

            return Ok(AppApiResponse<TaskResponseDto>.Ok(response, "Task retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve task {TaskId}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving task details."));
        }
    }
}
