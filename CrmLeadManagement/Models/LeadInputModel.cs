namespace CrmLeadManagement.Models;

/// <summary>
/// Binds the Add/Edit Lead modal form. When LeadId is empty, SaveLead creates a new
/// lead; when it matches an existing lead, SaveLead updates that lead in place.
/// </summary>
public class LeadInputModel
{
    public string LeadId { get; set; } = "";
    public string LeadName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string MobileNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Designation { get; set; } = "";
    public string Location { get; set; } = "";
    public string Source { get; set; } = "";
    public string LeadStatus { get; set; } = "";
    public string AssignedTo { get; set; } = "";
    public string Priority { get; set; } = "Medium";
    public string FollowUpDate { get; set; } = "";
    public string? Remarks { get; set; }

    public Lead ToLead()
    {
        return new Lead
        {
            LeadId = LeadId,
            LeadName = LeadName,
            CompanyName = CompanyName,
            MobileNumber = MobileNumber,
            Email = Email,
            Designation = Designation,
            Location = Location,
            Source = Source,
            LeadStatus = LeadStatus,
            AssignedTo = AssignedTo,
            Priority = Priority,
            FollowUpDate = FollowUpDate,
            Remarks = Remarks
        };
    }
}
