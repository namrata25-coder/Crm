namespace CrmLeadManagement.Models;

/// <summary>
/// Centralized Tax Master model configurable under Settings -> Tax Master.
/// Supports GST, CGST, SGST, and IGST tax types.
/// </summary>
public class TaxMaster
{
    public int Id { get; set; }
    public string TaxName { get; set; } = "";     // e.g. "GST 18%", "CGST 9%", "SGST 9%", "IGST 18%"
    public string TaxType { get; set; } = "GST";   // GST | CGST | SGST | IGST
    public decimal TaxRate { get; set; }          // e.g. 18.00, 9.00
    public string? Description { get; set; } = "";
    public string Status { get; set; } = "Active"; // Active | Inactive
}
