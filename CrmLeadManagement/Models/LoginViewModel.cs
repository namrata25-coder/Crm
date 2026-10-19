using System.ComponentModel.DataAnnotations;

namespace CrmLeadManagement.Models;

/// <summary>
/// View model for the web login page.
/// Supports login via User ID or Email address.
/// </summary>
public class LoginViewModel
{
    private string _userId = "";

    [Required(ErrorMessage = "User ID or Email is required.")]
    [Display(Name = "User ID")]
    public string UserId
    {
        get => _userId;
        set => _userId = value ?? "";
    }

    /// <summary>Alias for UserId to accept 'Email' form fields as well.</summary>
    public string Email
    {
        get => _userId;
        set => _userId = value ?? "";
    }

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Display(Name = "Remember Me")]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public string? SuccessMessage { get; set; }

    /// <summary>Target authentication portal: 'admin' for Admin Login, 'user' for User Login.</summary>
    public string? LoginType { get; set; } = "user";
}
