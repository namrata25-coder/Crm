using Microsoft.AspNetCore.Mvc;

namespace CrmLeadManagement.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("List", "Lead");
        }
        return RedirectToAction("Login", "Auth");
    }

    public IActionResult Error()
    {
        return Problem();
    }
}
