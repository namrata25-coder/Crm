using CrmLeadManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class SettingsController : Controller
{
    // GET /Settings
    public IActionResult Index()
    {
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
        SettingsStore.AddStatus(newStatus);
        return RedirectToFragment("lead-status");
    }

    [HttpPost]
    public IActionResult RemoveStatus(string status)
    {
        SettingsStore.RemoveStatus(status);
        return RedirectToFragment("lead-status");
    }

    [HttpPost]
    public IActionResult AddSource(string newSource)
    {
        SettingsStore.AddSource(newSource);
        return RedirectToFragment("lead-sources");
    }

    [HttpPost]
    public IActionResult RemoveSource(string source)
    {
        SettingsStore.RemoveSource(source);
        return RedirectToFragment("lead-sources");
    }

    [HttpPost]
    public IActionResult AddUser(UserRoleEntry newUser)
    {
        if (!string.IsNullOrWhiteSpace(newUser.Name) && !string.IsNullOrWhiteSpace(newUser.Email))
        {
            SettingsStore.AddUser(newUser);
        }
        return RedirectToFragment("users-roles");
    }

    [HttpPost]
    public IActionResult RemoveUser(string email)
    {
        SettingsStore.RemoveUser(email);
        return RedirectToFragment("users-roles");
    }

    [HttpPost]
    public IActionResult SaveFollowUp(FollowUpSettings followUpInput)
    {
        SettingsStore.UpdateFollowUpSettings(followUpInput);
        return RedirectToFragment("followup-settings");
    }

    [HttpPost]
    public IActionResult AddTax(TaxMaster newTax)
    {
        if (!string.IsNullOrWhiteSpace(newTax.TaxName))
        {
            SettingsStore.AddTax(newTax);
        }
        return RedirectToFragment("tax-master");
    }

    [HttpPost]
    public IActionResult UpdateTax(TaxMaster tax)
    {
        if (tax.Id > 0 && !string.IsNullOrWhiteSpace(tax.TaxName))
        {
            SettingsStore.UpdateTax(tax);
        }
        return RedirectToFragment("tax-master");
    }

    [HttpPost]
    public IActionResult DeleteTax(int id)
    {
        SettingsStore.DeleteTax(id);
        return RedirectToFragment("tax-master");
    }

    [HttpPost]
    public IActionResult ToggleTaxStatus(int id)
    {
        SettingsStore.ToggleTaxStatus(id);
        return RedirectToFragment("tax-master");
    }

    private IActionResult RedirectToFragment(string fragment)
    {
        return Redirect(Url.Action("Index", "Settings") + "#" + fragment);
    }
}
