using System.Text.Json.Serialization;

namespace CrmLeadManagement.Models.Dtos.App;

/// <summary>
/// Uniform envelope for successful REST API responses.
/// </summary>
public class AppApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Operation completed successfully";

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    public static AppApiResponse<T> Ok(T? data, string message = "Operation completed successfully")
    {
        return new AppApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data
        };
    }
}

/// <summary>
/// Uniform envelope for successful REST API responses without explicit typed payload.
/// </summary>
public class AppApiResponse : AppApiResponse<object>
{
    public static AppApiResponse Ok(string message = "Operation completed successfully")
    {
        return new AppApiResponse
        {
            Success = true,
            Message = message,
            Data = null
        };
    }
}

/// <summary>
/// Uniform envelope for API error responses.
/// </summary>
public class AppApiErrorResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = false;

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("errors")]
    public object? Errors { get; set; }

    public static AppApiErrorResponse Fail(string message, object? errors = null)
    {
        return new AppApiErrorResponse
        {
            Success = false,
            Message = message,
            Errors = errors ?? new { }
        };
    }
}

/// <summary>
/// Generic paginated collection container.
/// </summary>
public class PaginatedResult<T>
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = new();
}

/// <summary>
/// Sanitized user DTO safe for settings and public exposure.
/// </summary>
public class AppSettingsUserDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("modulePermissions")]
    public string? ModulePermissions { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>
/// Aggregate application settings response DTO.
/// </summary>
public class AppFullSettingsDto
{
    [JsonPropertyName("statuses")]
    public List<string> Statuses { get; set; } = new();

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = new();

    [JsonPropertyName("users")]
    public List<AppSettingsUserDto> Users { get; set; } = new();

    [JsonPropertyName("taxes")]
    public List<TaxMaster> Taxes { get; set; } = new();

    [JsonPropertyName("followUp")]
    public FollowUpSettings? FollowUp { get; set; }
}

/// <summary>
/// Status creation/update input DTO.
/// </summary>
public class StatusInputDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    public string GetEffectiveValue() =>
        !string.IsNullOrWhiteSpace(Value) ? Value.Trim() : (Name?.Trim() ?? "");
}

/// <summary>
/// Source creation/update input DTO.
/// </summary>
public class SourceInputDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    public string GetEffectiveValue() =>
        !string.IsNullOrWhiteSpace(Value) ? Value.Trim() : (Name?.Trim() ?? "");
}

/// <summary>
/// User creation payload in application settings.
/// </summary>
public class CreateSettingsUserDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Active";

    [JsonPropertyName("modulePermissions")]
    public string? ModulePermissions { get; set; }
}

/// <summary>
/// Tax configuration input DTO.
/// </summary>
public class TaxInputDto
{
    [JsonPropertyName("taxName")]
    public string TaxName { get; set; } = "";

    [JsonPropertyName("taxType")]
    public string? TaxType { get; set; } = "GST";

    [JsonPropertyName("taxRate")]
    public decimal TaxRate { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; } = "";

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Active";
}

/// <summary>
/// Follow-up settings update DTO.
/// </summary>
public class FollowUpSettingsInputDto
{
    [JsonPropertyName("defaultReminderDaysBefore")]
    public int DefaultReminderDaysBefore { get; set; } = 1;

    [JsonPropertyName("enableEmailReminders")]
    public bool EnableEmailReminders { get; set; } = true;

    [JsonPropertyName("enableSmsReminders")]
    public bool EnableSmsReminders { get; set; } = false;

    [JsonPropertyName("defaultFollowUpType")]
    public string DefaultFollowUpType { get; set; } = "Call";

    [JsonPropertyName("autoEscalateAfterDaysOverdue")]
    public int AutoEscalateAfterDaysOverdue { get; set; } = 3;
}

