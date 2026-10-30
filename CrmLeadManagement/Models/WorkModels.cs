using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace CrmLeadManagement.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  PROJECT
// ─────────────────────────────────────────────────────────────────────────────
public class WorkProject
{
    public int Id { get; set; }
    public string? ProjectCode   { get; set; } = "";
    public string? ProjectName   { get; set; } = "";
    public string? ClientName    { get; set; } = "";
    public string? ClientCompany { get; set; } = "";
    /// Direct Project | Milestone Based Project | Task Based Project
    public string? ProjectType   { get; set; } = "Direct Project";
    public string? Description   { get; set; } = "";
    public string? StartDate     { get; set; } = "";
    public string? Deadline      { get; set; } = "";
    public string? ProjectManager  { get; set; } = "";
    public string? AssignedMembers { get; set; } = "";
    public decimal Budget       { get; set; }
    public string? Priority      { get; set; } = "Medium";
    public string? Status        { get; set; } = "Active";
    public string? Notes         { get; set; } = "";
    public string? CreatedDate   { get; set; } = "";

    // Navigation
    public List<WorkMilestone> Milestones { get; set; } = new();
    public List<WorkTask>      Tasks      { get; set; } = new();
    [JsonIgnore]
    public List<WorkInvoice>   Invoices   { get; set; } = new();
    public List<WorkFile>      Files      { get; set; } = new();
    public List<WorkTimesheet> Timesheets { get; set; } = new();
    public List<WorkExpense>   Expenses   { get; set; } = new();
    public List<WorkNote>      WorkNotes  { get; set; } = new();

    // Calculated — not stored
    public decimal CompletedAmount
    {
        get
        {
            if (Tasks.Any()) return Tasks.Sum(t => t.CompletedAmount);
            if (Milestones.Any()) return Milestones.Sum(m => m.EffectiveCompletedAmount);
            return 0;
        }
    }

    public decimal PendingAmount => Math.Max(0, Budget - CompletedAmount);

    public int ProgressPercent =>
        Budget <= 0 ? 0 : (int)Math.Min(100, Math.Max(0, Math.Round(CompletedAmount / Budget * 100)));

    // Invoice Summary (Computed from Invoices and Payments tables, not stored in DB)
    [NotMapped]
    public decimal InvoiceTotal => Invoices != null && Invoices.Any()
        ? Invoices.Sum(i => i.GrandTotal)
        : 0m;

    [NotMapped]
    public decimal InvoiceReceived => Invoices != null && Invoices.Any()
        ? Invoices.Sum(i => (i.Payments != null && i.Payments.Any()) ? i.Payments.Sum(p => p.Amount) : i.PaidAmount)
        : 0m;

    [NotMapped]
    public decimal InvoiceDue => Math.Max(0m, InvoiceTotal - InvoiceReceived);
}

