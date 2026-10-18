namespace CrmLeadManagement.Models;

/// <summary>
/// Audit trail entity for recording security and administrative actions
/// (e.g., user creation, updates, role changes, status changes, password resets).
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = ""; // "CreateUser", "UpdateUser", "ChangeRole", "ChangeStatus", "ResetPassword", "DeleteUser", "ChangePassword"
    public string? PerformedBy { get; set; } = "";
    public int? TargetUserId { get; set; }
    public string? TargetUserEmail { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
