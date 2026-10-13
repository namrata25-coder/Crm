using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Milestones module.
/// Base route: /api/milestones
/// </summary>
[ApiController]
[Route("api/milestones")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class MilestonesApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<MilestonesApiController> _logger;

    public MilestonesApiController(
        AppDbContext db,
        ILogger<MilestonesApiController> logger)
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

    // GET /api/milestones
    // GET /api/milestones?projectId=1&status=Pending&search=Design
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MilestoneResponseDto>>> GetAll(
        [FromQuery] int? projectId,
        [FromQuery] string? status,
        [FromQuery] string? search)
    {
        try
        {
            var query = _db.WorkMilestones
                .Include(m => m.Project)
                .Include(m => m.Tasks)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(m => m.ProjectId == projectId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(m => m.Status == status);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(m =>
                    (m.MilestoneName != null && m.MilestoneName.ToLower().Contains(term)) ||
                    (m.MilestoneCode != null && m.MilestoneCode.ToLower().Contains(term)) ||
                    (m.Project != null && m.Project.ProjectName != null && m.Project.ProjectName.ToLower().Contains(term)) ||
                    (m.AssignedMembers != null && m.AssignedMembers.ToLower().Contains(term)));
            }

            var milestones = await query.OrderBy(m => m.Id).ToListAsync();
            var milestoneIds = milestones.Select(m => m.Id).ToList();
            var milestoneInvoices = await _db.WorkInvoices
                .Include(i => i.Payments)
                .Where(i => i.MilestoneId.HasValue && milestoneIds.Contains(i.MilestoneId.Value))
                .ToListAsync();

            // Group milestones by project to evaluate sequential dependencies per project
            var projectGroups = milestones.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

            var result = milestones.Select(m =>
            {
                var inv = milestoneInvoices.FirstOrDefault(i => i.MilestoneId == m.Id);
                var projMilestones = projectGroups.ContainsKey(m.ProjectId) ? projectGroups[m.ProjectId] : new List<WorkMilestone>();
                var depInfo = WorkDependencyHelper.EvaluateMilestone(m, projMilestones, milestoneInvoices.Where(i => i.ProjectId == m.ProjectId));

                return new MilestoneResponseDto
                {
                    Id = m.Id,
                    ProjectId = m.ProjectId,
                    ProjectName = m.Project?.ProjectName ?? "",
                    ProjectCode = m.Project?.ProjectCode ?? "",
                    ClientName = m.Project?.ClientName ?? "",
                    ClientCompany = m.Project?.ClientCompany ?? "",
                    MilestoneCode = m.MilestoneCode ?? "",
                    MilestoneName = m.MilestoneName ?? "",
                    Description = m.Description ?? "",
                    StartDate = m.StartDate ?? "",
                    DueDate = m.DueDate ?? "",
                    Amount = m.Amount,
                    CompletedAmount = m.CompletedAmount,
                    EffectiveAmount = m.EffectiveAmount,
                    EffectiveCompletedAmount = m.EffectiveCompletedAmount,
                    PendingAmount = m.PendingAmount,
                    ProgressPercent = m.ProgressPercent,
                    AssignedMembers = m.AssignedMembers ?? "",
                    Priority = m.Priority ?? "Medium",
                    Status = m.Status ?? "Pending",
                    Notes = m.Notes ?? "",
                    TasksCount = m.Tasks?.Count ?? 0,
                    IsInvoiced = inv != null,
                    InvoiceId = inv?.Id,
                    InvoiceNumber = inv?.InvoiceNumber,
                    InvoiceStatus = inv?.PaymentStatus,
                    InvoiceGrandTotal = inv?.GrandTotal,

                    // Dependency info
                    SequenceIndex = depInfo.SequenceIndex,
                    TotalMilestones = depInfo.TotalMilestones,
                    DependencyType = depInfo.DependencyType,
                    IsLocked = depInfo.IsLocked,
                    LockReason = depInfo.LockReason,
                    PrerequisiteMilestoneId = depInfo.PrerequisiteMilestoneId,
                    PrerequisiteMilestoneCode = depInfo.PrerequisiteMilestoneCode,
                    PrerequisiteMilestoneName = depInfo.PrerequisiteMilestoneName,
                    PrerequisiteStatus = depInfo.PrerequisiteStatus,
                    PrerequisiteCompleted = depInfo.PrerequisiteCompleted,
                    PrerequisiteInvoiced = depInfo.PrerequisiteInvoiced,
                    PrerequisiteInvoiceNumber = depInfo.PrerequisiteInvoiceNumber,
                    PrerequisitePaid = depInfo.PrerequisitePaid,
                    PrerequisiteInvoiceStatus = depInfo.PrerequisiteInvoiceStatus
                };
            }).OrderByDescending(m => m.Id).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/milestones failed");
            return StatusCode(500, new { message = "Failed to retrieve milestones", error = ex.Message });
        }
    }

    // GET /api/milestones/project-dependency/{projectId}
    [HttpGet("project-dependency/{projectId:int}")]
    public async Task<IActionResult> GetProjectMilestoneDependency(int projectId)
    {
        try
        {
            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
                return NotFound(new { message = $"Project with ID {projectId} not found." });

            var milestones = project.Milestones.OrderBy(m => m.Id).ToList();
            var invoices = project.Invoices.ToList();

            var evaluatedMilestones = milestones.Select(m =>
            {
                var dep = WorkDependencyHelper.EvaluateMilestone(m, milestones, invoices);
                var inv = invoices.FirstOrDefault(i => i.MilestoneId == m.Id);
                return new
                {
                    m.Id,
                    m.MilestoneCode,
                    m.MilestoneName,
                    m.Status,
                    m.Amount,
                    m.CompletedAmount,
                    m.ProgressPercent,
                    m.StartDate,
                    m.DueDate,
                    dep.SequenceIndex,
                    dep.IsLocked,
                    dep.LockReason,
                    dep.DependencyType,
                    Invoice = inv != null ? new
                    {
                        inv.Id,
                        inv.InvoiceNumber,
                        inv.PaymentStatus,
                        inv.GrandTotal,
                        PaidAmount = inv.EffectivePaidAmount,
                        IsPaid = string.Equals(inv.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) || (inv.GrandTotal > 0 && inv.EffectivePaidAmount >= inv.GrandTotal)
                    } : null
                };
            }).ToList();

            // Dependency for next milestone to be created
            var dummyNext = new WorkMilestone { Id = 0, ProjectId = projectId };
            var nextDep = WorkDependencyHelper.EvaluateMilestone(dummyNext, milestones, invoices);

            return Ok(new
            {
                Project = new
                {
                    project.Id,
                    project.ProjectCode,
                    project.ProjectName,
                    project.ClientName,
                    project.ClientCompany,
                    project.ProjectType,
                    project.Budget,
                    project.StartDate,
                    project.Deadline,
                    project.Priority,
                    project.Status,
                    project.ProjectManager,
                    project.AssignedMembers,
                    project.Description,
                    TotalMilestones = milestones.Count,
                    CompletedMilestones = milestones.Count(m => m.Status == "Completed")
                },
                Milestones = evaluatedMilestones,
                NextMilestoneDependency = nextDep
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/milestones/project-dependency/{projectId} failed", projectId);
            return StatusCode(500, new { message = "Failed to retrieve project milestone dependency", error = ex.Message });
        }
    }

    // GET /api/milestones/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MilestoneResponseDto>> GetById(int id)
    {
        try
        {
            var m = await _db.WorkMilestones
                .Include(x => x.Project)
                .Include(x => x.Tasks)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (m is null)
                return NotFound(new { message = $"Milestone with ID {id} not found." });

            var projectMilestones = await _db.WorkMilestones
                .Where(x => x.ProjectId == m.ProjectId)
                .OrderBy(x => x.Id)
                .ToListAsync();

            var projectInvoices = await _db.WorkInvoices
                .Include(i => i.Payments)
                .Where(i => i.ProjectId == m.ProjectId)
                .ToListAsync();

            var depInfo = WorkDependencyHelper.EvaluateMilestone(m, projectMilestones, projectInvoices);
            var inv = projectInvoices.FirstOrDefault(i => i.MilestoneId == m.Id);

            var result = new MilestoneResponseDto
            {
                Id = m.Id,
                ProjectId = m.ProjectId,
                ProjectName = m.Project?.ProjectName ?? "",
                ProjectCode = m.Project?.ProjectCode ?? "",
                ClientName = m.Project?.ClientName ?? "",
                ClientCompany = m.Project?.ClientCompany ?? "",
                MilestoneCode = m.MilestoneCode ?? "",
                MilestoneName = m.MilestoneName ?? "",
                Description = m.Description ?? "",
                StartDate = m.StartDate ?? "",
                DueDate = m.DueDate ?? "",
                Amount = m.Amount,
                CompletedAmount = m.CompletedAmount,
                EffectiveAmount = m.EffectiveAmount,
                EffectiveCompletedAmount = m.EffectiveCompletedAmount,
                PendingAmount = m.PendingAmount,
                ProgressPercent = m.ProgressPercent,
                AssignedMembers = m.AssignedMembers ?? "",
                Priority = m.Priority ?? "Medium",
                Status = m.Status ?? "Pending",
                Notes = m.Notes ?? "",
                TasksCount = m.Tasks?.Count ?? 0,
                IsInvoiced = inv != null,
                InvoiceId = inv?.Id,
                InvoiceNumber = inv?.InvoiceNumber,
                InvoiceStatus = inv?.PaymentStatus,
                InvoiceGrandTotal = inv?.GrandTotal,

                SequenceIndex = depInfo.SequenceIndex,
                TotalMilestones = depInfo.TotalMilestones,
                DependencyType = depInfo.DependencyType,
                IsLocked = depInfo.IsLocked,
                LockReason = depInfo.LockReason,
                PrerequisiteMilestoneId = depInfo.PrerequisiteMilestoneId,
                PrerequisiteMilestoneCode = depInfo.PrerequisiteMilestoneCode,
                PrerequisiteMilestoneName = depInfo.PrerequisiteMilestoneName,
                PrerequisiteStatus = depInfo.PrerequisiteStatus,
                PrerequisiteCompleted = depInfo.PrerequisiteCompleted,
                PrerequisiteInvoiced = depInfo.PrerequisiteInvoiced,
                PrerequisiteInvoiceNumber = depInfo.PrerequisiteInvoiceNumber,
                PrerequisitePaid = depInfo.PrerequisitePaid,
                PrerequisiteInvoiceStatus = depInfo.PrerequisiteInvoiceStatus
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/milestones/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to retrieve milestone", error = ex.Message });
        }
    }

    // POST /api/milestones
    [HttpPost]
    public async Task<ActionResult<MilestoneResponseDto>> Create([FromBody] MilestoneDto input)
    {
        try
        {
            if (input.ProjectId <= 0)
                return BadRequest(new { message = "Valid ProjectId is required." });

            if (string.IsNullOrWhiteSpace(input.MilestoneName))
                return BadRequest(new { message = "MilestoneName is required." });

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == input.ProjectId);

            if (project == null)
                return BadRequest(new { message = $"Project with ID {input.ProjectId} does not exist." });

            var dummyNext = new WorkMilestone { Id = 0, ProjectId = input.ProjectId };
            var depInfo = WorkDependencyHelper.EvaluateMilestone(dummyNext, project.Milestones, project.Invoices);

            var requestedStatus = string.IsNullOrWhiteSpace(input.Status) ? "Pending" : input.Status;
            // If milestone is locked due to prerequisite not completed or invoice not paid, it must start as Pending
            if (depInfo.IsLocked && (requestedStatus == "In Progress" || requestedStatus == "Completed"))
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
                Amount = Math.Max(0, input.Amount),
                CompletedAmount = depInfo.IsLocked ? 0 : Math.Max(0, input.CompletedAmount),
                AssignedMembers = input.AssignedMembers?.Trim() ?? "",
                Priority = string.IsNullOrWhiteSpace(input.Priority) ? "Medium" : input.Priority,
                Status = requestedStatus,
                Notes = input.Notes?.Trim() ?? ""
            };

            _db.WorkMilestones.Add(milestone);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = milestone.Id }, milestone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/milestones failed");
            return StatusCode(500, new { message = "Failed to create milestone", error = ex.Message });
        }
    }

    // PUT /api/milestones/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkMilestone>> Update(int id, [FromBody] MilestoneDto input)
    {
        try
        {
            var milestone = await _db.WorkMilestones.FindAsync(id);
            if (milestone is null)
                return NotFound(new { message = $"Milestone with ID {id} not found." });

            var targetProjectId = input.ProjectId > 0 ? input.ProjectId : milestone.ProjectId;

            if (string.IsNullOrWhiteSpace(input.MilestoneName))
                return BadRequest(new { message = "MilestoneName is required." });

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == targetProjectId);

            if (project == null)
                return BadRequest(new { message = $"Project with ID {targetProjectId} does not exist." });

            milestone.ProjectId = targetProjectId;

            // Check dependency lock condition
            var depInfo = WorkDependencyHelper.EvaluateMilestone(milestone, project.Milestones, project.Invoices);
            var newStatus = string.IsNullOrWhiteSpace(input.Status) ? milestone.Status : input.Status;

            if (depInfo.IsLocked && (newStatus == "In Progress" || newStatus == "Completed"))
            {
                return BadRequest(new
                {
                    message = $"Cannot set milestone to '{newStatus}'. {depInfo.LockReason}",
                    lockReason = depInfo.LockReason,
                    isLocked = true
                });
            }

            milestone.MilestoneName = input.MilestoneName.Trim();
            milestone.Description = input.Description?.Trim() ?? "";
            milestone.StartDate = input.StartDate?.Trim() ?? "";
            milestone.DueDate = input.DueDate?.Trim() ?? "";
            milestone.Amount = Math.Max(0, input.Amount);
            milestone.CompletedAmount = depInfo.IsLocked ? 0 : Math.Max(0, input.CompletedAmount);
            milestone.AssignedMembers = input.AssignedMembers?.Trim() ?? "";
            milestone.Priority = string.IsNullOrWhiteSpace(input.Priority) ? milestone.Priority : input.Priority;
            milestone.Status = newStatus;
            milestone.Notes = input.Notes?.Trim() ?? "";

            await _db.SaveChangesAsync();

            return Ok(milestone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /api/milestones/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to update milestone", error = ex.Message });
        }
    }

    // DELETE /api/milestones/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var milestone = await _db.WorkMilestones
                .Include(m => m.Tasks)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (milestone is null)
                return NotFound(new { message = $"Milestone with ID {id} not found." });

            // Unlink tasks before removing milestone so tasks are not orphaned or deleted abruptly
            if (milestone.Tasks != null)
            {
                foreach (var t in milestone.Tasks)
                {
                    t.MilestoneId = null;
                }
            }

            _db.WorkMilestones.Remove(milestone);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Milestone deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /api/milestones/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to delete milestone", error = ex.Message });
        }
    }
}

