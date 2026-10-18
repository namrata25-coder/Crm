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
/// REST API endpoints for Work Summary, Work Statistics, and Work Reports under /api/v1/app/work.
/// </summary>
[ApiController]
[Route("api/v1/app/work")]
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
    /// GET /api/v1/app/work/summary
    /// Returns aggregated counts and financial metrics calculated from real database records.
    /// Supports filtering by assignedUser, projectId, and date range (from, to).
    /// </summary>
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

            // Role-based data access
            if (currentUser != null && string.IsNullOrWhiteSpace(assignedUser))
            {
                var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == currentUser.Role);
                if (role != null && string.Equals(role.DataAccess, "OnlyAssigned", StringComparison.OrdinalIgnoreCase))
                {
                    assignedUser = currentUser.Name;
                }
            }

            var projectsQuery = _db.WorkProjects.AsQueryable();
            var milestonesQuery = _db.WorkMilestones.Include(m => m.Project).AsQueryable();
            var tasksQuery = _db.WorkTasks.AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                projectsQuery = projectsQuery.Where(p => p.Id == projectId.Value);
                milestonesQuery = milestonesQuery.Where(m => m.ProjectId == projectId.Value);
                tasksQuery = tasksQuery.Where(t => t.ProjectId == projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(assignedUser))
            {
                var u = assignedUser.Trim().ToLower();
                projectsQuery = projectsQuery.Where(p =>
                    (p.ProjectManager != null && p.ProjectManager.ToLower().Contains(u)) ||
                    (p.AssignedMembers != null && p.AssignedMembers.ToLower().Contains(u)));

                milestonesQuery = milestonesQuery.Where(m =>
                    (m.AssignedMembers != null && m.AssignedMembers.ToLower().Contains(u)) ||
                    (m.Project != null && m.Project.AssignedMembers != null && m.Project.AssignedMembers.ToLower().Contains(u)));

                tasksQuery = tasksQuery.Where(t =>
                    t.AssignedMember != null && t.AssignedMember.ToLower().Contains(u));
            }

            if (!string.IsNullOrWhiteSpace(from))
            {
                projectsQuery = projectsQuery.Where(p => string.Compare(p.StartDate, from) >= 0 || string.Compare(p.CreatedDate, from) >= 0);
                milestonesQuery = milestonesQuery.Where(m => string.Compare(m.StartDate, from) >= 0);
                tasksQuery = tasksQuery.Where(t => string.Compare(t.StartDate, from) >= 0 || string.Compare(t.CreatedDate, from) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                projectsQuery = projectsQuery.Where(p => string.Compare(p.Deadline, to) <= 0);
                milestonesQuery = milestonesQuery.Where(m => string.Compare(m.DueDate, to) <= 0);
                tasksQuery = tasksQuery.Where(t => string.Compare(t.DueDate, to) <= 0);
            }

            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // Execute project calculations
            var projects = await projectsQuery.Select(p => new
            {
                p.Id,
                p.Status,
                p.Deadline,
                p.Budget
            }).ToListAsync();

            var totalProjects = projects.Count;
            var activeProjects = projects.Count(p => string.Equals(p.Status, "Active", StringComparison.OrdinalIgnoreCase));
            var completedProjects = projects.Count(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var onHoldProjects = projects.Count(p => string.Equals(p.Status, "On Hold", StringComparison.OrdinalIgnoreCase));
            var overdueProjects = projects.Count(p => !string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                                                      !string.IsNullOrWhiteSpace(p.Deadline) && string.Compare(p.Deadline, todayStr) < 0);
            var totalBudget = projects.Sum(p => p.Budget);

            // Execute milestone calculations
            var milestones = await milestonesQuery.Select(m => new
            {
                m.Id,
                m.Status,
                m.DueDate,
                m.Amount,
                m.CompletedAmount
            }).ToListAsync();

            var totalMilestones = milestones.Count;
            var pendingMilestones = milestones.Count(m => string.Equals(m.Status, "Pending", StringComparison.OrdinalIgnoreCase));
            var inProgressMilestones = milestones.Count(m => string.Equals(m.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            var completedMilestones = milestones.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var overdueMilestones = milestones.Count(m => !string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                                                          !string.IsNullOrWhiteSpace(m.DueDate) && string.Compare(m.DueDate, todayStr) < 0);
            var completedMilestonesValue = milestones.Sum(m => m.CompletedAmount);

            // Execute task calculations
            var tasks = await tasksQuery.Select(t => new
            {
                t.Id,
                t.Status,
                t.DueDate,
                t.AssignedMember,
                t.TaskAmount,
                t.CompletedAmount
            }).ToListAsync();

            var totalTasks = tasks.Count;
            var assignedTasks = tasks.Count(t => !string.IsNullOrWhiteSpace(t.AssignedMember));
            var pendingTasks = tasks.Count(t => string.Equals(t.Status, "Pending", StringComparison.OrdinalIgnoreCase));
            var inProgressTasks = tasks.Count(t => string.Equals(t.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            var completedTasks = tasks.Count(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var overdueTasks = tasks.Count(t => !string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                                                 !string.IsNullOrWhiteSpace(t.DueDate) && string.Compare(t.DueDate, todayStr) < 0);
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
    /// Returns detailed work statistics for dashboards, with status distributions and completion rates.
    /// Supports filtering by date range (from, to), projectId, and assignedUser.
    /// </summary>
    [HttpGet("stats")]
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

            // Role-based data access
            if (currentUser != null && string.IsNullOrWhiteSpace(assignedUser))
            {
                var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == currentUser.Role);
                if (role != null && string.Equals(role.DataAccess, "OnlyAssigned", StringComparison.OrdinalIgnoreCase))
                {
                    assignedUser = currentUser.Name;
                }
            }

            var projectsQuery = _db.WorkProjects.AsQueryable();
            var milestonesQuery = _db.WorkMilestones.Include(m => m.Project).AsQueryable();
            var tasksQuery = _db.WorkTasks.AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                projectsQuery = projectsQuery.Where(p => p.Id == projectId.Value);
                milestonesQuery = milestonesQuery.Where(m => m.ProjectId == projectId.Value);
                tasksQuery = tasksQuery.Where(t => t.ProjectId == projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(assignedUser))
            {
                var u = assignedUser.Trim().ToLower();
                projectsQuery = projectsQuery.Where(p =>
                    (p.ProjectManager != null && p.ProjectManager.ToLower().Contains(u)) ||
                    (p.AssignedMembers != null && p.AssignedMembers.ToLower().Contains(u)));

                milestonesQuery = milestonesQuery.Where(m =>
                    (m.AssignedMembers != null && m.AssignedMembers.ToLower().Contains(u)) ||
                    (m.Project != null && m.Project.AssignedMembers != null && m.Project.AssignedMembers.ToLower().Contains(u)));

                tasksQuery = tasksQuery.Where(t =>
                    t.AssignedMember != null && t.AssignedMember.ToLower().Contains(u));
            }

            if (!string.IsNullOrWhiteSpace(from))
            {
                projectsQuery = projectsQuery.Where(p => string.Compare(p.StartDate, from) >= 0 || string.Compare(p.CreatedDate, from) >= 0);
                milestonesQuery = milestonesQuery.Where(m => string.Compare(m.StartDate, from) >= 0);
                tasksQuery = tasksQuery.Where(t => string.Compare(t.StartDate, from) >= 0 || string.Compare(t.CreatedDate, from) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                projectsQuery = projectsQuery.Where(p => string.Compare(p.Deadline, to) <= 0);
                milestonesQuery = milestonesQuery.Where(m => string.Compare(m.DueDate, to) <= 0);
                tasksQuery = tasksQuery.Where(t => string.Compare(t.DueDate, to) <= 0);
            }

            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // Aggregated distributions
            var taskStatusDist = await tasksQuery
                .GroupBy(t => string.IsNullOrWhiteSpace(t.Status) ? "Pending" : t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);

            var taskPriorityDist = await tasksQuery
                .GroupBy(t => string.IsNullOrWhiteSpace(t.Priority) ? "Medium" : t.Priority)
                .Select(g => new { Priority = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Priority, x => x.Count);

            var milestoneStatusDist = await milestonesQuery
                .GroupBy(m => string.IsNullOrWhiteSpace(m.Status) ? "Pending" : m.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);

            var projectStatusDist = await projectsQuery
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Status) ? "Active" : p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);

            // Compute summary counts
            var totalTasks = taskStatusDist.Values.Sum();
            var completedTasks = taskStatusDist.GetValueOrDefault("Completed", 0);
            var totalMilestones = milestoneStatusDist.Values.Sum();
            var completedMilestones = milestoneStatusDist.GetValueOrDefault("Completed", 0);
            var totalProjects = projectStatusDist.Values.Sum();
            var activeProjects = projectStatusDist.GetValueOrDefault("Active", 0);
            var completedProjects = projectStatusDist.GetValueOrDefault("Completed", 0);

            var overdueTasks = await tasksQuery.CountAsync(t =>
                (t.Status == null || t.Status.ToLower() != "completed") &&
                t.DueDate != null && t.DueDate != "" && string.Compare(t.DueDate, todayStr) < 0);

            var overdueMilestones = await milestonesQuery.CountAsync(m =>
                (m.Status == null || m.Status.ToLower() != "completed") &&
                m.DueDate != null && m.DueDate != "" && string.Compare(m.DueDate, todayStr) < 0);

            var overdueProjects = await projectsQuery.CountAsync(p =>
                (p.Status == null || p.Status.ToLower() != "completed") &&
                p.Deadline != null && p.Deadline != "" && string.Compare(p.Deadline, todayStr) < 0);

            var totalBudget = await projectsQuery.SumAsync(p => p.Budget);
            var completedValue = await tasksQuery.SumAsync(t => t.CompletedAmount);
            if (completedValue == 0)
            {
                completedValue = await milestonesQuery.SumAsync(m => m.CompletedAmount);
            }
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
                    AssignedTasks = await tasksQuery.CountAsync(t => !string.IsNullOrWhiteSpace(t.AssignedMember)),
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

            // Role-based data access
            if (currentUser != null && string.IsNullOrWhiteSpace(assignedUser))
            {
                var role = await _db.RoleMasters.FirstOrDefaultAsync(r => r.RoleName == currentUser.Role);
                if (role != null && string.Equals(role.DataAccess, "OnlyAssigned", StringComparison.OrdinalIgnoreCase))
                {
                    assignedUser = currentUser.Name;
                }
            }

            var query = _db.WorkProjects.AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(p => p.Id == projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status != null && p.Status.ToLower() == status.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(assignedUser))
            {
                var u = assignedUser.Trim().ToLower();
                query = query.Where(p =>
                    (p.ProjectManager != null && p.ProjectManager.ToLower().Contains(u)) ||
                    (p.AssignedMembers != null && p.AssignedMembers.ToLower().Contains(u)));
            }

            if (!string.IsNullOrWhiteSpace(from))
            {
                query = query.Where(p => string.Compare(p.StartDate, from) >= 0 || string.Compare(p.CreatedDate, from) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                query = query.Where(p => string.Compare(p.Deadline, to) <= 0);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Compute summary totals efficiently without loading entire dataset into memory
            var summaryData = await query.Select(p => new
            {
                p.Budget,
                Completed = p.Tasks.Sum(t => (decimal?)t.CompletedAmount) ?? p.Milestones.Sum(m => (decimal?)m.CompletedAmount) ?? 0m,
                Invoices = p.Invoices.Sum(i => (decimal?)i.GrandTotal) ?? 0m,
                Expenses = p.Expenses.Sum(e => (decimal?)e.TotalAmount) ?? 0m,
                Timesheets = p.Timesheets.Sum(t => (decimal?)t.HoursWorked) ?? 0m
            }).ToListAsync();

            var totalBudget = summaryData.Sum(x => x.Budget);
            var totalCompleted = summaryData.Sum(x => x.Completed);
            var totalPending = Math.Max(0, totalBudget - totalCompleted);
            var totalInvoices = summaryData.Sum(x => x.Invoices);
            var totalExpenses = summaryData.Sum(x => x.Expenses);
            var totalHours = summaryData.Sum(x => x.Timesheets);

            // Paginated items projection
            var items = await query
                .OrderByDescending(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new AppWorkReportItemDto
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
                    CompletedAmount = p.Tasks.Any() ? p.Tasks.Sum(t => t.CompletedAmount) : p.Milestones.Sum(m => m.CompletedAmount),
                    PendingAmount = Math.Max(0, p.Budget - (p.Tasks.Any() ? p.Tasks.Sum(t => t.CompletedAmount) : p.Milestones.Sum(m => m.CompletedAmount))),
                    ProgressPercent = p.Budget <= 0 ? 0 : (int)Math.Min(100, Math.Round((p.Tasks.Any() ? p.Tasks.Sum(t => t.CompletedAmount) : p.Milestones.Sum(m => m.CompletedAmount)) / p.Budget * 100)),
                    MilestonesCount = p.Milestones.Count,
                    CompletedMilestonesCount = p.Milestones.Count(m => m.Status == "Completed"),
                    TasksCount = p.Tasks.Count,
                    CompletedTasksCount = p.Tasks.Count(t => t.Status == "Completed"),
                    InvoicesGrandTotal = p.Invoices.Sum(i => i.GrandTotal),
                    ExpensesTotal = p.Expenses.Sum(e => e.TotalAmount),
                    TimesheetHoursTotal = p.Timesheets.Sum(t => t.HoursWorked)
                })
                .ToListAsync();

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
}
