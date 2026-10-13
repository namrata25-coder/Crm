namespace CrmLeadManagement.Models;

/// <summary>
/// View model for the Settings page (MVC view), bundling the lookup lists and
/// follow-up defaults that used to live directly on the Razor Pages PageModel.
/// </summary>
public class SettingsViewModel
{
    public List<string> Statuses { get; set; } = new();
    public List<string> Sources { get; set; } = new();
    public List<UserRoleEntry> Users { get; set; } = new();
    public List<TaxMaster> Taxes { get; set; } = new();
    public FollowUpSettings FollowUp { get; set; } = new();
}