public class MilestoneDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string? MilestoneCode { get; set; }
    public string MilestoneName { get; set; } = "";
    public string? Description { get; set; } = "";
    public string? StartDate { get; set; } = "";
    public string? DueDate { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal CompletedAmount { get; set; }
    public string? AssignedMembers { get; set; } = "";
    public string? Priority { get; set; } = "Medium";
    public string? Status { get; set; } = "Pending";
    public string? Notes { get; set; } = "";
}

public class MilestoneResponseDto : MilestoneDto
{
    public string ProjectName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientCompany { get; set; } = "";
    public decimal EffectiveAmount { get; set; }
    public decimal EffectiveCompletedAmount { get; set; }
    public decimal PendingAmount { get; set; }
    public int ProgressPercent { get; set; }
    public int TasksCount { get; set; }
    public bool IsInvoiced { get; set; }
    public int? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceStatus { get; set; }
    public decimal? InvoiceGrandTotal { get; set; }

    // Dependency Properties
    public int SequenceIndex { get; set; } = 1;
    public int TotalMilestones { get; set; } = 1;
    public string DependencyType { get; set; } = "Strict Finish-to-Start (Sequential with 100% Completion & Full Invoice Paid Requirement)";
    public bool IsLocked { get; set; } = false;
    public string LockReason { get; set; } = "";
    public int? PrerequisiteMilestoneId { get; set; }
    public string? PrerequisiteMilestoneCode { get; set; } = "";
    public string? PrerequisiteMilestoneName { get; set; } = "";
    public string? PrerequisiteStatus { get; set; } = "";
    public bool PrerequisiteCompleted { get; set; } = true;
    public bool PrerequisiteInvoiced { get; set; } = true;
    public string? PrerequisiteInvoiceNumber { get; set; } = "";
    public bool PrerequisitePaid { get; set; } = true;
    public string? PrerequisiteInvoiceStatus { get; set; } = "";
}
