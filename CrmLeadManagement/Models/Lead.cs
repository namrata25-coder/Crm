namespace CrmLeadManagement.Models;

public class Lead
{
    /// <summary>Database primary key (identity column). Not shown in the UI — the UI uses LeadId.</summary>
    public int Id { get; set; }

    public string LeadId { get; set; } = "";
    public string LeadName { get; set; } = "";
    public string? CompanyName { get; set; } = "";
    public string? MobileNumber { get; set; } = "";
    public string? Email { get; set; } = "";
    public string? Designation { get; set; } = "";
    public string? Location { get; set; } = "";
    public string? LeadStatus { get; set; } = "";
    public string? Source { get; set; } = "";
    public string? AssignedTo { get; set; } = "";
    public string? CreatedDate { get; set; } = "";
    public string? FollowUpDate { get; set; } = "";
    public string? Priority { get; set; } = "Medium";
    public string? AssignedDate { get; set; } = "";
    public string? Remarks { get; set; }

    public List<CallHistoryEntry> CallHistory { get; set; } = new();
    public List<FollowUpEntry> FollowUps { get; set; } = new();
    public List<RequirementEntry> Requirements { get; set; } = new();
    public List<VisitEntry> Visits { get; set; } = new();
    public List<AssignmentEntry> AssignmentHistory { get; set; } = new();

    // ===== Finance (Estimation / Quotation / Proposal) — all linked to this lead =====
    public List<EstimationEntry> Estimations { get; set; } = new();
    public List<QuotationEntry> Quotations { get; set; } = new();
    public List<ProposalEntry> Proposals { get; set; } = new();

    /// <summary>Latest visit location shown as a quick-glance column in the Lead List.</summary>
    public string LatestVisitLocation => Visits.Count > 0 ? (Visits[^1].VisitLocation ?? "No visit yet") : "No visit yet";
}

public class VisitEntry
{
    public int Id { get; set; }
    public string? VisitDate { get; set; } = "";
    public string? VisitTime { get; set; } = "";
    public string? VisitLocation { get; set; } = "";
    public string? Employee { get; set; } = "";
    public string? Conversation { get; set; } = "";
    public string? Outcome { get; set; } = ""; // Interested / Not Interested / Follow-up Needed / Deal Closed
    public string? Remarks { get; set; } = "";
}

public class CallHistoryEntry
{
    public int Id { get; set; }
    public string? CallDate { get; set; } = "";
    public string? CallTime { get; set; } = "";
    public string? CallType { get; set; } = "Outgoing"; // Incoming / Outgoing
    public string? Duration { get; set; } = "";
    public string? Employee { get; set; } = "";
    public string? CallStatus { get; set; } = ""; // Connected / Not Connected / Not Answered / Busy / Follow-up Needed
    public string? Notes { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Remark
    {
        get => Notes;
        set => Notes = value;
    }
}

/// <summary>CallDetail entity representing a single call log entry associated with a Lead.</summary>
public class CallDetail : CallHistoryEntry
{
}

public class FollowUpEntry
{
    public int Id { get; set; }
    public string? FollowUpDate { get; set; } = "";
    public string? FollowUpTime { get; set; } = "";
    public string? FollowUpType { get; set; } = ""; // Call / Email / Visit / Meeting
    public string? AssignedEmployee { get; set; } = "";
    public string? Status { get; set; } = ""; // Pending / Completed / Rescheduled
    public string? Remarks { get; set; } = "";
}

public class RequirementEntry
{
    public int Id { get; set; }
    public string? Title { get; set; } = "";
    public string? Description { get; set; } = "";
    public string? Priority { get; set; } = "";
    public string? ExpectedDate { get; set; } = "";
    public string? Budget { get; set; } = "";
    public string? Status { get; set; } = "New";
    public string? ImagePath { get; set; } = ""; // relative path under wwwroot, e.g. /uploads/requirements/xyz.jpg

