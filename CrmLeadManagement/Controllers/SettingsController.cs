using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<SettingsController> _logger;
    private readonly IConfiguration? _configuration;

    public SettingsController(AppDbContext db, ILogger<SettingsController> logger, IConfiguration? configuration = null)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    private bool CheckPermission(string module, string action = "View")
    {
        if (User.IsInRole("Admin")) return true;

        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        if (currentUser != null && (string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return PermissionChecker.HasModulePermission(_db, currentUser, module, action);
    }

    // GET /Settings
    public IActionResult Index()
    {
        if (!CheckPermission("Settings", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to access Settings.";
            return RedirectToAction("List", "Lead");
        }

        ViewData["Title"] = "Settings";
        ViewData["ActivePage"] = "Settings";

        var vm = new SettingsViewModel
        {
            Statuses = SettingsStore.Statuses,
            Sources = SettingsStore.Sources,
            Users = SettingsStore.Users,
            Taxes = SettingsStore.Taxes,
            FollowUp = SettingsStore.FollowUp
        };
        return View(vm);
    }

    [HttpPost]
    public IActionResult AddStatus(string newStatus)
    {
        if (!CheckPermission("Lead Status/Source", "Create") && !CheckPermission("Lead Status/Source", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to add lead statuses.";
            return RedirectToFragment("lead-status");
        }

        SettingsStore.AddStatus(newStatus);
        return RedirectToFragment("lead-status");
    }

    [HttpPost]
    public IActionResult RemoveStatus(string status)
    {
        if (!CheckPermission("Lead Status/Source", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to remove lead statuses.";
            return RedirectToFragment("lead-status");
        }

        SettingsStore.RemoveStatus(status);
        return RedirectToFragment("lead-status");
    }

    [HttpPost]
    public IActionResult AddSource(string newSource)
    {
        if (!CheckPermission("Lead Status/Source", "Create") && !CheckPermission("Lead Status/Source", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to add lead sources.";
            return RedirectToFragment("lead-sources");
        }

        SettingsStore.AddSource(newSource);
        return RedirectToFragment("lead-sources");
    }

    [HttpPost]
    public IActionResult RemoveSource(string source)
    {
        if (!CheckPermission("Lead Status/Source", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to remove lead sources.";
            return RedirectToFragment("lead-sources");
        }

        SettingsStore.RemoveSource(source);
        return RedirectToFragment("lead-sources");
    }

    [HttpPost]
    public IActionResult AddUser(UserRoleEntry newUser, string? password = null)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && !CheckPermission("Users & Roles", "Create"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to create users.";
            return RedirectToFragment("users-roles");
        }

        if (!isAdmin && string.Equals(newUser.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "Access Denied: Only administrators can assign the Admin role.";
            return RedirectToFragment("users-roles");
        }

        var adminPassword = _configuration?["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        if (!string.IsNullOrWhiteSpace(password) && !string.Equals(newUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(password.Trim(), adminPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "User password must be different from the administrator password.";
            return RedirectToFragment("users-roles");
        }

        try
        {
            var savedPassword = SettingsStore.AddUser(newUser, password);
            TempData["GeneratedUserPassword"] = savedPassword;
            TempData["CreatedUserEmail"] = newUser.Email;
        }
        catch (DuplicateUserEmailException)
        {
            TempData["ErrorMessage"] = "A user with that email address already exists.";
        }
        catch (ArgumentException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error while creating user {Email}", newUser.Email);
            TempData["ErrorMessage"] = "The user could not be created because of a database error. Please try again.";
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Database error while creating user {Email}", newUser.Email);
            TempData["ErrorMessage"] = "The user could not be created because of a database error. Please try again.";
        }

        var referer = Request.Headers["Referer"].ToString();
        if (!string.IsNullOrEmpty(referer))
        {
            return Redirect(referer);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult RemoveUser(string email)
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        bool isAdmin = User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && !CheckPermission("Users & Roles", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete users.";
            return RedirectToFragment("users-roles");
        }

        email = email?.Trim().ToLowerInvariant() ?? "";
        if (!string.IsNullOrEmpty(authEmail) && string.Equals(email, authEmail, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "You cannot delete your own account while logged in.";
            return RedirectToFragment("users-roles");
        }

        var targetUser = _db.Users.FirstOrDefault(u => u.Email != null && u.Email.ToLower() == email);
        if (targetUser != null && (string.Equals(targetUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(targetUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase)))
        {
            TempData["ErrorMessage"] = "Administrator accounts cannot be deleted.";
            return RedirectToFragment("users-roles");
        }

        SettingsStore.RemoveUser(email);
        var referer = Request.Headers["Referer"].ToString();
        if (!string.IsNullOrEmpty(referer))
        {
            return Redirect(referer);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult SaveFollowUp(FollowUpSettings followUpInput)
    {
        if (!CheckPermission("Follow-up Settings", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to modify follow-up settings.";
            return RedirectToFragment("followup-settings");
        }

        SettingsStore.UpdateFollowUpSettings(followUpInput);
        return RedirectToFragment("followup-settings");
    }

    [HttpPost]
    public IActionResult AddTax(TaxMaster newTax)
    {
        if (!CheckPermission("Settings", "Create") && !CheckPermission("Settings", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to add taxes.";
            return RedirectToFragment("tax-master");
        }

        if (!string.IsNullOrWhiteSpace(newTax.TaxName))
        {
            SettingsStore.AddTax(newTax);
        }
        return RedirectToFragment("tax-master");
    }

    [HttpPost]
    public IActionResult UpdateTax(TaxMaster tax)
    {
        if (!CheckPermission("Settings", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to update taxes.";
            return RedirectToFragment("tax-master");
        }

        if (tax.Id > 0 && !string.IsNullOrWhiteSpace(tax.TaxName))
        {
            SettingsStore.UpdateTax(tax);
        }
        return RedirectToFragment("tax-master");
    }

    [HttpPost]
    public IActionResult DeleteTax(int id)
    {
        if (!CheckPermission("Settings", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete taxes.";
            return RedirectToFragment("tax-master");
        }

        SettingsStore.DeleteTax(id);
        return RedirectToFragment("tax-master");
    }

    [HttpPost]
    public IActionResult ToggleTaxStatus(int id)
    {
        if (!CheckPermission("Settings", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to modify tax status.";
            return RedirectToFragment("tax-master");
        }

        SettingsStore.ToggleTaxStatus(id);
        return RedirectToFragment("tax-master");
    }

    private IActionResult RedirectToFragment(string fragment)
    {
        return Redirect(Url.Action("Index", "Settings") + "#" + fragment);
    }
}
