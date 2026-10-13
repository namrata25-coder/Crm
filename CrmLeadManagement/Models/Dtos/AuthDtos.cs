namespace CrmLeadManagement.Models.Dtos;

/// <summary>Request payload for POST /api/auth/login</summary>
public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? DeviceId { get; set; }
}

/// <summary>Success payload for POST /api/auth/login</summary>
public class LoginResponse
{
    public string Token { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAt { get; set; }
    public UserProfileDto User { get; set; } = new();
}

/// <summary>User profile information returned in auth responses and GET /api/auth/me</summary>
public class UserProfileDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
}

/// <summary>Request payload for POST /api/auth/change-password</summary>
public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

/// <summary>Request payload for POST /api/auth/register</summary>
public class RegisterRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