    // Kept for backward compatibility with older seeded data / existing screens.
    public string? RequirementType { get; set; } = "";
    public string? ItemName { get; set; } = "";
    public string? Quantity { get; set; } = "";
    public string? Details { get; set; } = "";
    public string? Remarks { get; set; } = "";
}

/// <summary>A single assign/re-assign action logged against a lead.</summary>
public class AssignmentEntry
{
    public int Id { get; set; }
    public string? AssignedEmployee { get; set; } = "";
    public string? AssignmentDate { get; set; } = "";
    public string? Remarks { get; set; } = "";
}

/// <summary>A single item/service row used inside an Estimation or Quotation.</summary>
public class FinanceLineItem
{
    public int Id { get; set; }
    public int? EstimationId_FK { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public EstimationEntry? Estimation { get; set; }

    public int? QuotationId_FK { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public QuotationEntry? Quotation { get; set; }

    public string? ItemName { get; set; } = "";
    public string? Quantity { get; set; } = "";
    public string? Rate { get; set; } = "";
    public string? Amount { get; set; } = ""; // Quantity * Rate, computed server-side on save
}

/// <summary>An Estimation raised against a lead (Finance &gt; Estimation tab).</summary>
public class EstimationEntry
{
    public int Id { get; set; }
    public string? Number { get; set; } = "";           // e.g. EST-2001
    public string? Date { get; set; } = "";
    public string? LeadCustomer { get; set; } = "";      // display copy of the lead's name/company at save time
    public string? ProjectRequirement { get; set; } = "";
    public List<FinanceLineItem> Items { get; set; } = new();
    public string? Subtotal { get; set; } = "0";
    public string? Discount { get; set; } = "0";
    public string? TaxRate { get; set; } = "0";
    public string? Tax { get; set; } = "0";
    public string? TaxName { get; set; } = "";
    public string? TaxType { get; set; } = "";
    public string? Total { get; set; } = "0";
    public string? ValidUntil { get; set; } = "";
    public string? TermsConditions { get; set; } = "";
    public string? Notes { get; set; } = "";
    public string? Status { get; set; } = "Draft";

    // Optional single attachment (quote/estimation backup file, drawing, spec sheet, etc.)
    public string? AttachmentPath { get; set; } = "";     // relative path under wwwroot, e.g. /uploads/finance/estimations/xyz.pdf
    public string? AttachmentFileName { get; set; } = ""; // original file name shown to the user, e.g. Estimate_Draft.pdf
}

/// <summary>A Quotation raised against a lead (Finance &gt; Quotation tab).</summary>
public class QuotationEntry
{
    public int Id { get; set; }
    public string? Number { get; set; } = "";           // e.g. QUO-2001
    public string? Date { get; set; } = "";
    public string? LeadCustomer { get; set; } = "";
    public string? ProjectRequirement { get; set; } = "";
    public List<FinanceLineItem> Items { get; set; } = new();
    public string? Subtotal { get; set; } = "0";
    public string? Discount { get; set; } = "0";
    public string? TaxRate { get; set; } = "0";
    public string? Tax { get; set; } = "0";
    public string? TaxName { get; set; } = "";
    public string? TaxType { get; set; } = "";
    public string? GrandTotal { get; set; } = "0";
    public string? ValidUntil { get; set; } = "";
    public string? TermsConditions { get; set; } = "";
    public string? Notes { get; set; } = "";
    public string? Status { get; set; } = "Draft";

    public string? AttachmentPath { get; set; } = "";
    public string? AttachmentFileName { get; set; } = "";
}

/// <summary>A Proposal raised against a lead (Finance &gt; Proposal tab). No line items.</summary>
public class ProposalEntry
{
    public int Id { get; set; }
    public string? Number { get; set; } = "";           // e.g. PRO-2001
    public string? Date { get; set; } = "";
    public string? LeadCustomer { get; set; } = "";
    public string? ProjectRequirement { get; set; } = "";
    public string? Title { get; set; } = "";
    public string? ScopeOfWork { get; set; } = "";
    public string? Description { get; set; } = "";
    public string? Deliverables { get; set; } = "";
    public string? Timeline { get; set; } = "";
    public string? Budget { get; set; } = "";
    public string? Subtotal { get; set; } = "0";
    public string? Discount { get; set; } = "0";
    public string? TaxRate { get; set; } = "0";
    public string? Tax { get; set; } = "0";
    public string? TaxName { get; set; } = "";
    public string? TaxType { get; set; } = "";
    public string? GrandTotal { get; set; } = "0";
    public string? TermsConditions { get; set; } = "";
    public string? Notes { get; set; } = "";
    public string? Status { get; set; } = "Draft";

    public string? AttachmentPath { get; set; } = "";
    public string? AttachmentFileName { get; set; } = "";
}

/// <summary>
/// Fixed lookup lists that are not editable via Settings (kept static/simple by design).
/// Lead Status, Lead Sources and Users/Roles are editable — see <see cref="SettingsStore"/>.
/// </summary>
public static class LookupData
{
    public static readonly string[] Priorities = { "High", "Medium", "Low" };

    public static readonly string[] RequirementTypes =
    {
        "Material", "Service", "Product", "Price Quote", "Demo", "Other"
    };

    public static readonly string[] CallTypes = { "Outgoing", "Incoming" };
    public static readonly string[] CallStatuses = { "Connected", "Not Answered", "Busy", "Follow-up Needed" };
    public static readonly string[] FollowUpTypes = { "Call", "Email", "Visit", "Meeting" };
    public static readonly string[] FollowUpStatuses = { "Pending", "Completed", "Rescheduled" };
    public static readonly string[] VisitOutcomes = { "Interested", "Not Interested", "Follow-up Needed", "Deal Closed" };
    public static readonly string[] RequirementStatuses = { "New", "In Progress", "Fulfilled", "On Hold" };
    public static readonly string[] FinanceStatuses = { "Draft", "Sent", "Approved", "Rejected", "Expired" };

    /// <summary>Convenience accessor so existing pages can keep using LookupData.Employees etc.</summary>
    public static List<string> Employees => SettingsStore.Employees;
    public static List<string> Statuses => SettingsStore.Statuses;
    public static List<string> Sources => SettingsStore.Sources;
    public static List<TaxMaster> Taxes => SettingsStore.ActiveTaxes;
}
