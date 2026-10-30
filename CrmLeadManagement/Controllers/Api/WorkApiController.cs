using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Work Overview &amp; Module Statistics.
/// Base route: /api/work
/// </summary>
[ApiController]
[Route("api/work")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class WorkApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<WorkApiController> _logger;

    public WorkApiController(AppDbContext db, ILogger<WorkApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET /api/work
    // GET /api/work/summary
    [HttpGet]
    [HttpGet("summary")]
    [HttpGet("stats")]
    public async Task<ActionResult<WorkSummaryDto>> GetSummary()
    {
        try
        {
            var projects = await _db.WorkProjects
                .Include(p => p.Milestones)
                    .ThenInclude(m => m.Tasks)
                .Include(p => p.Tasks)
                .ToListAsync();

            projects = ProjectAccessPolicy.FilterAccessible(_db, User, projects);
            var accessibleProjectIds = projects.Select(project => project.Id).ToList();
            var milestones = await _db.WorkMilestones
                .Include(m => m.Tasks)
                .Where(m => accessibleProjectIds.Contains(m.ProjectId))
                .ToListAsync();
            var tasks = await _db.WorkTasks
                .Where(t => accessibleProjectIds.Contains(t.ProjectId))
                .ToListAsync();

            var totalProjects = projects.Count;
            var activeProjects = projects.Count(p => string.Equals(p.Status, "Active", StringComparison.OrdinalIgnoreCase));
            var completedProjects = projects.Count(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var onHoldProjects = projects.Count(p => string.Equals(p.Status, "On Hold", StringComparison.OrdinalIgnoreCase));

            var totalBudget = projects.Sum(p => p.Budget);
            var totalCompletedValue = projects.Sum(p => p.CompletedAmount);
            var totalPendingValue = Math.Max(0, totalBudget - totalCompletedValue);

            var totalMilestones = milestones.Count;
            var completedMilestones = milestones.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var inProgressMilestones = milestones.Count(m => string.Equals(m.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            var pendingMilestones = milestones.Count(m => string.Equals(m.Status, "Pending", StringComparison.OrdinalIgnoreCase));

            var totalTasks = tasks.Count;
            var completedTasks = tasks.Count(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var inProgressTasks = tasks.Count(t => string.Equals(t.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            var pendingTasks = tasks.Count(t => string.Equals(t.Status, "Pending", StringComparison.OrdinalIgnoreCase));

            var summary = new WorkSummaryDto
            {
                TotalProjects = totalProjects,
                ActiveProjects = activeProjects,
                CompletedProjects = completedProjects,
                OnHoldProjects = onHoldProjects,
                TotalBudget = totalBudget,
                CompletedValue = totalCompletedValue,
                PendingValue = totalPendingValue,
                TotalMilestones = totalMilestones,
                CompletedMilestones = completedMilestones,
                InProgressMilestones = inProgressMilestones,
                PendingMilestones = pendingMilestones,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                InProgressTasks = inProgressTasks,
                PendingTasks = pendingTasks
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/work/summary failed");
            return StatusCode(500, new { message = "Failed to retrieve work summary", error = ex.Message });
        }
    }

    // GET /api/work/reports
    [HttpGet("reports")]
    public async Task<ActionResult> GetReports([FromQuery] int? projectId)
    {
        try
        {
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

            var projects = ProjectAccessPolicy.FilterAccessible(
                _db,
                User,
                await query.ToListAsync());
            var reportData = projects.Select(p => new
            {
                p.Id,
                p.ProjectCode,
                p.ProjectName,
                p.ClientName,
                p.ProjectType,
                p.Budget,
                CompletedAmount = p.CompletedAmount,
                PendingAmount = p.PendingAmount,
                ProgressPercent = p.ProgressPercent,
                p.Status,
                p.Priority,
                p.StartDate,
                p.Deadline,
                MilestonesCount = p.Milestones.Count,
                TasksCount = p.Tasks.Count,
                InvoicesTotal = p.Invoices.Sum(i => i.GrandTotal),
                ExpensesTotal = p.Expenses.Sum(e => e.TotalAmount),
                TimesheetHoursTotal = p.Timesheets.Sum(t => t.HoursWorked)
            }).ToList();

            return Ok(reportData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/work/reports failed");
            return StatusCode(500, new { message = "Failed to retrieve reports", error = ex.Message });
        }
    }
}

public class WorkSummaryDto
{
    public int TotalProjects { get; set; }
    public int ActiveProjects { get; set; }
    public int CompletedProjects { get; set; }
    public int OnHoldProjects { get; set; }
    public decimal TotalBudget { get; set; }
    public decimal CompletedValue { get; set; }
    public decimal PendingValue { get; set; }
    public int TotalMilestones { get; set; }
    public int CompletedMilestones { get; set; }
    public int InProgressMilestones { get; set; }
    public int PendingMilestones { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int InProgressTasks { get; set; }
    public int PendingTasks { get; set; }
}
