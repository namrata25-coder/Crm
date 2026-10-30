using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class WorkController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration? _configuration;

    public WorkController(AppDbContext db, IConfiguration? configuration = null)
    {
        _db = db;
        _configuration = configuration;
    }

    private bool CheckPermission(string module, string action = "View")
    {
        if (User.IsInRole("Admin") || User.IsInRole("Manager")) return true;

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        if (currentUser != null && (string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(currentUser.Role, "Manager", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return PermissionChecker.HasModulePermission(_db, currentUser, module, action);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

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

    private string NextInvoiceNumber()
    {
        var codes = _db.WorkInvoices.Select(i => i.InvoiceNumber).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => { var n = c!.StartsWith("INV-", StringComparison.OrdinalIgnoreCase) ? c.Substring(4) : c; return int.TryParse(n, out var v) ? v : 2000; })
            .DefaultIfEmpty(2000).Max();
        int next = Math.Max(max + 1, 2001);
        while (codes.Any(c => string.Equals(c, $"INV-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"INV-{next}";
    }

    private string NextExpenseCode()
    {
        var codes = _db.WorkExpenses.Select(e => e.ExpenseCode).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => { var n = c!.StartsWith("EXP-", StringComparison.OrdinalIgnoreCase) ? c.Substring(4) : c; return int.TryParse(n, out var v) ? v : 1000; })
            .DefaultIfEmpty(1000).Max();
        int next = Math.Max(max + 1, 1001);
        while (codes.Any(c => string.Equals(c, $"EXP-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"EXP-{next}";
    }

    private List<string> GetEmployeesList()
    {
        var dbUsers = _db.Users.Where(u => !string.IsNullOrWhiteSpace(u.Name)).Select(u => u.Name!).ToList();
        if (dbUsers.Any()) return dbUsers.Distinct().OrderBy(n => n).ToList();
        return SettingsStore.Employees;
    }

    private async Task<List<WorkProject>> GetAccessibleProjectsAsync()
    {
        var projectIds = await ProjectAccessPolicy.GetAccessibleProjectIdsAsync(_db, User);
        return await _db.WorkProjects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToListAsync();
    }

    private async Task<bool> CanAccessProjectIdAsync(int projectId)
    {
        var project = await _db.WorkProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == projectId);
        return project is not null && ProjectAccessPolicy.CanAccess(_db, User, project);
    }

    private void SetWorkNav(string activePage)
    {
        ViewData["ActivePage"] = activePage;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  1. WORK → PROJECT PAGE
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Projects()
    {
        if (!CheckPermission("Project", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Projects.";
            return RedirectToAction("List", "Lead");
        }

        SetWorkNav("WorkProject");
        ViewData["Title"] = "Projects";

        // Role-based UI permission flags
        ViewData["CanCreateProject"] = CheckPermission("Project", "Create");
        ViewData["CanEditProject"]   = CheckPermission("Project", "Edit");
        ViewData["CanDeleteProject"] = CheckPermission("Project", "Delete");

        var allProjects = await _db.WorkProjects
            .Include(p => p.Milestones)
                .ThenInclude(m => m.Tasks)
            .Include(p => p.Tasks)
            .Include(p => p.Invoices)
            .Include(p => p.Files)
            .Include(p => p.Timesheets)
            .Include(p => p.Expenses)
            .Include(p => p.WorkNotes)
            .OrderByDescending(p => p.Id)
            .ToListAsync();
        var projects = ProjectAccessPolicy.FilterAccessible(_db, User, allProjects);

        ViewData["Employees"] = GetEmployeesList();
        ViewData["Clients"]   = await _db.Leads
            .Select(l => new { l.LeadName, l.CompanyName })
            .Where(l => !string.IsNullOrWhiteSpace(l.LeadName))
            .Distinct()
            .ToListAsync();

        return View(projects);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PROJECT DETAILS (9 TABS: Milestone, Task, Invoice, Report, Member, File, Timesheet, Expenses, Note)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> ProjectDetails(int id, string? tab)
    {
        if (!CheckPermission("Project", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Project details.";
            return RedirectToAction(nameof(Projects));
        }

        var project = await _db.WorkProjects
            .Include(p => p.Milestones)
                .ThenInclude(m => m.Tasks)
            .Include(p => p.Tasks)
            .Include(p => p.Invoices)
            .Include(p => p.Files)
            .Include(p => p.Timesheets)
                .ThenInclude(t => t.Task)
            .Include(p => p.Expenses)
            .Include(p => p.WorkNotes)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project is null || !ProjectAccessPolicy.CanAccess(_db, User, project))
            return RedirectToAction(nameof(Projects));

        SetWorkNav("WorkProject");
        ViewData["Title"] = $"{project.ProjectName} · Project Details";
        ViewData["ActiveTab"] = string.IsNullOrWhiteSpace(tab) ? "milestone" : tab.ToLowerInvariant();
        ViewData["Employees"] = GetEmployeesList();

        // Role-based UI permission flags — Project
        ViewData["CanEditProject"]   = CheckPermission("Project", "Edit");
        ViewData["CanDeleteProject"] = CheckPermission("Project", "Delete");

        // Milestone
        ViewData["CanCreateMilestone"] = CheckPermission("Milestone", "Create");
        ViewData["CanEditMilestone"]   = CheckPermission("Milestone", "Edit");
        ViewData["CanDeleteMilestone"] = CheckPermission("Milestone", "Delete");
        ViewData["CanApproveMilestone"] = CheckPermission("Milestone", "Extra");

        // Task
        ViewData["CanCreateTask"] = CheckPermission("Task", "Create");
        ViewData["CanEditTask"]   = CheckPermission("Task", "Edit");
        ViewData["CanDeleteTask"] = CheckPermission("Task", "Delete");
        ViewData["CanAssignTask"] = CheckPermission("Task", "Extra");

        // Invoice
        ViewData["CanCreateInvoice"] = CheckPermission("Invoice", "Create");
        ViewData["CanEditInvoice"]   = CheckPermission("Invoice", "Edit");
        ViewData["CanDeleteInvoice"] = CheckPermission("Invoice", "Delete");

        // Payment
        ViewData["CanCreatePayment"] = CheckPermission("Payment", "Create");
        ViewData["CanEditPayment"]   = CheckPermission("Payment", "Edit");
        ViewData["CanDeletePayment"] = CheckPermission("Payment", "Delete");

        // Timesheet
        ViewData["CanCreateTimesheet"] = CheckPermission("Timesheet", "Create");
        ViewData["CanEditTimesheet"]   = CheckPermission("Timesheet", "Edit");
        ViewData["CanDeleteTimesheet"] = CheckPermission("Timesheet", "Delete");
        ViewData["CanApproveTimesheet"] = CheckPermission("Timesheet", "Extra");

        // Expense
        ViewData["CanCreateExpense"] = CheckPermission("Expenses", "Create");
        ViewData["CanEditExpense"]   = CheckPermission("Expenses", "Edit");
        ViewData["CanDeleteExpense"] = CheckPermission("Expenses", "Delete");
        ViewData["CanApproveExpense"] = CheckPermission("Expenses", "Extra");

        // File
        ViewData["CanUploadFile"]    = CheckPermission("File", "Create");
        ViewData["CanDeleteFile"]    = CheckPermission("File", "Delete");
        ViewData["CanDownloadFile"]  = CheckPermission("File", "Extra");

        // Note
        ViewData["CanCreateNote"] = CheckPermission("Note", "Create");
        ViewData["CanEditNote"]   = CheckPermission("Note", "Edit");
        ViewData["CanDeleteNote"] = CheckPermission("Note", "Delete");

        // Member
        ViewData["CanManageMember"] = CheckPermission("Member", "Create");

        return View(project);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PROJECT CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> SaveProject(
        int id, string projectName, string clientName, string clientCompany,
        string projectType, string description, string startDate, string deadline,
        string projectManager, string assignedMembers, decimal budget,
        string priority, string status, string notes)
    {
        var action = id == 0 ? "Create" : "Edit";
        if (!CheckPermission("Project", action))
        {
            TempData["ErrorMessage"] = $"Access Denied: You do not have permission to {action.ToLower()} projects.";
            return RedirectToAction(nameof(Projects));
        }

        var currentUser = PermissionChecker.GetCurrentUser(_db, User);
        var isAdmin = PermissionChecker.IsAdminOrManager(User, currentUser);
        if (!isAdmin && currentUser is null)
        {
            TempData["ErrorMessage"] = "Access denied. A valid user account is required.";
            return RedirectToAction(nameof(Projects));
        }

        if (id == 0)
        {
            var p = new WorkProject
            {
                ProjectCode     = NextProjectCode(),
                ProjectName     = projectName?.Trim() ?? "New Project",
                ClientName      = clientName?.Trim() ?? "",
                ClientCompany   = clientCompany?.Trim() ?? "",
                ProjectType     = string.IsNullOrWhiteSpace(projectType) ? "Direct Project" : projectType,
                Description     = description?.Trim() ?? "",
                StartDate       = startDate?.Trim() ?? "",
                Deadline        = deadline?.Trim() ?? "",
                ProjectManager  = isAdmin ? projectManager?.Trim() ?? "" : currentUser!.Name?.Trim() ?? "",
                AssignedMembers = assignedMembers?.Trim() ?? "",
                Budget          = Math.Max(0, budget),
                Priority        = string.IsNullOrWhiteSpace(priority) ? "Medium" : priority,
                Status          = string.IsNullOrWhiteSpace(status) ? "Active" : status,
                Notes           = notes?.Trim() ?? "",
                CreatedDate     = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };
            _db.WorkProjects.Add(p);
        }
        else
        {
            var p = await _db.WorkProjects.FindAsync(id);
            if (p is not null && ProjectAccessPolicy.CanAccess(_db, User, p))
            {
                p.ProjectName     = projectName?.Trim() ?? p.ProjectName;
                p.ClientName      = clientName?.Trim() ?? "";
                p.ClientCompany   = clientCompany?.Trim() ?? "";
                p.ProjectType     = string.IsNullOrWhiteSpace(projectType) ? p.ProjectType : projectType;
                p.Description     = description?.Trim() ?? "";
                p.StartDate       = startDate?.Trim() ?? "";
                p.Deadline        = deadline?.Trim() ?? "";
                p.ProjectManager  = isAdmin ? projectManager?.Trim() ?? "" : currentUser!.Name?.Trim() ?? "";
                p.AssignedMembers = assignedMembers?.Trim() ?? "";
                p.Budget          = Math.Max(0, budget);
                p.Priority        = string.IsNullOrWhiteSpace(priority) ? p.Priority : priority;
                p.Status          = string.IsNullOrWhiteSpace(status) ? p.Status : status;
                p.Notes           = notes?.Trim() ?? "";
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Projects));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteProject(int id)
    {
        if (!CheckPermission("Project", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete projects.";
            return RedirectToAction(nameof(Projects));
        }
        var p = await _db.WorkProjects
            .Include(x => x.Files)
            .Include(x => x.Expenses)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is not null && ProjectAccessPolicy.CanAccess(_db, User, p))
        {
            foreach (var f in p.Files)
            {
                WorkAttachmentService.Delete(f.FilePath);
            }
            foreach (var e in p.Expenses)
            {
                WorkAttachmentService.Delete(e.ReceiptFilePath);
            }

            _db.WorkProjects.Remove(p);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Projects));
    }

    [HttpPost]
    public async Task<IActionResult> SaveProjectMembers(int projectId, string? projectManager, string? assignedMembers)
    {
        var project = await _db.WorkProjects.FindAsync(projectId);
        if (project is not null && ProjectAccessPolicy.CanAccess(_db, User, project))
        {
            var currentUser = PermissionChecker.GetCurrentUser(_db, User);
            project.ProjectManager = PermissionChecker.IsAdminOrManager(User, currentUser)
                ? projectManager?.Trim() ?? ""
                : currentUser?.Name?.Trim() ?? project.ProjectManager;
            project.AssignedMembers = assignedMembers?.Trim() ?? "";
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "member" });
    }

    [HttpPost]
    public async Task<IActionResult> AddProjectMember(int projectId, string memberName, string? role)
    {
        var project = await _db.WorkProjects.FindAsync(projectId);
        if (project is not null && ProjectAccessPolicy.CanAccess(_db, User, project) && !string.IsNullOrWhiteSpace(memberName))
        {
            var cleanMember = memberName.Trim();
            if (string.Equals(role, "Project Manager", StringComparison.OrdinalIgnoreCase))
            {
                project.ProjectManager = cleanMember;
            }
            else
            {
                var existing = (project.AssignedMembers ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                if (!existing.Contains(cleanMember, StringComparer.OrdinalIgnoreCase))
                {
                    existing.Add(cleanMember);
                    project.AssignedMembers = string.Join(", ", existing);
                }
            }
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "member" });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveProjectMember(int projectId, string memberName)
    {
        var project = await _db.WorkProjects.FindAsync(projectId);
        if (project is not null && ProjectAccessPolicy.CanAccess(_db, User, project) && !string.IsNullOrWhiteSpace(memberName))
        {
            var cleanMember = memberName.Trim();
            if (string.Equals(project.ProjectManager, cleanMember, StringComparison.OrdinalIgnoreCase))
            {
                project.ProjectManager = "";
            }
            var existing = (project.AssignedMembers ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            existing.RemoveAll(m => string.Equals(m, cleanMember, StringComparison.OrdinalIgnoreCase));
            project.AssignedMembers = string.Join(", ", existing);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "member" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  2. WORK → MILESTONE PAGE & MILESTONE CRUD
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Milestones()
    {
        if (!CheckPermission("Milestone", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Milestones.";
            return RedirectToAction("List", "Lead");
        }

        SetWorkNav("WorkMilestone");
        ViewData["Title"] = "Milestones";

        var milestones = await _db.WorkMilestones
            .Include(m => m.Project)
            .Include(m => m.Tasks)
            .OrderByDescending(m => m.Id)
            .ToListAsync();

        var projects = (await GetAccessibleProjectsAsync())
            .OrderBy(p => p.ProjectName)
            .ToList();
        var accessibleProjectIds = projects.Select(p => p.Id).ToHashSet();
        milestones = milestones.Where(m => accessibleProjectIds.Contains(m.ProjectId)).ToList();

        ViewData["Projects"]  = projects;
        ViewData["Employees"] = GetEmployeesList();
        return View(milestones);
    }

    [HttpPost]
    public async Task<IActionResult> SaveMilestone(
        int milestoneId, int projectId, string milestoneName, string description,
        string startDate, string dueDate, decimal amount, decimal completedAmount,
        string assignedMembers, string priority, string status, string notes,
        string? returnAction, string? returnTab)
    {
        var action = milestoneId == 0 ? "Create" : "Edit";
        if (!CheckPermission("Milestone", action))
        {
            TempData["ErrorMessage"] = $"Access Denied: You do not have permission to {action.ToLower()} milestones.";
            return RedirectToAction(nameof(Milestones));
        }

        amount = Math.Max(0, amount);
        completedAmount = Math.Max(0, Math.Min(completedAmount, amount > 0 ? amount : decimal.MaxValue));

        var project = await _db.WorkProjects
            .Include(p => p.Milestones)
            .Include(p => p.Invoices)
                .ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        var existingMilestone = milestoneId > 0 ? await _db.WorkMilestones.FindAsync(milestoneId) : null;
        if (project is null || !ProjectAccessPolicy.CanAccess(_db, User, project) ||
            (milestoneId > 0 && (existingMilestone is null ||
                                existingMilestone.ProjectId != projectId ||
                                !await CanAccessProjectIdAsync(existingMilestone.ProjectId))))
        {
            TempData["ErrorMessage"] = "Access denied. You can only manage milestones in your assigned projects.";
            return RedirectToAction(nameof(Milestones));
        }

        var evalTarget = existingMilestone ?? new WorkMilestone { Id = 0, ProjectId = projectId };
        var depInfo = project != null
            ? WorkDependencyHelper.EvaluateMilestone(evalTarget, project.Milestones, project.Invoices)
            : new MilestoneDependencyInfo();

        var finalStatus = string.IsNullOrWhiteSpace(status) ? "Pending" : status;
        if (depInfo.IsLocked && (finalStatus == "In Progress" || finalStatus == "Completed"))
        {
            finalStatus = "Pending";
            completedAmount = 0;
        }

        if (milestoneId == 0)
        {
            var m = new WorkMilestone
            {
                ProjectId       = projectId,
                MilestoneCode   = NextMilestoneCode(),
                MilestoneName   = milestoneName?.Trim() ?? "New Milestone",
                Description     = description?.Trim() ?? "",
                StartDate       = startDate?.Trim() ?? "",
                DueDate         = dueDate?.Trim() ?? "",
                Amount          = amount,
                CompletedAmount = depInfo.IsLocked ? 0 : completedAmount,
                AssignedMembers = assignedMembers?.Trim() ?? "",
                Priority        = string.IsNullOrWhiteSpace(priority) ? "Medium" : priority,
                Status          = finalStatus,
                Notes           = notes?.Trim() ?? ""
            };
            _db.WorkMilestones.Add(m);
        }
        else
        {
            var m = await _db.WorkMilestones.FindAsync(milestoneId);
            if (m is not null)
            {
                m.ProjectId       = projectId;
                m.MilestoneName   = milestoneName?.Trim() ?? m.MilestoneName;
                m.Description     = description?.Trim() ?? "";
                m.StartDate       = startDate?.Trim() ?? "";
                m.DueDate         = dueDate?.Trim() ?? "";
                m.Amount          = amount;
                m.CompletedAmount = depInfo.IsLocked ? 0 : completedAmount;
                m.AssignedMembers = assignedMembers?.Trim() ?? "";
                m.Priority        = string.IsNullOrWhiteSpace(priority) ? m.Priority : priority;
                m.Status          = finalStatus;
                m.Notes           = notes?.Trim() ?? "";
            }
        }
        await _db.SaveChangesAsync();

        if (string.Equals(returnAction, "Milestones", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Milestones));
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = returnTab ?? "milestone" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMilestone(int id, int? projectId, string? returnAction)
    {
        if (!CheckPermission("Milestone", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete milestones.";
            return RedirectToAction(nameof(Milestones));
        }

        var m = await _db.WorkMilestones.Include(x => x.Tasks).FirstOrDefaultAsync(x => x.Id == id);
        if (m is not null && await CanAccessProjectIdAsync(m.ProjectId))
        {
            _db.WorkTasks.RemoveRange(m.Tasks);
            _db.WorkMilestones.Remove(m);
            await _db.SaveChangesAsync();
        }

        if (string.Equals(returnAction, "Milestones", StringComparison.OrdinalIgnoreCase) || !projectId.HasValue)
        {
            return RedirectToAction(nameof(Milestones));
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId.Value, tab = "milestone" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  3. WORK → TASK PAGE & TASK CRUD
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Tasks(int? projectId, int? milestoneId)
    {
        if (!CheckPermission("Task", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Tasks.";
            return RedirectToAction("List", "Lead");
        }

        SetWorkNav("WorkTask");
        ViewData["Title"] = "Tasks";

        var projects = (await GetAccessibleProjectsAsync())
            .OrderBy(p => p.ProjectName)
            .ToList();
        var accessibleProjectIds = projects.Select(p => p.Id).ToHashSet();
        var query = _db.WorkTasks
            .Include(t => t.Project)
            .Include(t => t.Milestone)
            .Where(t => accessibleProjectIds.Contains(t.ProjectId))
            .AsQueryable();

        if (projectId.HasValue && projectId.Value > 0)
            query = query.Where(t => t.ProjectId == projectId.Value);

        if (milestoneId.HasValue && milestoneId.Value > 0)
            query = query.Where(t => t.MilestoneId == milestoneId.Value);

        var tasks = await query.OrderByDescending(t => t.Id).ToListAsync();

        ViewData["Projects"]   = projects;
        ViewData["Milestones"] = await _db.WorkMilestones
            .Where(m => accessibleProjectIds.Contains(m.ProjectId))
            .OrderBy(m => m.MilestoneName)
            .ToListAsync();
        ViewData["Employees"]  = GetEmployeesList();
        ViewData["FilterProjectId"]   = projectId;
        ViewData["FilterMilestoneId"] = milestoneId;

        return View(tasks);
    }

    [HttpPost]
    public async Task<IActionResult> SaveTask(
        int taskId, int projectId, int? milestoneId, string taskName, string description,
        string assignedMember, string startDate, string dueDate, decimal taskAmount,
        decimal completedAmount, string priority, string status, string notes,
        string? returnAction, string? returnTab, bool? hasDependency, int? dependencyTaskId)
    {
        var action = taskId == 0 ? "Create" : "Edit";
        if (!CheckPermission("Task", action))
        {
            TempData["ErrorMessage"] = $"Access Denied: You do not have permission to {action.ToLower()} tasks.";
            return RedirectToAction(nameof(Tasks));
        }
        taskAmount = Math.Max(0, taskAmount);
        completedAmount = Math.Max(0, Math.Min(completedAmount, taskAmount > 0 ? taskAmount : decimal.MaxValue));
        if (milestoneId.HasValue && milestoneId.Value <= 0) milestoneId = null;

        var project = await _db.WorkProjects
            .Include(p => p.Milestones)
            .Include(p => p.Tasks)
            .Include(p => p.Invoices)
                .ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null || !ProjectAccessPolicy.CanAccess(_db, User, project))
        {
            TempData["ErrorMessage"] = "Access denied. You can only manage tasks in your assigned projects.";
            return RedirectToAction(nameof(Tasks));
        }

        WorkMilestone? parentMilestone = null;
        if (milestoneId.HasValue && project != null)
        {
            parentMilestone = project.Milestones.FirstOrDefault(m => m.Id == milestoneId.Value);
            if (parentMilestone is null)
            {
                TempData["ErrorMessage"] = "The selected milestone does not belong to this project.";
                return RedirectToAction(nameof(Tasks));
            }
        }

        var scopeTasks = (project != null)
            ? (parentMilestone != null
                ? project.Tasks.Where(t => t.MilestoneId == parentMilestone.Id).OrderBy(t => t.Id).ToList()
                : project.Tasks.Where(t => !t.MilestoneId.HasValue).OrderBy(t => t.Id).ToList())
            : new List<WorkTask>();

        bool isDep = hasDependency.GetValueOrDefault() || (dependencyTaskId.HasValue && dependencyTaskId.Value > 0);
        var evalTarget = taskId > 0 ? (await _db.WorkTasks.FindAsync(taskId) ?? new WorkTask { Id = taskId, ProjectId = projectId, MilestoneId = milestoneId }) : new WorkTask { Id = 0, ProjectId = projectId, MilestoneId = milestoneId };

        var depInfo = (project != null)
            ? WorkDependencyHelper.EvaluateTask(evalTarget, scopeTasks, parentMilestone, project.Milestones, project.Invoices, project.Tasks, isDep, dependencyTaskId)
            : new TaskDependencyInfo();

        var finalStatus = string.IsNullOrWhiteSpace(status) ? "Pending" : status;
        if (depInfo.IsLocked && (finalStatus == "In Progress" || finalStatus == "Completed"))
        {
            finalStatus = "Pending";
            completedAmount = 0;
        }

        var formattedNotes = WorkDependencyHelper.FormatTaskNotesWithDependency(notes, isDep, dependencyTaskId);

        if (taskId == 0)
        {
            var t = new WorkTask
            {
                ProjectId       = projectId,
                MilestoneId     = milestoneId,
                TaskCode        = NextTaskCode(),
                TaskName        = taskName?.Trim() ?? "New Task",
                Description     = description?.Trim() ?? "",
                AssignedMember  = assignedMember?.Trim() ?? "",
                StartDate       = startDate?.Trim() ?? "",
                DueDate         = dueDate?.Trim() ?? "",
                TaskAmount      = taskAmount,
                CompletedAmount = depInfo.IsLocked ? 0 : completedAmount,
                Priority        = string.IsNullOrWhiteSpace(priority) ? "Medium" : priority,
                Status          = finalStatus,
                Notes           = formattedNotes,
                CreatedDate     = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };
            _db.WorkTasks.Add(t);
        }
        else
        {
            var t = await _db.WorkTasks.FindAsync(taskId);
            if (t is not null && t.ProjectId == projectId && await CanAccessProjectIdAsync(t.ProjectId))
            {
                t.ProjectId       = projectId;
                t.MilestoneId     = milestoneId;
                t.TaskName        = taskName?.Trim() ?? t.TaskName;
                t.Description     = description?.Trim() ?? "";
                t.AssignedMember  = assignedMember?.Trim() ?? "";
                t.StartDate       = startDate?.Trim() ?? "";
                t.DueDate         = dueDate?.Trim() ?? "";
                t.TaskAmount      = taskAmount;
                t.CompletedAmount = depInfo.IsLocked ? 0 : completedAmount;
                t.Priority        = string.IsNullOrWhiteSpace(priority) ? t.Priority : priority;
                t.Status          = finalStatus;
                t.Notes           = formattedNotes;
            }
        }
        await _db.SaveChangesAsync();

        if (string.Equals(returnAction, "Tasks", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Tasks));
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = returnTab ?? "task" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTask(int id, int? projectId, string? returnAction, string? returnTab)
    {
        if (!CheckPermission("Task", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete tasks.";
            return RedirectToAction(nameof(Tasks));
        }

        var t = await _db.WorkTasks.FindAsync(id);
        if (t is not null && await CanAccessProjectIdAsync(t.ProjectId))
        {
            _db.WorkTasks.Remove(t);
            await _db.SaveChangesAsync();
        }

        if (string.Equals(returnAction, "Tasks", StringComparison.OrdinalIgnoreCase) || !projectId.HasValue)
        {
            return RedirectToAction(nameof(Tasks));
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId.Value, tab = returnTab ?? "task" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  4. INVOICE CRUD (Under Project)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> SaveInvoice(
        int invoiceId, int projectId, int? milestoneId, string invoiceDate, string dueDate,
        decimal amount, decimal taxRate, decimal discount, string paymentStatus,
        string paymentDate, string notes)
    {
        var project = await _db.WorkProjects.FindAsync(projectId);
        if (project is null)
            return RedirectToAction(nameof(Projects));

        bool isTaskBased = string.Equals(project.ProjectType, "Task Based Project", StringComparison.OrdinalIgnoreCase) ||
                           (project.ProjectType != null && project.ProjectType.Contains("Task", StringComparison.OrdinalIgnoreCase));

        bool isMilestoneBased = string.Equals(project.ProjectType, "Milestone Based Project", StringComparison.OrdinalIgnoreCase) ||
                               (project.ProjectType != null && project.ProjectType.Contains("Milestone", StringComparison.OrdinalIgnoreCase));

        if (invoiceId == 0)
        {
            if (isTaskBased)
            {
                TempData["Error"] = "Task Based projects invoices must be created from Finance > Invoice.";
                return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "invoice" });
            }

            if (isMilestoneBased)
            {
                if (!milestoneId.HasValue || milestoneId.Value <= 0)
                {
                    TempData["Error"] = "Milestone is required for Milestone Based projects.";
                    return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "invoice" });
                }
            }
            else
            {
                // Direct Project
                milestoneId = null;
            }
        }

        WorkMilestone? milestone = null;
        if (milestoneId.HasValue && milestoneId.Value > 0)
        {
            milestone = await _db.WorkMilestones
                .Include(m => m.Tasks)
                .FirstOrDefaultAsync(m => m.Id == milestoneId.Value && m.ProjectId == projectId);
            if (milestone is null)
            {
                TempData["Error"] = "The selected milestone does not belong to this project.";
                return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "invoice" });
            }

            // Backend rule: 1 Milestone = 1 Invoice.
            var duplicate = await _db.WorkInvoices.AnyAsync(i => i.MilestoneId == milestoneId.Value && i.Id != invoiceId);
            if (duplicate)
            {
                TempData["Error"] = $"An invoice already exists for milestone '{milestone.MilestoneName}'. Duplicate milestone invoices are not allowed.";
                return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "invoice" });
            }

            amount = milestone.Amount > 0 ? milestone.Amount : milestone.EffectiveAmount;
        }

        amount = Math.Max(0, amount);
        discount = Math.Max(0, discount);
        if (discount > amount) discount = amount;

        var taxableAmount = Math.Max(0, amount - discount);
        taxRate = Math.Max(0, taxRate);
        var tax = Math.Round(taxableAmount * (taxRate / 100m), 2);
        var grandTotal = taxableAmount + tax;

        if (invoiceId == 0)
        {
            var inv = new WorkInvoice
            {
                ProjectId     = projectId,
                MilestoneId   = milestoneId > 0 ? milestoneId : null,
                InvoiceNumber = NextInvoiceNumber(),
                ClientName    = project.ClientName ?? "",
                ClientCompany = project.ClientCompany ?? "",
                InvoiceDate   = invoiceDate?.Trim() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                DueDate       = dueDate?.Trim() ?? "",
                Amount        = amount,
                TaxRate       = taxRate,
                Tax           = tax,
                Discount      = discount,
                GrandTotal    = grandTotal,
                PaymentStatus = string.IsNullOrWhiteSpace(paymentStatus) ? "Unpaid" : paymentStatus,
                PaymentDate   = paymentDate?.Trim() ?? "",
                Notes         = notes?.Trim() ?? "",
                CreatedDate   = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            };
            _db.WorkInvoices.Add(inv);
        }
        else
        {
            var inv = await _db.WorkInvoices.FindAsync(invoiceId);
            if (inv is not null)
            {
                inv.MilestoneId   = milestoneId > 0 ? milestoneId : null;
                inv.InvoiceDate   = invoiceDate?.Trim() ?? inv.InvoiceDate;
                inv.DueDate       = dueDate?.Trim() ?? "";
                inv.Amount        = amount;
                inv.TaxRate       = taxRate;
                inv.Tax           = tax;
                inv.Discount      = discount;
                inv.GrandTotal    = grandTotal;
                inv.PaymentStatus = string.IsNullOrWhiteSpace(paymentStatus) ? inv.PaymentStatus : paymentStatus;
                inv.PaymentDate   = paymentDate?.Trim() ?? "";
                inv.Notes         = notes?.Trim() ?? "";
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "invoice" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteInvoice(int id, int projectId)
    {
        var inv = await _db.WorkInvoices.FindAsync(id);
        if (inv is not null)
        {
            _db.WorkInvoices.Remove(inv);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "invoice" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  5. FILE MANAGEMENT (Project Files & Attachments)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> UploadFile(int projectId, string? description, string? uploadedBy, IFormFile? file)
    {
        if (file is not null && file.Length > 0)
        {
            var (path, originalName, size, type, error) = await WorkAttachmentService.SaveAsync(file, "files");
            if (string.IsNullOrEmpty(error))
            {
                var workFile = new WorkFile
                {
                    ProjectId        = projectId,
                    FileName         = Path.GetFileName(path),
                    OriginalFileName = originalName,
                    FilePath         = path,
                    FileSizeBytes    = size,
                    FileType         = type,
                    UploadedBy       = string.IsNullOrWhiteSpace(uploadedBy) ? "Ananya Sharma" : uploadedBy.Trim(),
                    UploadedDate     = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
                    Description      = description?.Trim() ?? ""
                };
                _db.WorkFiles.Add(workFile);
                await _db.SaveChangesAsync();
            }
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "file" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteFile(int id, int projectId)
    {
        var f = await _db.WorkFiles.FindAsync(id);
        if (f is not null)
        {
            WorkAttachmentService.Delete(f.FilePath);
            _db.WorkFiles.Remove(f);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "file" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  6. TIMESHEET (Time Tracking)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> SaveTimesheet(
        int timesheetId, int projectId, int? taskId, string memberName,
        string logDate, decimal hoursWorked, string? startTime, string? endTime,
        decimal hourlyRate, string billingStatus, string? description, string? notes)
    {
        hoursWorked = Math.Max(0, hoursWorked);
        hourlyRate = Math.Max(0, hourlyRate);
        var totalCost = hoursWorked * hourlyRate;
        if (taskId.HasValue && taskId.Value <= 0) taskId = null;

        if (timesheetId == 0)
        {
            var ts = new WorkTimesheet
            {
                ProjectId     = projectId,
                TaskId        = taskId,
                MemberName    = memberName?.Trim() ?? "Unassigned",
                LogDate       = string.IsNullOrWhiteSpace(logDate) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : logDate.Trim(),
                HoursWorked   = hoursWorked,
                StartTime     = startTime?.Trim() ?? "",
                EndTime       = endTime?.Trim() ?? "",
                HourlyRate    = hourlyRate,
                TotalCost     = totalCost,
                BillingStatus = string.IsNullOrWhiteSpace(billingStatus) ? "Billable" : billingStatus,
                Description   = description?.Trim() ?? "",
                Notes         = notes?.Trim() ?? "",
                CreatedDate   = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };
            _db.WorkTimesheets.Add(ts);
        }
        else
        {
            var ts = await _db.WorkTimesheets.FindAsync(timesheetId);
            if (ts is not null)
            {
                ts.TaskId        = taskId;
                ts.MemberName    = memberName?.Trim() ?? ts.MemberName;
                ts.LogDate       = string.IsNullOrWhiteSpace(logDate) ? ts.LogDate : logDate.Trim();
                ts.HoursWorked   = hoursWorked;
                ts.StartTime     = startTime?.Trim() ?? "";
                ts.EndTime       = endTime?.Trim() ?? "";
                ts.HourlyRate    = hourlyRate;
                ts.TotalCost     = totalCost;
                ts.BillingStatus = string.IsNullOrWhiteSpace(billingStatus) ? ts.BillingStatus : billingStatus;
                ts.Description   = description?.Trim() ?? "";
                ts.Notes         = notes?.Trim() ?? "";
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "timesheet" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTimesheet(int id, int projectId)
    {
        var ts = await _db.WorkTimesheets.FindAsync(id);
        if (ts is not null)
        {
            _db.WorkTimesheets.Remove(ts);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "timesheet" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  7. EXPENSES (Project Expenses)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> SaveExpense(
        int expenseId, int projectId, string expenseName, string category,
        decimal amount, decimal taxAmount, string expenseDate, string paymentMethod,
        string status, string paidBy, string notes, IFormFile? receipt)
    {
        amount = Math.Max(0, amount);
        taxAmount = Math.Max(0, taxAmount);
        var totalAmount = amount + taxAmount;

        string? receiptPath = null;
        string? receiptName = null;
        if (receipt is not null && receipt.Length > 0)
        {
            var (path, name, _, _, error) = await WorkAttachmentService.SaveAsync(receipt, "receipts");
            if (string.IsNullOrEmpty(error))
            {
                receiptPath = path;
                receiptName = name;
            }
        }

        if (expenseId == 0)
        {
            var exp = new WorkExpense
            {
                ProjectId       = projectId,
                ExpenseCode     = NextExpenseCode(),
                ExpenseName     = expenseName?.Trim() ?? "New Expense",
                Category        = string.IsNullOrWhiteSpace(category) ? "Travel" : category,
                Amount          = amount,
                TaxAmount       = taxAmount,
                TotalAmount     = totalAmount,
                ExpenseDate     = string.IsNullOrWhiteSpace(expenseDate) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : expenseDate.Trim(),
                PaymentMethod   = string.IsNullOrWhiteSpace(paymentMethod) ? "Credit Card" : paymentMethod,
                Status          = string.IsNullOrWhiteSpace(status) ? "Pending" : status,
                PaidBy          = paidBy?.Trim() ?? "",
                ReceiptFileName = receiptName ?? "",
                ReceiptFilePath = receiptPath ?? "",
                Notes           = notes?.Trim() ?? "",
                CreatedDate     = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };
            _db.WorkExpenses.Add(exp);
        }
        else
        {
            var exp = await _db.WorkExpenses.FindAsync(expenseId);
            if (exp is not null)
            {
                exp.ExpenseName   = expenseName?.Trim() ?? exp.ExpenseName;
                exp.Category      = string.IsNullOrWhiteSpace(category) ? exp.Category : category;
                exp.Amount        = amount;
                exp.TaxAmount     = taxAmount;
                exp.TotalAmount   = totalAmount;
                exp.ExpenseDate   = string.IsNullOrWhiteSpace(expenseDate) ? exp.ExpenseDate : expenseDate.Trim();
                exp.PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? exp.PaymentMethod : paymentMethod;
                exp.Status        = string.IsNullOrWhiteSpace(status) ? exp.Status : status;
                exp.PaidBy        = paidBy?.Trim() ?? "";
                exp.Notes         = notes?.Trim() ?? "";

                if (!string.IsNullOrEmpty(receiptPath))
                {
                    WorkAttachmentService.Delete(exp.ReceiptFilePath);
                    exp.ReceiptFilePath = receiptPath;
                    exp.ReceiptFileName = receiptName ?? "";
                }
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "expenses" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteExpense(int id, int projectId)
    {
        var exp = await _db.WorkExpenses.FindAsync(id);
        if (exp is not null)
        {
            WorkAttachmentService.Delete(exp.ReceiptFilePath);
            _db.WorkExpenses.Remove(exp);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "expenses" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  8. NOTES (Project Notes & Discussion)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> SaveNote(
        int noteId, int projectId, string title, string content,
        string category, bool isPinned, string colorTag, string createdBy)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        if (noteId == 0)
        {
            var note = new WorkNote
            {
                ProjectId        = projectId,
                Title            = title?.Trim() ?? "Untitled Note",
                Content          = content?.Trim() ?? "",
                Category         = string.IsNullOrWhiteSpace(category) ? "General" : category,
                IsPinned         = isPinned,
                ColorTag         = string.IsNullOrWhiteSpace(colorTag) ? "blue" : colorTag,
                CreatedBy        = string.IsNullOrWhiteSpace(createdBy) ? "Ananya Sharma" : createdBy.Trim(),
                CreatedDate      = now,
                LastModifiedDate = now
            };
            _db.WorkNotes.Add(note);
        }
        else
        {
            var note = await _db.WorkNotes.FindAsync(noteId);
            if (note is not null)
            {
                note.Title            = title?.Trim() ?? note.Title;
                note.Content          = content?.Trim() ?? "";
                note.Category         = string.IsNullOrWhiteSpace(category) ? note.Category : category;
                note.IsPinned         = isPinned;
                note.ColorTag         = string.IsNullOrWhiteSpace(colorTag) ? note.ColorTag : colorTag;
                note.LastModifiedDate = now;
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "note" });
    }

    [HttpPost]
    public async Task<IActionResult> TogglePinNote(int id, int projectId)
    {
        var note = await _db.WorkNotes.FindAsync(id);
        if (note is not null)
        {
            note.IsPinned = !note.IsPinned;
            note.LastModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "note" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteNote(int id, int projectId)
    {
        var note = await _db.WorkNotes.FindAsync(id);
        if (note is not null)
        {
            _db.WorkNotes.Remove(note);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ProjectDetails), new { id = projectId, tab = "note" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  9. WORK → REPORT PAGE
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Reports(int? projectId)
    {
        if (!CheckPermission("Report", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Reports.";
            return RedirectToAction("List", "Lead");
        }

        SetWorkNav("WorkReport");
        ViewData["Title"] = "Work Reports";

        var allProjects = await _db.WorkProjects
            .Include(p => p.Milestones)
                .ThenInclude(m => m.Tasks)
            .Include(p => p.Tasks)
            .Include(p => p.Invoices)
            .Include(p => p.Files)
            .Include(p => p.Timesheets)
            .Include(p => p.Expenses)
            .Include(p => p.WorkNotes)
            .OrderBy(p => p.ProjectName)
            .ToListAsync();
        var projects = ProjectAccessPolicy.FilterAccessible(_db, User, allProjects);

        ViewData["Projects"] = projects;

        WorkProject? selected = null;
        if (projectId.HasValue && projectId.Value > 0)
        {
            selected = projects.FirstOrDefault(p => p.Id == projectId.Value);
        }
        else if (projects.Any())
        {
            selected = projects.First();
        }

        return View(selected);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  10. WORK → MEMBER PAGE & MEMBER CRUD
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Members(string? search, string? role, string? status)
    {
        if (!CheckPermission("Member", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Members.";
            return RedirectToAction("List", "Lead");
        }

        SetWorkNav("WorkMember");
        ViewData["Title"] = "Team Members";

        var users = await _db.Users.OrderBy(u => u.Name).ToListAsync();
        var projects = await _db.WorkProjects.ToListAsync();
        var tasks = await _db.WorkTasks.Include(t => t.Project).ToListAsync();
        var timesheets = await _db.WorkTimesheets.ToListAsync();

        var memberItems = new List<WorkMemberItem>();

        foreach (var user in users)
        {
            var uName = (user.Name ?? "").Trim();

            // Projects where user is PM or assigned
            var userProjects = projects
                .Where(p =>
                    string.Equals(p.ProjectManager, uName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(p.AssignedMembers) && p.AssignedMembers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(m => string.Equals(m, uName, StringComparison.OrdinalIgnoreCase))))
                .Select(p => p.ProjectName ?? p.ProjectCode ?? "")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList();

            // Tasks assigned to user
            var userTasks = tasks
                .Where(t => !string.IsNullOrEmpty(t.AssignedMember) &&
                    string.Equals(t.AssignedMember.Trim(), uName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Timesheets
            var userHours = timesheets
                .Where(ts => !string.IsNullOrEmpty(ts.MemberName) &&
                    string.Equals(ts.MemberName.Trim(), uName, StringComparison.OrdinalIgnoreCase))
                .Sum(ts => ts.HoursWorked);

            memberItems.Add(new WorkMemberItem
            {
                Id = user.Id,
                Name = uName,
                Email = user.Email ?? "",
                Role = string.IsNullOrWhiteSpace(user.Role) ? "Team Member" : user.Role,
                Status = string.IsNullOrWhiteSpace(user.Status) ? "Active" : user.Status,
                ModulePermissions = user.ModulePermissions ?? "",
                TotalProjects = userProjects.Count,
                TotalTasks = userTasks.Count,
                CompletedTasks = userTasks.Count(t => t.Status == "Completed"),
                PendingTasks = userTasks.Count(t => t.Status != "Completed"),
                LoggedHours = userHours,
                AssignedProjects = userProjects,
                ActiveTasksList = userTasks.Where(t => t.Status != "Completed").Take(8).ToList()
            });
        }

        var distinctRoles = memberItems
            .Select(m => m.Role)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        var viewModel = new WorkMembersViewModel
        {
            Members = memberItems,
            AvailableRoles = distinctRoles
        };

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> SaveMember(int id, string name, string email, string role, string status, string? password = null, string? returnUrl = null)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        var requiredAction = id == 0 ? "Create" : "Edit";

        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", requiredAction))
        {
            TempData["ErrorMessage"] = $"Access Denied: You do not have permission to {requiredAction.ToLower()} members.";
            return RedirectToAction(nameof(Members));
        }

        if (!isAdmin && string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "Access Denied: Only administrators can assign the Admin role.";
            return RedirectToAction(nameof(Members));
        }

        var adminPassword = _configuration?["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        if (!string.IsNullOrWhiteSpace(password) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(password.Trim(), adminPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "User password must be different from the administrator password.";
            return RedirectToAction(nameof(Members));
        }

        name = name?.Trim() ?? "";
        email = email?.Trim().ToLowerInvariant() ?? "";
        role = string.IsNullOrWhiteSpace(role) ? "Team Member" : role.Trim();
        status = string.IsNullOrWhiteSpace(status) ? "Active" : status.Trim();

        if (id != 0)
        {
            var targetUser = await _db.Users.FindAsync(id);
            if (targetUser != null && !isAdmin && (string.Equals(targetUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(targetUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase)))
            {
                TempData["ErrorMessage"] = "Access Denied: Only administrators can modify an Admin account.";
                return RedirectToAction(nameof(Members));
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<UserRoleEntry>();
            if (id == 0)
            {
                var newUser = new UserRoleEntry
                {
                    Name = name,
                    Email = email,
                    Role = role,
                    Status = status,
                    CreatedAt = DateTime.UtcNow
                };
                var pw = !string.IsNullOrWhiteSpace(password) ? password.Trim() : CrmLeadManagement.Security.PasswordGenerator.Generate();
                newUser.PasswordHash = hasher.HashPassword(newUser, pw);
                _db.Users.Add(newUser);
            }
            else
            {
                var user = await _db.Users.FindAsync(id);
                if (user is not null)
                {
                    user.Name = name;
                    user.Email = email;
                    user.Role = role;
                    user.Status = status;
                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        user.PasswordHash = hasher.HashPassword(user, password.Trim());
                        user.LastPasswordChangeAt = DateTime.UtcNow;
                    }
                }
            }
            await _db.SaveChangesAsync();
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        var referer = Request.Headers["Referer"].ToString();
        if (!string.IsNullOrWhiteSpace(referer) && referer.Contains("/Team", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction("Index", "Team");
        }

        return RedirectToAction(nameof(Members));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMember(int id)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete members.";
            return RedirectToAction(nameof(Members));
        }

        var user = await _db.Users.FindAsync(id);
        if (user is not null)
        {
            if (!string.IsNullOrEmpty(authEmail) && string.Equals(user.Email, authEmail, StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "You cannot delete your own account while logged in.";
                return RedirectToAction(nameof(Members));
            }

            if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Administrator accounts cannot be deleted.";
                return RedirectToAction(nameof(Members));
            }

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Members));
    }

    [HttpPost]
    public async Task<IActionResult> SaveUserSetup(string userName, List<string>? selectedModules)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && !PermissionChecker.HasModulePermission(_db, currentUser, "Member", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: Only administrators can modify user permission setups.";
            return RedirectToAction(nameof(Members));
        }

        userName = userName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(userName))
        {
            TempData["ErrorMessage"] = "User Name is required.";
            return RedirectToAction(nameof(Members));
        }

        var moduleString = selectedModules != null && selectedModules.Any()
            ? string.Join(", ", selectedModules.Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
            : "";

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Name == userName);
        if (user != null)
        {
            user.ModulePermissions = moduleString;
        }
        else
        {
            user = new UserRoleEntry
            {
                Name = userName,
                Email = $"{userName.ToLower().Replace(" ", ".")}@example.com",
                Role = "Team Member",
                Status = "Active",
                ModulePermissions = moduleString
            };
            _db.Users.Add(user);
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Permissions saved successfully for '{userName}'.";
        return RedirectToAction(nameof(Members));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API: Get milestones by project (for cascade dropdown in Task modals)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetMilestonesByProject(int projectId)
    {
        var milestones = await _db.WorkMilestones
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.MilestoneName)
            .Select(m => new { m.Id, m.MilestoneCode, m.MilestoneName })
            .ToListAsync();
        return Json(milestones);
    }
}
