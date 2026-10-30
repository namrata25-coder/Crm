using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Tasks module.
/// Base route: /api/tasks
/// </summary>
[ApiController]
[Route("api/tasks")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class TasksApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<TasksApiController> _logger;

    public TasksApiController(
        AppDbContext db,
        ILogger<TasksApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string NextTaskCode()
    {
        var codes = _db.WorkTasks.Select(t => t.TaskCode).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => { var n = c!.StartsWith("TSK-", StringComparison.OrdinalIgnoreCase) ? c.Substring(4) : c; return int.TryParse(n, out var v) ? v : 1000; })
            .DefaultIfEmpty(1000).Max();
        int next = Math.Max(max + 1, 1001);
        while (codes.Any(c => string.Equals(c, $"TSK-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"TSK-{next}";
    }

    // GET /api/tasks
    // GET /api/tasks?projectId=1&milestoneId=2&status=In+Progress&search=login
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskResponseDto>>> GetAll(
        [FromQuery] int? projectId,
        [FromQuery] int? milestoneId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] string? assignedMember,
        [FromQuery] string? search)
    {
        try
        {
            var query = _db.WorkTasks
                .Include(t => t.Project)
                .Include(t => t.Milestone)
                .AsQueryable();

            var accessibleProjectIds = await ProjectAccessPolicy.GetAccessibleProjectIdsAsync(_db, User);
            query = query.Where(t => accessibleProjectIds.Contains(t.ProjectId));

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(t => t.ProjectId == projectId.Value);

            if (milestoneId.HasValue && milestoneId.Value > 0)
                query = query.Where(t => t.MilestoneId == milestoneId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(t => t.Status == status);

            if (!string.IsNullOrWhiteSpace(priority))
                query = query.Where(t => t.Priority == priority);

            if (!string.IsNullOrWhiteSpace(assignedMember))
                query = query.Where(t => t.AssignedMember != null && t.AssignedMember.ToLower().Contains(assignedMember.Trim().ToLower()));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(t =>
                    (t.TaskName != null && t.TaskName.ToLower().Contains(term)) ||
                    (t.TaskCode != null && t.TaskCode.ToLower().Contains(term)) ||
                    (t.Description != null && t.Description.ToLower().Contains(term)) ||
                    (t.AssignedMember != null && t.AssignedMember.ToLower().Contains(term)) ||
                    (t.Project != null && t.Project.ProjectName != null && t.Project.ProjectName.ToLower().Contains(term)) ||
                    (t.Milestone != null && t.Milestone.MilestoneName != null && t.Milestone.MilestoneName.ToLower().Contains(term)));
            }

            var tasks = await query.OrderBy(t => t.Id).ToListAsync();

            // Load all project milestones and invoices for dependency evaluation
            var projectIds = tasks.Select(t => t.ProjectId).Distinct().ToList();
            var allMilestones = await _db.WorkMilestones.Where(m => projectIds.Contains(m.ProjectId)).ToListAsync();
            var allInvoices = await _db.WorkInvoices.Include(i => i.Payments).Where(i => projectIds.Contains(i.ProjectId)).ToListAsync();

            var result = tasks.Select(t =>
            {
                var scopeTasks = t.MilestoneId.HasValue
                    ? tasks.Where(x => x.MilestoneId == t.MilestoneId).ToList()
                    : tasks.Where(x => x.ProjectId == t.ProjectId && !x.MilestoneId.HasValue).ToList();

                var parentMs = t.MilestoneId.HasValue ? allMilestones.FirstOrDefault(m => m.Id == t.MilestoneId.Value) : null;
                var projMilestones = allMilestones.Where(m => m.ProjectId == t.ProjectId).ToList();
                var projInvoices = allInvoices.Where(i => i.ProjectId == t.ProjectId).ToList();
                var projTasks = tasks.Where(x => x.ProjectId == t.ProjectId).ToList();

                var parsedNotes = WorkDependencyHelper.ParseTaskDependencyNotes(t.Notes);
                var depInfo = WorkDependencyHelper.EvaluateTask(t, scopeTasks, parentMs, projMilestones, projInvoices, projTasks, parsedNotes.HasDependency, parsedNotes.DependencyTaskId);

                return new TaskResponseDto
                {
                    Id = t.Id,
                    ProjectId = t.ProjectId,
                    ProjectName = t.Project?.ProjectName ?? "",
                    ProjectCode = t.Project?.ProjectCode ?? "",
                    MilestoneId = t.MilestoneId,
                    MilestoneName = t.Milestone?.MilestoneName ?? "",
                    MilestoneCode = t.Milestone?.MilestoneCode ?? "",
                    TaskCode = t.TaskCode ?? "",
                    TaskName = t.TaskName ?? "",
                    Description = t.Description ?? "",
                    AssignedMember = t.AssignedMember ?? "",
                    StartDate = t.StartDate ?? "",
                    DueDate = t.DueDate ?? "",
                    TaskAmount = t.TaskAmount,
                    CompletedAmount = t.CompletedAmount,
                    PendingAmount = t.PendingAmount,
                    ProgressPercent = t.ProgressPercent,
                    Priority = t.Priority ?? "Medium",
                    Status = t.Status ?? "Pending",
                    Notes = parsedNotes.CleanNotes,
                    CreatedDate = t.CreatedDate ?? "",

                    // Dependency fields
                    HasDependency = parsedNotes.HasDependency,
                    DependencyTaskId = parsedNotes.DependencyTaskId,
                    SequenceIndex = depInfo.SequenceIndex,
                    TotalTasks = depInfo.TotalTasks,
                    DependencyType = depInfo.DependencyType,
                    IsLocked = depInfo.IsLocked,
                    LockReason = depInfo.LockReason,
                    PrerequisiteTaskId = depInfo.PrerequisiteTaskId,
                    PrerequisiteTaskCode = depInfo.PrerequisiteTaskCode,
                    PrerequisiteTaskName = depInfo.PrerequisiteTaskName,
                    PrerequisiteStatus = depInfo.PrerequisiteStatus,
                    PrerequisiteCompleted = depInfo.PrerequisiteCompleted,
                    ParentMilestoneLocked = depInfo.ParentMilestoneLocked,
                    ParentMilestoneCode = depInfo.ParentMilestoneCode
                };
            }).OrderByDescending(t => t.Id).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/tasks failed");
            return StatusCode(500, new { message = "Failed to retrieve tasks", error = ex.Message });
        }
    }

    // GET /api/tasks/project-dependency/{projectId}
    [HttpGet("project-dependency/{projectId:int}")]
    public async Task<IActionResult> GetProjectTaskDependency(int projectId, [FromQuery] int? milestoneId)
    {
        try
        {
            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null || !ProjectAccessPolicy.CanAccess(_db, User, project))
                return NotFound(new { message = $"Project with ID {projectId} not found." });

            var milestones = project.Milestones.OrderBy(m => m.Id).ToList();
            var invoices = project.Invoices.ToList();
            var allTasks = project.Tasks.OrderBy(t => t.Id).ToList();

            WorkMilestone? targetMilestone = null;
            if (milestoneId.HasValue && milestoneId.Value > 0)
            {
                targetMilestone = milestones.FirstOrDefault(m => m.Id == milestoneId.Value);
            }

            var scopeTasks = targetMilestone != null
                ? allTasks.Where(t => t.MilestoneId == targetMilestone.Id).ToList()
                : allTasks.Where(t => !t.MilestoneId.HasValue).ToList();

            var evaluatedTasks = scopeTasks.Select(t =>
            {
                var parsedNotes = WorkDependencyHelper.ParseTaskDependencyNotes(t.Notes);
                var dep = WorkDependencyHelper.EvaluateTask(
                    t, 
                    scopeTasks, 
                    targetMilestone, 
                    milestones, 
                    invoices, 
                    allTasks, 
                    parsedNotes.HasDependency, 
                    parsedNotes.DependencyTaskId);

                return new
                {
                    t.Id,
                    t.TaskCode,
                    t.TaskName,
                    t.Status,
                    t.TaskAmount,
                    t.CompletedAmount,
                    t.ProgressPercent,
                    t.AssignedMember,
                    t.StartDate,
                    t.DueDate,
                    dep.SequenceIndex,
                    dep.IsLocked,
                    dep.LockReason,
                    dep.DependencyType,
                    dep.ParentMilestoneLocked,
                    dep.ParentMilestoneCode,
                    parsedNotes.HasDependency,
                    parsedNotes.DependencyTaskId
                };
            }).ToList();

            // Next task creation dependency
            var dummyNext = new WorkTask { Id = 0, ProjectId = projectId, MilestoneId = targetMilestone?.Id };
            var nextDep = WorkDependencyHelper.EvaluateTask(
                dummyNext, 
                scopeTasks, 
                targetMilestone, 
                milestones, 
                invoices, 
                allTasks, 
                false, 
                null);

            var existingTasks = allTasks.Select(x => new
            {
                x.Id,
                x.TaskCode,
                x.TaskName,
                x.Status,
                x.MilestoneId
            }).ToList();

            return Ok(new
            {
                Project = new
                {
                    project.Id,
                    project.ProjectCode,
                    project.ProjectName,
                    project.ClientName,
                    project.ClientCompany,
                    project.Status
                },
                Milestone = targetMilestone != null ? new
                {
                    targetMilestone.Id,
                    targetMilestone.MilestoneCode,
                    targetMilestone.MilestoneName,
                    targetMilestone.Amount,
                    targetMilestone.StartDate,
                    targetMilestone.DueDate,
                    targetMilestone.Status,
                    IsLocked = WorkDependencyHelper.EvaluateMilestone(targetMilestone, milestones, invoices).IsLocked,
                    LockReason = WorkDependencyHelper.EvaluateMilestone(targetMilestone, milestones, invoices).LockReason
                } : null,
                NextTaskDependency = nextDep,
                ExistingTasks = existingTasks
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/tasks/project-dependency/{projectId} failed", projectId);
            return StatusCode(500, new { message = "Failed to retrieve project task dependency", error = ex.Message });
        }
    }

    // GET /api/tasks/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TaskResponseDto>> GetById(int id)
    {
        try
        {
            var t = await _db.WorkTasks
                .Include(x => x.Project)
                .Include(x => x.Milestone)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (t is null)
                return NotFound(new { message = $"Task with ID {id} not found." });
            if (t.Project is null || !ProjectAccessPolicy.CanAccess(_db, User, t.Project))
                return NotFound(new { message = $"Task with ID {id} not found." });

            var projectMilestones = await _db.WorkMilestones.Where(m => m.ProjectId == t.ProjectId).ToListAsync();
            var projectInvoices = await _db.WorkInvoices.Include(i => i.Payments).Where(i => i.ProjectId == t.ProjectId).ToListAsync();
            var projectTasks = await _db.WorkTasks.Where(x => x.ProjectId == t.ProjectId).ToListAsync();

            var scopeTasks = t.MilestoneId.HasValue
                ? projectTasks.Where(x => x.MilestoneId == t.MilestoneId).OrderBy(x => x.Id).ToList()
                : projectTasks.Where(x => !x.MilestoneId.HasValue).OrderBy(x => x.Id).ToList();

            var parsedNotes = WorkDependencyHelper.ParseTaskDependencyNotes(t.Notes);
            var depInfo = WorkDependencyHelper.EvaluateTask(
                t, 
                scopeTasks, 
                t.Milestone, 
                projectMilestones, 
                projectInvoices, 
                projectTasks, 
                parsedNotes.HasDependency, 
                parsedNotes.DependencyTaskId);

            var result = new TaskResponseDto
            {
                Id = t.Id,
                ProjectId = t.ProjectId,
                ProjectName = t.Project?.ProjectName ?? "",
                ProjectCode = t.Project?.ProjectCode ?? "",
                MilestoneId = t.MilestoneId,
                MilestoneName = t.Milestone?.MilestoneName ?? "",
                MilestoneCode = t.Milestone?.MilestoneCode ?? "",
                TaskCode = t.TaskCode ?? "",
                TaskName = t.TaskName ?? "",
                Description = t.Description ?? "",
                AssignedMember = t.AssignedMember ?? "",
                StartDate = t.StartDate ?? "",
                DueDate = t.DueDate ?? "",
                TaskAmount = t.TaskAmount,
                CompletedAmount = t.CompletedAmount,
                PendingAmount = t.PendingAmount,
                ProgressPercent = t.ProgressPercent,
                Priority = t.Priority ?? "Medium",
                Status = t.Status ?? "Pending",
                Notes = parsedNotes.CleanNotes,
                CreatedDate = t.CreatedDate ?? "",

                // Dependency fields
                HasDependency = parsedNotes.HasDependency,
                DependencyTaskId = parsedNotes.DependencyTaskId,
                SequenceIndex = depInfo.SequenceIndex,
                TotalTasks = depInfo.TotalTasks,
                DependencyType = depInfo.DependencyType,
                IsLocked = depInfo.IsLocked,
                LockReason = depInfo.LockReason,
                PrerequisiteTaskId = depInfo.PrerequisiteTaskId,
                PrerequisiteTaskCode = depInfo.PrerequisiteTaskCode,
                PrerequisiteTaskName = depInfo.PrerequisiteTaskName,
                PrerequisiteStatus = depInfo.PrerequisiteStatus,
                PrerequisiteCompleted = depInfo.PrerequisiteCompleted,
                ParentMilestoneLocked = depInfo.ParentMilestoneLocked,
                ParentMilestoneCode = depInfo.ParentMilestoneCode
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/tasks/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to retrieve task", error = ex.Message });
        }
    }

    // POST /api/tasks
    [HttpPost]
    public async Task<ActionResult<TaskResponseDto>> Create([FromBody] TaskDto input)
    {
        try
        {
            if (input.ProjectId <= 0)
                return BadRequest(new { message = "Valid ProjectId is required." });

            if (string.IsNullOrWhiteSpace(input.TaskName))
                return BadRequest(new { message = "TaskName is required." });

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == input.ProjectId);

            if (project == null)
                return BadRequest(new { message = $"Project with ID {input.ProjectId} does not exist." });
            if (!ProjectAccessPolicy.CanAccess(_db, User, project))
                return NotFound(new { message = $"Project with ID {input.ProjectId} does not exist." });

            WorkMilestone? parentMilestone = null;
            if (input.MilestoneId.HasValue && input.MilestoneId.Value > 0)
            {
                parentMilestone = project.Milestones.FirstOrDefault(m => m.Id == input.MilestoneId.Value);
                if (parentMilestone == null)
                    return BadRequest(new { message = $"Milestone with ID {input.MilestoneId.Value} does not exist." });
            }

            var scopeTasks = parentMilestone != null
                ? project.Tasks.Where(t => t.MilestoneId == parentMilestone.Id).OrderBy(t => t.Id).ToList()
                : project.Tasks.Where(t => !t.MilestoneId.HasValue).OrderBy(t => t.Id).ToList();

            bool hasDep = input.HasDependency ?? (input.DependencyTaskId.HasValue && input.DependencyTaskId.Value > 0);
            var dummyNext = new WorkTask { Id = 0, ProjectId = input.ProjectId, MilestoneId = parentMilestone?.Id };
            var depInfo = WorkDependencyHelper.EvaluateTask(
                dummyNext, 
                scopeTasks, 
                parentMilestone, 
                project.Milestones, 
                project.Invoices, 
                project.Tasks, 
                hasDep, 
                input.DependencyTaskId);

            var requestedStatus = string.IsNullOrWhiteSpace(input.Status) ? "Pending" : input.Status;
            if (depInfo.IsLocked && (requestedStatus == "In Progress" || requestedStatus == "Completed"))
            {
                requestedStatus = "Pending";
            }

            var formattedNotes = WorkDependencyHelper.FormatTaskNotesWithDependency(input.Notes, hasDep, input.DependencyTaskId);

            var task = new WorkTask
            {
                ProjectId = input.ProjectId,
                MilestoneId = (input.MilestoneId.HasValue && input.MilestoneId.Value > 0) ? input.MilestoneId : null,
                TaskCode = NextTaskCode(),
                TaskName = input.TaskName.Trim(),
                Description = input.Description?.Trim() ?? "",
                AssignedMember = input.AssignedMember?.Trim() ?? "",
                StartDate = input.StartDate?.Trim() ?? "",
                DueDate = input.DueDate?.Trim() ?? "",
                TaskAmount = Math.Max(0, input.TaskAmount),
                CompletedAmount = depInfo.IsLocked ? 0 : Math.Max(0, input.CompletedAmount),
                Priority = string.IsNullOrWhiteSpace(input.Priority) ? "Medium" : input.Priority,
                Status = requestedStatus,
                Notes = formattedNotes,
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            _db.WorkTasks.Add(task);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = task.Id }, task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/tasks failed");
            return StatusCode(500, new { message = "Failed to create task", error = ex.Message });
        }
    }

    // PUT /api/tasks/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkTask>> Update(int id, [FromBody] TaskDto input)
    {
        try
        {
            var task = await _db.WorkTasks.FindAsync(id);
            if (task is null)
                return NotFound(new { message = $"Task with ID {id} not found." });
            var accessibleProjectIds = await ProjectAccessPolicy.GetAccessibleProjectIdsAsync(_db, User);
            if (!accessibleProjectIds.Contains(task.ProjectId))
                return NotFound(new { message = $"Task with ID {id} not found." });

            if (string.IsNullOrWhiteSpace(input.TaskName))
                return BadRequest(new { message = "TaskName is required." });

            var targetProjectId = input.ProjectId > 0 ? input.ProjectId : task.ProjectId;
            if (targetProjectId != task.ProjectId)
                return BadRequest(new { message = "A task cannot be moved to another project." });

            var project = await _db.WorkProjects
                .Include(p => p.Milestones)
                .Include(p => p.Tasks)
                .Include(p => p.Invoices)
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(p => p.Id == targetProjectId);

            if (project == null)
                return BadRequest(new { message = $"Project with ID {targetProjectId} does not exist." });
            if (!ProjectAccessPolicy.CanAccess(_db, User, project))
                return NotFound(new { message = $"Project with ID {targetProjectId} does not exist." });

            task.ProjectId = targetProjectId;

            int? targetMsId = (input.MilestoneId.HasValue && input.MilestoneId.Value > 0) ? input.MilestoneId : null;
            WorkMilestone? parentMilestone = targetMsId.HasValue ? project.Milestones.FirstOrDefault(m => m.Id == targetMsId.Value) : null;
            task.MilestoneId = targetMsId;

            var scopeTasks = parentMilestone != null
                ? project.Tasks.Where(t => t.MilestoneId == parentMilestone.Id).OrderBy(t => t.Id).ToList()
                : project.Tasks.Where(t => !t.MilestoneId.HasValue).OrderBy(t => t.Id).ToList();

            bool hasDep = input.HasDependency ?? (input.DependencyTaskId.HasValue && input.DependencyTaskId.Value > 0);
            var depInfo = WorkDependencyHelper.EvaluateTask(
                task, 
                scopeTasks, 
                parentMilestone, 
                project.Milestones, 
                project.Invoices, 
                project.Tasks, 
                hasDep, 
                input.DependencyTaskId);

            var newStatus = string.IsNullOrWhiteSpace(input.Status) ? task.Status : input.Status;
            if (depInfo.IsLocked && (newStatus == "In Progress" || newStatus == "Completed"))
            {
                return BadRequest(new
                {
                    message = $"Cannot set task to '{newStatus}'. {depInfo.LockReason}",
                    lockReason = depInfo.LockReason,
                    isLocked = true
                });
            }

            var formattedNotes = WorkDependencyHelper.FormatTaskNotesWithDependency(input.Notes, hasDep, input.DependencyTaskId);

            task.TaskName = input.TaskName.Trim();
            task.Description = input.Description?.Trim() ?? "";
            task.AssignedMember = input.AssignedMember?.Trim() ?? "";
            task.StartDate = input.StartDate?.Trim() ?? "";
            task.DueDate = input.DueDate?.Trim() ?? "";
            task.TaskAmount = Math.Max(0, input.TaskAmount);
            task.CompletedAmount = depInfo.IsLocked ? 0 : Math.Max(0, input.CompletedAmount);
            task.Priority = string.IsNullOrWhiteSpace(input.Priority) ? task.Priority : input.Priority;
            task.Status = newStatus;
            task.Notes = formattedNotes;

            await _db.SaveChangesAsync();

            return Ok(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /api/tasks/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to update task", error = ex.Message });
        }
    }

    // DELETE /api/tasks/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var task = await _db.WorkTasks.FindAsync(id);
            if (task is null)
                return NotFound(new { message = $"Task with ID {id} not found." });
            var project = await _db.WorkProjects.FindAsync(task.ProjectId);
            if (project is null || !ProjectAccessPolicy.CanAccess(_db, User, project))
                return NotFound(new { message = $"Task with ID {id} not found." });

            _db.WorkTasks.Remove(task);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Task deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /api/tasks/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to delete task", error = ex.Message });
        }
    }
}

public class TaskDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? MilestoneId { get; set; }
    public string? TaskCode { get; set; }
    public string TaskName { get; set; } = "";
    public string? Description { get; set; } = "";
    public string? AssignedMember { get; set; } = "";
    public string? StartDate { get; set; } = "";
    public string? DueDate { get; set; } = "";
    public decimal TaskAmount { get; set; }
    public decimal CompletedAmount { get; set; }
    public string? Priority { get; set; } = "Medium";
    public string? Status { get; set; } = "Pending";
    public string? Notes { get; set; } = "";
    public bool? HasDependency { get; set; }
    public int? DependencyTaskId { get; set; }
}

public class TaskResponseDto : TaskDto
{
    public string ProjectName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string MilestoneName { get; set; } = "";
    public string MilestoneCode { get; set; } = "";
    public decimal PendingAmount { get; set; }
    public int ProgressPercent { get; set; }
    public string CreatedDate { get; set; } = "";

    // Dependency Properties
    public int SequenceIndex { get; set; } = 1;
    public int TotalTasks { get; set; } = 1;
    public string DependencyType { get; set; } = "Strict Finish-to-Start (Sequential Completion Required)";
    public bool IsLocked { get; set; } = false;
    public string LockReason { get; set; } = "";
    public int? PrerequisiteTaskId { get; set; }
    public string? PrerequisiteTaskCode { get; set; } = "";
    public string? PrerequisiteTaskName { get; set; } = "";
    public string? PrerequisiteStatus { get; set; } = "";
    public bool PrerequisiteCompleted { get; set; } = true;
    public bool ParentMilestoneLocked { get; set; } = false;
    public string? ParentMilestoneCode { get; set; } = "";
}