// ─────────────────────────────────────────────────────────────────────────────
//  MILESTONE
// ─────────────────────────────────────────────────────────────────────────────
public class WorkMilestone
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public string? MilestoneCode   { get; set; } = "";
    public string? MilestoneName   { get; set; } = "";
    public string? Description     { get; set; } = "";
    public string? StartDate       { get; set; } = "";
    public string? DueDate         { get; set; } = "";
    public decimal Amount         { get; set; }
    public decimal CompletedAmount { get; set; }
    public string? AssignedMembers { get; set; } = "";
    public string? Priority        { get; set; } = "Medium";
    public string? Status          { get; set; } = "Pending";
    public string? Notes           { get; set; } = "";

    // Navigation
    [JsonIgnore]
    public List<WorkTask> Tasks { get; set; } = new();

    // Calculated
    public decimal EffectiveCompletedAmount => Tasks.Any() ? Tasks.Sum(t => t.CompletedAmount) : CompletedAmount;
    public decimal EffectiveAmount => Amount > 0 ? Amount : (Tasks.Any() ? Tasks.Sum(t => t.TaskAmount) : 0);
    public decimal PendingAmount => Math.Max(0, (Amount > 0 ? Amount : EffectiveAmount) - EffectiveCompletedAmount);
    public int ProgressPercent
    {
        get
        {
            var total = Amount > 0 ? Amount : EffectiveAmount;
            if (total <= 0) return 0;
            return (int)Math.Min(100, Math.Max(0, Math.Round(EffectiveCompletedAmount / total * 100)));
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  TASK
// ─────────────────────────────────────────────────────────────────────────────
public class WorkTask
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public int? MilestoneId { get; set; }
    [JsonIgnore]
    public WorkMilestone? Milestone { get; set; }

    public string? TaskCode        { get; set; } = "";
    public string? TaskName        { get; set; } = "";
    public string? Description     { get; set; } = "";
    public string? AssignedMember  { get; set; } = "";
    public string? StartDate       { get; set; } = "";
    public string? DueDate         { get; set; } = "";
    public decimal TaskAmount     { get; set; }
    public decimal CompletedAmount { get; set; }
    public string? Priority        { get; set; } = "Medium";
    public string? Status          { get; set; } = "Pending";
    public string? Notes           { get; set; } = "";
    public string? CreatedDate     { get; set; } = "";

    // Calculated
    public decimal PendingAmount => Math.Max(0, TaskAmount - CompletedAmount);
    public int ProgressPercent =>
        TaskAmount <= 0 ? 0 : (int)Math.Min(100, Math.Round(CompletedAmount / TaskAmount * 100));
}

// ─────────────────────────────────────────────────────────────────────────────
//  INVOICE
// ─────────────────────────────────────────────────────────────────────────────
public class WorkInvoice
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public int? MilestoneId { get; set; }
    [JsonIgnore]
    public WorkMilestone? Milestone { get; set; }

    public string? InvoiceNumber  { get; set; } = "";
    public string? ClientId       { get; set; } = "";
    public string? ClientName     { get; set; } = "";
    public string? ClientCompany  { get; set; } = "";
    public string? InvoiceDate    { get; set; } = "";
    public string? DueDate        { get; set; } = "";
    public decimal Amount         { get; set; }
    public decimal TaxRate        { get; set; } // 0, 5, 12, 18, 28 etc.
    public decimal Tax            { get; set; } // calculated tax amount
    public string? TaxName        { get; set; } = "";
    public string? TaxType        { get; set; } = ""; // GST | CGST | SGST | IGST
    public decimal Discount       { get; set; }
    public decimal GrandTotal     { get; set; }
    public decimal PaidAmount     { get; set; }
    public string? PaymentStatus  { get; set; } = "Unpaid"; // Unpaid | Partially Paid | Paid | Overdue
    public string? PaymentDate    { get; set; } = "";
    public bool IsRecurring      { get; set; } = false;
    public string? RecurringFrequency { get; set; } = ""; // Monthly | Quarterly | Yearly
    public string? Notes          { get; set; } = "";
    public string? CreatedDate    { get; set; } = "";

    // Navigation
    public List<WorkPayment> Payments { get; set; } = new();

    // Calculated
    public decimal EffectivePaidAmount => Payments.Any() ? Payments.Sum(p => p.Amount) : PaidAmount;
    public decimal RemainingAmount => Math.Max(0, GrandTotal - EffectivePaidAmount);

    public string CalculatedStatus
    {
        get
        {
            var paid = EffectivePaidAmount;
            if (paid >= GrandTotal && GrandTotal > 0) return "Paid";
            if (paid > 0 && paid < GrandTotal) return "Partial Paid";
            if (!string.IsNullOrWhiteSpace(DueDate) && DateTime.TryParse(DueDate, out var dDate) && dDate.Date < DateTime.UtcNow.Date && (GrandTotal - paid) > 0)
                return "Overdue";
            return "Pending";
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  PAYMENT
// ─────────────────────────────────────────────────────────────────────────────
public class WorkPayment
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    [JsonIgnore]
    public WorkInvoice? Invoice { get; set; }

    public string? PaymentNumber        { get; set; } = "";
    public decimal Amount               { get; set; }
    public string? PaymentDate          { get; set; } = "";
    public string? PaymentMethod        { get; set; } = "Bank Transfer";
    public string? TransactionReference { get; set; } = "";
    public string? Notes                { get; set; } = "";
    public string? CreatedDate          { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
//  RECURRING INVOICE PROFILE
// ─────────────────────────────────────────────────────────────────────────────
public class WorkRecurringInvoice
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public int? MilestoneId { get; set; }
    [JsonIgnore]
    public WorkMilestone? Milestone { get; set; }

    public string? ProfileNumber   { get; set; } = "";
    public string? Title           { get; set; } = "";
    public string? ClientName      { get; set; } = "";
    public string? ClientCompany   { get; set; } = "";
    public string? Frequency       { get; set; } = "Monthly"; // Monthly | Quarterly | Half-Yearly | Yearly
    public string? StartDate       { get; set; } = "";
    public string? NextInvoiceDate { get; set; } = "";
    public string? EndDate         { get; set; } = "";
    public decimal Amount          { get; set; }
    public decimal TaxRate         { get; set; }
    public decimal Tax             { get; set; }
    public string? TaxName         { get; set; } = "";
    public string? TaxType         { get; set; } = "";
    public decimal Discount        { get; set; }
    public decimal GrandTotal      { get; set; }
    public string? Status          { get; set; } = "Active"; // Active | Paused | Completed
    public int TotalCycles         { get; set; } = 12;
    public int CompletedCycles     { get; set; } = 0;
    public string? Notes           { get; set; } = "";
    public string? CreatedDate     { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
//  FILE (Project Files & Attachments)
// ─────────────────────────────────────────────────────────────────────────────
public class WorkFile
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public string? FileName         { get; set; } = "";
    public string? OriginalFileName { get; set; } = "";
    public string? FilePath         { get; set; } = "";
    public long FileSizeBytes       { get; set; }
    public string? FileType         { get; set; } = "";
    public string? UploadedBy       { get; set; } = "";
    public string? UploadedDate     { get; set; } = "";
    public string? Description      { get; set; } = "";

    // Helper formatted size
    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{(FileSizeBytes / 1024.0):F1} KB";
            return $"{(FileSizeBytes / (1024.0 * 1024.0)):F2} MB";
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  TIMESHEET (Time Tracking & Log)
// ─────────────────────────────────────────────────────────────────────────────
public class WorkTimesheet
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public int? TaskId { get; set; }
    [JsonIgnore]
    public WorkTask? Task { get; set; }

    public string? MemberName    { get; set; } = "";
    public string? LogDate       { get; set; } = "";
    public decimal HoursWorked   { get; set; }
    public string? StartTime     { get; set; } = "";
    public string? EndTime       { get; set; } = "";
    public decimal HourlyRate    { get; set; }
    public decimal TotalCost     { get; set; }
    public string? BillingStatus { get; set; } = "Billable"; // Billable | Non-Billable | Billed
    public string? Description   { get; set; } = "";
    public string? Notes         { get; set; } = "";
    public string? CreatedDate   { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
//  EXPENSE (Project Expenses)
// ─────────────────────────────────────────────────────────────────────────────
public class WorkExpense
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public string? ExpenseCode     { get; set; } = "";
    public string? ExpenseName     { get; set; } = "";
    public string? Category        { get; set; } = "Travel"; // Travel | Software/Licenses | Hardware/Equipment | Contractor | Meals/Entertainment | Office Supplies | Other
    public decimal Amount          { get; set; }
    public decimal TaxAmount       { get; set; }
    public decimal TotalAmount     { get; set; }
    public string? ExpenseDate     { get; set; } = "";
    public string? PaymentMethod   { get; set; } = "Credit Card"; // Credit Card | Bank Transfer | Cash | UPI | Reimbursement
    public string? Status          { get; set; } = "Pending"; // Pending | Approved | Paid | Rejected
    public string? PaidBy          { get; set; } = "";
    public string? ReceiptFileName { get; set; } = "";
    public string? ReceiptFilePath { get; set; } = "";
    public string? Notes           { get; set; } = "";
    public string? CreatedDate     { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
//  NOTE (Project Notes & Discussion)
// ─────────────────────────────────────────────────────────────────────────────
public class WorkNote
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    [JsonIgnore]
    public WorkProject? Project { get; set; }

    public string? Title            { get; set; } = "";
    public string? Content          { get; set; } = "";
    public string? Category         { get; set; } = "General"; // General | Meeting | Requirement | Technical | Client Feedback
    public bool IsPinned            { get; set; } = false;
    public string? ColorTag         { get; set; } = "blue"; // blue | amber | green | purple | rose | slate
    public string? CreatedBy        { get; set; } = "";
    public string? CreatedDate      { get; set; } = "";
    public string? LastModifiedDate { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
//  LOOKUP LISTS
// ─────────────────────────────────────────────────────────────────────────────
public static class WorkLookup
{
    public static readonly string[] ProjectTypes          = { "Direct Project", "Milestone Based Project", "Task Based Project" };
    public static readonly string[] Priorities            = { "High", "Medium", "Low" };
    public static readonly string[] ProjectStatuses       = { "Active", "On Hold", "Completed", "Cancelled" };
    public static readonly string[] MilestoneStatuses     = { "Pending", "In Progress", "Completed", "On Hold" };
    public static readonly string[] TaskStatuses          = { "Pending", "In Progress", "Completed", "On Hold", "Cancelled" };
    public static readonly string[] PaymentStatuses       = { "Pending", "Partial Paid", "Paid", "Overdue" };
    public static readonly string[] PaymentMethods        = { "Cash", "Bank Transfer", "UPI", "Cheque", "Card", "Other" };
    public static readonly string[] RecurringFrequencies  = { "Weekly", "Monthly", "Quarterly", "Yearly" };
    public static readonly decimal[] TaxRates             = { 0m, 5m, 12m, 18m, 28m };
    public static readonly string[] BillingStatuses       = { "Billable", "Non-Billable", "Billed" };
    public static readonly string[] ExpenseCategories     = { "Travel", "Software/Licenses", "Hardware/Equipment", "Contractor", "Meals/Entertainment", "Office Supplies", "Other" };
    public static readonly string[] ExpensePaymentMethods = { "Credit Card", "Bank Transfer", "Cash", "UPI", "Reimbursement" };
    public static readonly string[] ExpenseStatuses       = { "Pending", "Approved", "Paid", "Rejected" };
    public static readonly string[] NoteCategories         = { "General", "Meeting", "Requirement", "Technical", "Client Feedback" };
    public static readonly string[] NoteColors             = { "blue", "amber", "green", "purple", "rose", "slate" };
}

// ─────────────────────────────────────────────────────────────────────────────
//  WORK MEMBERS VIEW MODELS
// ─────────────────────────────────────────────────────────────────────────────
public class WorkMemberItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Team Member";
    public string Status { get; set; } = "Active";
    public int TotalProjects { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int PendingTasks { get; set; }
    public decimal LoggedHours { get; set; }
    public string ModulePermissions { get; set; } = "";
    public List<string> AssignedProjects { get; set; } = new();
    public List<WorkTask> ActiveTasksList { get; set; } = new();
}

public class WorkMembersViewModel
{
    public List<WorkMemberItem> Members { get; set; } = new();
    public int TotalMembers => Members.Count;
    public int ActiveMembers => Members.Count(m => string.Equals(m.Status, "Active", StringComparison.OrdinalIgnoreCase));
    public int TotalAssignedProjects => Members.Sum(m => m.TotalProjects);
    public int TotalAssignedTasks => Members.Sum(m => m.TotalTasks);
    public List<string> AvailableRoles { get; set; } = new();
}

