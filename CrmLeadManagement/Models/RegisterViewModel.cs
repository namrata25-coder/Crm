using System.ComponentModel.DataAnnotations;

namespace CrmLeadManagement.Models;

public class RegisterViewModel
{
    [Required(ErrorMessage = "User ID is required.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "User ID must be between 3 and 50 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_\-\.@]+$", ErrorMessage = "User ID can only contain letters, numbers, dots, hyphens, and underscores.")]
    [Display(Name = "User ID")]
    public string UserId { get; set; } = "";

    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
    [Display(Name = "Full Name")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password.")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Password and confirmation password do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}
