namespace CrmLeadManagement.Models.Dtos;

/// <summary>Generic error payload returned by the API on failure (400/404/500).</summary>
public class ApiError
{
    public string Message { get; set; } = "";
    public string? Detail { get; set; }
}

/// <summary>Body for POST /api/leads/{id}/calls and POST /api/leads/{id}/call-details</summary>
public class AddCallRequest
{
    public string CallDate { get; set; } = "";
    public string CallTime { get; set; } = "";
    public string CallType { get; set; } = "Outgoing";
    public string CallStatus { get; set; } = "";
    public string Duration { get; set; } = "";
    public string? Notes { get; set; }
    public string? Remark { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>Body for POST /api/leads/{id}/followups</summary>
public class AddFollowUpRequest
{
    public string FollowUpDate { get; set; } = "";
    public string FollowUpTime { get; set; } = "";
    public string FollowUpType { get; set; } = "Call";
    public string Status { get; set; } = "";
    public string? Remarks { get; set; }
}

/// <summary>Body for POST /api/leads/{id}/visits</summary>
public class AddVisitRequest
{
    public string VisitDate { get; set; } = "";
    public string VisitTime { get; set; } = "";
    public string VisitLocation { get; set; } = "";
    public string? Employee { get; set; }
    public string? Conversation { get; set; }
    public string? Outcome { get; set; } = "Interested";
    public string? Remarks { get; set; }
}

/// <summary>Body for POST /api/leads/{id}/requirements (JSON, no image). Use the multipart
/// endpoint (same route, multipart/form-data) if an image needs to be uploaded.</summary>
public class AddRequirementRequest
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "";
    public string ExpectedDate { get; set; } = "";
    public string Budget { get; set; } = "";
    public string Status { get; set; } = "New";
}

/// <summary>Body for POST /api/leads/{id}/assign</summary>
public class AssignLeadRequest
{
    public string AssignedEmployee { get; set; } = "";
    public string AssignmentDate { get; set; } = "";
    public string? Remarks { get; set; }
}

/// <summary>Body for POST/DELETE on Settings lookup lists (status / source values).</summary>
public class LookupValueRequest
{
    public string Value { get; set; } = "";
}

/// <summary>A single Item/Service row used in Estimation and Quotation API requests.</summary>
public class FinanceLineItemRequest
{
    public string ItemName { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Rate { get; set; } = "";
}

/// <summary>Body for POST/PUT /api/leads/{id}/estimations</summary>
public class EstimationRequest
{
    public string Date { get; set; } = "";
    public string ProjectRequirement { get; set; } = "";
    public List<FinanceLineItemRequest> Items { get; set; } = new();
    public string Discount { get; set; } = "0";
    public string Tax { get; set; } = "0";
    public string ValidUntil { get; set; } = "";
    public string TermsConditions { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "Draft";
}

/// <summary>Body for POST/PUT /api/leads/{id}/quotations</summary>
public class QuotationRequest
{
    public string Date { get; set; } = "";
    public string ProjectRequirement { get; set; } = "";
    public List<FinanceLineItemRequest> Items { get; set; } = new();
    public string Discount { get; set; } = "0";
    public string Tax { get; set; } = "0";
    public string ValidUntil { get; set; } = "";
    public string TermsConditions { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "Draft";
}

/// <summary>Body for POST/PUT /api/leads/{id}/proposals</summary>
public class ProposalRequest
{
    public string Date { get; set; } = "";
    public string ProjectRequirement { get; set; } = "";
    public string Title { get; set; } = "";
    public string ScopeOfWork { get; set; } = "";
    public string Description { get; set; } = "";
    public string Deliverables { get; set; } = "";
    public string Timeline { get; set; } = "";
    public string Budget { get; set; } = "";
    public string TermsConditions { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "Draft";
}