/// <summary>
/// Lead creation/update input DTO for /api/v1/app/leads.
/// </summary>
public class AppLeadInputDto
{
    [JsonPropertyName("leadName")]
    public string LeadName { get; set; } = "";

    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; } = "";

    [JsonPropertyName("mobileNumber")]
    public string? MobileNumber { get; set; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; set; } = "";

    [JsonPropertyName("designation")]
    public string? Designation { get; set; } = "";

    [JsonPropertyName("location")]
    public string? Location { get; set; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; set; } = "";

    [JsonPropertyName("leadStatus")]
    public string? LeadStatus { get; set; } = "New";

    [JsonPropertyName("assignedTo")]
    public string? AssignedTo { get; set; } = "";

    [JsonPropertyName("priority")]
    public string? Priority { get; set; } = "Medium";

    [JsonPropertyName("followUpDate")]
    public string? FollowUpDate { get; set; } = "";

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }
}

/// <summary>
/// Call logging input DTO for /api/v1/app/leads/{id}/calls.
/// </summary>
public class AppCallInputDto
{
    [JsonPropertyName("callDate")]
    public string? CallDate { get; set; }

    [JsonPropertyName("callTime")]
    public string? CallTime { get; set; }

    [JsonPropertyName("callType")]
    public string? CallType { get; set; } = "Outgoing";

    [JsonPropertyName("callStatus")]
    public string? CallStatus { get; set; } = "Connected";

    [JsonPropertyName("duration")]
    public string? Duration { get; set; } = "00:00";

    [JsonPropertyName("employee")]
    public string? Employee { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Follow-up scheduling input DTO for /api/v1/app/leads/{id}/followups.
/// </summary>
public class AppFollowUpInputDto
{
    [JsonPropertyName("followUpDate")]
    public string? FollowUpDate { get; set; }

    [JsonPropertyName("followUpTime")]
    public string? FollowUpTime { get; set; }

    [JsonPropertyName("followUpType")]
    public string? FollowUpType { get; set; } = "Call";

    [JsonPropertyName("assignedEmployee")]
    public string? AssignedEmployee { get; set; }

    [JsonPropertyName("employee")]
    public string? Employee { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Scheduled";

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}

/// <summary>
/// Visit entry input DTO for /api/v1/app/leads/{id}/visits.
/// </summary>
public class AppVisitInputDto
{
    [JsonPropertyName("visitDate")]
    public string? VisitDate { get; set; }

    [JsonPropertyName("visitTime")]
    public string? VisitTime { get; set; }

    [JsonPropertyName("visitLocation")]
    public string? VisitLocation { get; set; }

    [JsonPropertyName("employee")]
    public string? Employee { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; } = "Interested";

    [JsonPropertyName("conversation")]
    public string? Conversation { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}

/// <summary>
/// Requirement entry input DTO for /api/v1/app/leads/{id}/requirements.
/// </summary>
public class AppRequirementInputDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; } = "Medium";

    [JsonPropertyName("expectedDate")]
    public string? ExpectedDate { get; set; }

    [JsonPropertyName("budget")]
    public string? Budget { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "New";

    [JsonPropertyName("requirementType")]
    public string? RequirementType { get; set; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("quantity")]
    public string? Quantity { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }
}

/// <summary>
/// Lead statistics response DTO for /api/v1/app/leads/stats.
/// </summary>
public class AppLeadStatsDto
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("assigned")]
    public int Assigned { get; set; }

    [JsonPropertyName("unassigned")]
    public int Unassigned { get; set; }

    [JsonPropertyName("new")]
    public int New { get; set; }

    [JsonPropertyName("inProgress")]
    public int InProgress { get; set; }

    [JsonPropertyName("contacted")]
    public int Contacted { get; set; }

    [JsonPropertyName("qualified")]
    public int Qualified { get; set; }

    [JsonPropertyName("proposal")]
    public int Proposal { get; set; }

    [JsonPropertyName("won")]
    public int Won { get; set; }

    [JsonPropertyName("lost")]
    public int Lost { get; set; }

    [JsonPropertyName("closed")]
    public int Closed { get; set; }

    [JsonPropertyName("byStatus")]
    public Dictionary<string, int> ByStatus { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("byPriority")]
    public Dictionary<string, int> ByPriority { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("bySource")]
    public Dictionary<string, int> BySource { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Hybrid envelope supporting both flat keys and standard 'data' envelope.
/// </summary>
public class AppLeadStatsEnvelope : AppLeadStatsDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Lead statistics retrieved successfully.";

    [JsonPropertyName("data")]
    public AppLeadStatsDto? Data { get; set; }
}

/// <summary>
/// Project creation/update DTO for /api/v1/app/projects.
/// </summary>
public class AppProjectInputDto
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("clientName")]
    public string? ClientName { get; set; } = "";

