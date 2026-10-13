namespace CrmLeadManagement.Models;

/// <summary>
/// Opaque bearer access token entity storing SHA-256 hash.
/// Plain tokens are 32 random bytes (Base64Url encoded) returned only once at login.
/// </summary>
public class AccessToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = ""; // SHA-256 hex string (64 characters)
    public string? DeviceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    // Navigation property
    public UserRoleEntry? User { get; set; }
}
