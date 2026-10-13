namespace CrmLeadManagement.Models.Dtos.App;

/// <summary>Request payload for mobile app login</summary>
public class AppLoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? DeviceId { get; set; }
}

/// <summary>Response payload for mobile app login</summary>
public class AppLoginResponse
{
    public string Token { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAt { get; set; }
    public AppUserProfileDto User { get; set; } = new();
}

/// <summary>Mobile app user profile DTO</summary>
public class AppUserProfileDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
}

/// <summary>Request payload for mobile app password change</summary>
public class AppChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

/// <summary>Request payload for mobile app registration</summary>
public class AppRegisterRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>Error payload for mobile app API failures</summary>
public class AppApiError
{
    public string Message { get; set; } = "";
    public string? Detail { get; set; }
}