    [JsonPropertyName("clientCompany")]
    public string? ClientCompany { get; set; } = "";

    [JsonPropertyName("projectType")]
    public string? ProjectType { get; set; } = "Direct Project";

    [JsonPropertyName("description")]
    public string? Description { get; set; } = "";

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; } = "";

    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; } = "";

    [JsonPropertyName("projectManager")]
    public string? ProjectManager { get; set; } = "";

    [JsonPropertyName("assignedMembers")]
    public string? AssignedMembers { get; set; } = "";

    [JsonPropertyName("budget")]
    public decimal Budget { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; } = "Medium";

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Active";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; } = "";
}

/// <summary>
/// Milestone creation/update DTO for /api/v1/app/milestones.
/// </summary>
public class AppMilestoneInputDto
{
    [JsonPropertyName("projectId")]
    public int ProjectId { get; set; }

    [JsonPropertyName("milestoneName")]
    public string MilestoneName { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; } = "";

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; } = "";

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("completedAmount")]
    public decimal CompletedAmount { get; set; }

    [JsonPropertyName("assignedMembers")]
    public string? AssignedMembers { get; set; } = "";

    [JsonPropertyName("priority")]
    public string? Priority { get; set; } = "Medium";

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Pending";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; } = "";
}

/// <summary>
/// Detailed milestone item response DTO with dependency information.
/// </summary>
public class AppMilestoneItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("projectId")]
    public int ProjectId { get; set; }

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("projectCode")]
    public string ProjectCode { get; set; } = "";

    [JsonPropertyName("milestoneCode")]
    public string? MilestoneCode { get; set; }

    [JsonPropertyName("milestoneName")]
    public string? MilestoneName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("completedAmount")]
    public decimal CompletedAmount { get; set; }

    [JsonPropertyName("pendingAmount")]
    public decimal PendingAmount { get; set; }

    [JsonPropertyName("progressPercent")]
    public int ProgressPercent { get; set; }

    [JsonPropertyName("assignedMembers")]
    public string? AssignedMembers { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("tasksCount")]
    public int TasksCount { get; set; }

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; }

    [JsonPropertyName("lockReason")]
    public string LockReason { get; set; } = "";

    [JsonPropertyName("dependencyType")]
    public string DependencyType { get; set; } = "";

    [JsonPropertyName("sequenceIndex")]
    public int SequenceIndex { get; set; }

    [JsonPropertyName("invoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("invoiceStatus")]
    public string? InvoiceStatus { get; set; }
}

/// <summary>
/// Work summary metrics DTO for /api/v1/app/work/summary.
/// </summary>
public class AppWorkSummaryDto
{
    // Project metrics
    [JsonPropertyName("totalProjects")]
    public int TotalProjects { get; set; }

    [JsonPropertyName("activeProjects")]
    public int ActiveProjects { get; set; }

    [JsonPropertyName("completedProjects")]
    public int CompletedProjects { get; set; }

    [JsonPropertyName("onHoldProjects")]
    public int OnHoldProjects { get; set; }

    [JsonPropertyName("overdueProjects")]
    public int OverdueProjects { get; set; }

    // Milestone metrics
    [JsonPropertyName("totalMilestones")]
    public int TotalMilestones { get; set; }

    [JsonPropertyName("pendingMilestones")]
    public int PendingMilestones { get; set; }

    [JsonPropertyName("inProgressMilestones")]
    public int InProgressMilestones { get; set; }

    [JsonPropertyName("completedMilestones")]
    public int CompletedMilestones { get; set; }

    [JsonPropertyName("overdueMilestones")]
    public int OverdueMilestones { get; set; }

    // Task metrics
    [JsonPropertyName("totalTasks")]
    public int TotalTasks { get; set; }

    [JsonPropertyName("assignedTasks")]
    public int AssignedTasks { get; set; }

    [JsonPropertyName("pendingTasks")]
    public int PendingTasks { get; set; }

    [JsonPropertyName("inProgressTasks")]
    public int InProgressTasks { get; set; }

    [JsonPropertyName("completedTasks")]
    public int CompletedTasks { get; set; }

    [JsonPropertyName("overdueTasks")]
    public int OverdueTasks { get; set; }

    // Financial overview
    [JsonPropertyName("totalBudget")]
    public decimal TotalBudget { get; set; }

    [JsonPropertyName("completedValue")]
    public decimal CompletedValue { get; set; }

    [JsonPropertyName("pendingValue")]
    public decimal PendingValue { get; set; }
}

/// <summary>
/// Work dashboard statistics DTO for /api/v1/app/work/stats.
/// </summary>
public class AppWorkStatsDto
{
    [JsonPropertyName("summary")]
    public AppWorkSummaryDto Summary { get; set; } = new();

    [JsonPropertyName("taskStatusDistribution")]
    public Dictionary<string, int> TaskStatusDistribution { get; set; } = new();

    [JsonPropertyName("milestoneStatusDistribution")]
    public Dictionary<string, int> MilestoneStatusDistribution { get; set; } = new();

    [JsonPropertyName("projectStatusDistribution")]
    public Dictionary<string, int> ProjectStatusDistribution { get; set; } = new();

    [JsonPropertyName("taskPriorityDistribution")]
    public Dictionary<string, int> TaskPriorityDistribution { get; set; } = new();

    [JsonPropertyName("completionRatePercent")]
    public double CompletionRatePercent { get; set; }
}

/// <summary>
/// Single row in work reports.
/// </summary>
public class AppWorkReportItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("projectCode")]
    public string? ProjectCode { get; set; }

    [JsonPropertyName("projectName")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("clientName")]
    public string? ClientName { get; set; }

    [JsonPropertyName("projectManager")]
    public string? ProjectManager { get; set; }

    [JsonPropertyName("assignedMembers")]
    public string? AssignedMembers { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; }

    [JsonPropertyName("budget")]
    public decimal Budget { get; set; }

    [JsonPropertyName("completedAmount")]
    public decimal CompletedAmount { get; set; }

    [JsonPropertyName("pendingAmount")]
    public decimal PendingAmount { get; set; }

    [JsonPropertyName("progressPercent")]
    public int ProgressPercent { get; set; }

    [JsonPropertyName("milestonesCount")]
    public int MilestonesCount { get; set; }

    [JsonPropertyName("completedMilestonesCount")]
    public int CompletedMilestonesCount { get; set; }

    [JsonPropertyName("tasksCount")]
    public int TasksCount { get; set; }

    [JsonPropertyName("completedTasksCount")]
    public int CompletedTasksCount { get; set; }

    [JsonPropertyName("invoicesGrandTotal")]
    public decimal InvoicesGrandTotal { get; set; }

    [JsonPropertyName("expensesTotal")]
    public decimal ExpensesTotal { get; set; }

    [JsonPropertyName("timesheetHoursTotal")]
    public decimal TimesheetHoursTotal { get; set; }
}

/// <summary>
/// Overall summary header for work reports.
/// </summary>
public class AppWorkReportSummaryDto
{
    [JsonPropertyName("totalProjects")]
    public int TotalProjects { get; set; }

    [JsonPropertyName("totalBudget")]
    public decimal TotalBudget { get; set; }

    [JsonPropertyName("totalCompletedAmount")]
    public decimal TotalCompletedAmount { get; set; }

    [JsonPropertyName("totalPendingAmount")]
    public decimal TotalPendingAmount { get; set; }

    [JsonPropertyName("totalInvoicedAmount")]
    public decimal TotalInvoicedAmount { get; set; }

    [JsonPropertyName("totalExpenses")]
    public decimal TotalExpenses { get; set; }

    [JsonPropertyName("totalLoggedHours")]
    public decimal TotalLoggedHours { get; set; }
}

/// <summary>
/// Wrapper for work reports response including both summary totals and paginated items.
/// </summary>
public class AppWorkReportResponseDto
{
    [JsonPropertyName("summary")]
    public AppWorkReportSummaryDto Summary { get; set; } = new();

    [JsonPropertyName("report")]
    public PaginatedResult<AppWorkReportItemDto> Report { get; set; } = new();
}
