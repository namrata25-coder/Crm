using System.ComponentModel.DataAnnotations;

namespace CrmLeadManagement.Models.Dtos;

public class CreateUserRequest
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Full Name must be between 2 and 100 characters.")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Email Address is required.")]
    [EmailAddress(ErrorMessage = "Please provide a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Role is required.")]
    public string Role { get; set; } = "";

    public string Status { get; set; } = "Active";
}

public class UpdateUserRequest
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Full Name must be between 2 and 100 characters.")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Email Address is required.")]
    [EmailAddress(ErrorMessage = "Please provide a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Role is required.")]
    public string Role { get; set; } = "";

    [Required(ErrorMessage = "Status is required.")]
    public string Status { get; set; } = "Active";
}

public class UserItemResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string? ModulePermissions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool MustChangePassword { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastPasswordChangeAt { get; set; }
    public bool IsLocked { get; set; }
}

public class ChangePasswordModel
{
    [Required(ErrorMessage = "Current / Temporary password is required.")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "New password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "New password must be at least 8 characters long.")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Password confirmation is required.")]
    [Compare("NewPassword", ErrorMessage = "The password confirmation does not match the new password.")]
    public string ConfirmPassword { get; set; } = "";
}
