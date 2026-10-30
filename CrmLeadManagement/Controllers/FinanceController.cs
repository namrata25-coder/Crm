using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

/// <summary>
/// Sidebar-level Finance module: Invoices, Payments, Recurring Invoices,
/// Estimation List / Quotation List / Proposal List, and dedicated Details pages.
/// </summary>
[Authorize]
public class FinanceController : Controller
{
    private readonly AppDbContext _db;

    public FinanceController(AppDbContext db)
    {
        _db = db;
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

    // ===================== INVOICE =====================

    // GET /Finance/InvoiceList
    // GET /Finance/Invoices
    public IActionResult InvoiceList()
    {
        if (!CheckPermission("Invoice", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Invoices.";
            return RedirectToAction("List", "Lead");
        }

        ViewData["Title"] = "Invoices";
        ViewData["ActivePage"] = "FinanceInvoice";
        return View();
    }

    // GET /Finance/InvoiceDetails/5
    public async Task<IActionResult> InvoiceDetails(int id)
    {
        if (!CheckPermission("Invoice", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Invoice details.";
            return RedirectToAction(nameof(InvoiceList));
        }

        ViewData["Title"] = "Invoice Details";
        ViewData["ActivePage"] = "FinanceInvoice";
        ViewData["InvoiceId"] = id;

        var invoice = await _db.WorkInvoices
            .Include(i => i.Project)
            .Include(i => i.Milestone)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null)
        {
            return RedirectToAction(nameof(InvoiceList));
        }

        return View(invoice);
    }

    // ===================== PAYMENT =====================

    // GET /Finance/PaymentList
    // GET /Finance/Payments
    public IActionResult PaymentList()
    {
        if (!CheckPermission("Payment", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Payments.";
            return RedirectToAction("List", "Lead");
        }

        ViewData["Title"] = "Payments";
        ViewData["ActivePage"] = "FinancePayment";
        return View();
    }

    // ===================== RECURRING INVOICE =====================

    // GET /Finance/RecurringInvoiceList
    // GET /Finance/RecurringInvoices
    public IActionResult RecurringInvoiceList()
    {
        if (!CheckPermission("Invoice", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Recurring Invoices.";
            return RedirectToAction("List", "Lead");
        }

        ViewData["Title"] = "Recurring Invoices";
        ViewData["ActivePage"] = "FinanceRecurring";
        return View();
    }

    // ===================== ESTIMATION =====================

    // GET /Finance/EstimationList
    public IActionResult EstimationList()
    {
        if (!CheckPermission("Estimation", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Estimations.";
            return RedirectToAction("List", "Lead");
        }

        ViewData["Title"] = "Estimation List";
        ViewData["ActivePage"] = "FinanceEstimation";

        // Apply role‑based filtering: admins/managers see all, others only their assigned leads
        var currentUser = PermissionChecker.GetCurrentUser(_db, User);
        var isAdminOrManager = currentUser != null && PermissionChecker.IsAdminOrManager(User, currentUser);

        var rows = LeadStore.Leads
            .Where(lead => isAdminOrManager ||
                (lead.AssignedTo != null && currentUser != null &&
                 (string.Equals(lead.AssignedTo.Trim(), currentUser.Name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(lead.AssignedTo.Trim(), currentUser.Email?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(lead.AssignedTo.Trim(), currentUser.UserId?.Trim(), StringComparison.OrdinalIgnoreCase))))
            .SelectMany(lead => lead.Estimations.Select(e => new FinanceListRow
            {
                LeadId = lead.LeadId,
                LeadName = lead.LeadName,
                CompanyName = lead.CompanyName ?? "",
                MobileNumber = lead.MobileNumber ?? "",
                Email = lead.Email ?? "",
                Date = e.Date ?? "",
                Number = e.Number ?? "",
                Total = e.Total ?? "",
                Status = e.Status ?? "",
                AttachmentPath = e.AttachmentPath ?? "",
                AttachmentFileName = e.AttachmentFileName ?? "",
                Notes = e.Notes ?? "",
            }))
            .OrderByDescending(r => r.Number)
            .ToList();

        return View(rows);
    }

    // GET /Finance/EstimationDetails?leadId=LD-1001&number=EST-2001
    public IActionResult EstimationDetails(string leadId, string number)
    {
        if (!CheckPermission("Estimation", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Estimation details.";
            return RedirectToAction(nameof(EstimationList));
        }

        var lead = LeadStore.GetById(leadId);
        var entry = lead?.Estimations.FirstOrDefault(e => e.Number == number);
        if (lead is null || entry is null)
        {
            return RedirectToAction("EstimationList");
        }

        ViewData["Title"] = "Estimation Details";
        ViewData["ActivePage"] = "FinanceEstimation";
        ViewData["Lead"] = lead;
        return View(entry);
    }

    // POST /Finance/SaveEstimation
    [HttpPost]
    public async Task<IActionResult> SaveEstimation(string? leadId, string? number, string? notes, IFormFile? attachment, string? returnUrl = null)
    {
        if (!CheckPermission("Estimation", "Create") && !CheckPermission("Estimation", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to create or edit estimations.";
            return RedirectToAction(nameof(EstimationList));
        }

        Lead? lead = null;
        EstimationEntry? existing = null;
        if (!string.IsNullOrWhiteSpace(number))
        {
            foreach (var l in LeadStore.Leads)
            {
                var found = l.Estimations.FirstOrDefault(e => e.Number == number);
                if (found is not null)
                {
                    lead = l;
                    existing = found;
                    break;
                }
            }
        }

        if (lead is null)
        {
            var selectedLeadId = !string.IsNullOrWhiteSpace(leadId) ? leadId : LeadStore.Leads.FirstOrDefault()?.LeadId;
            if (string.IsNullOrWhiteSpace(selectedLeadId))
            {
                TempData["ErrorMessage"] = "Please create or select a lead before adding an estimation.";
                return RedirectToAction(nameof(EstimationList));
            }

            lead = LeadStore.GetById(selectedLeadId);
            if (lead is null)
            {
                TempData["ErrorMessage"] = "Selected lead could not be found.";
                return RedirectToAction(nameof(EstimationList));
            }
        }

        var entry = existing ?? new EstimationEntry { Number = LeadStore.NextEstimationNumber() };

        var (attPath, attName, attError) = await FinanceAttachmentService.SaveAsync(attachment, "estimations");
        if (attError is not null)
        {
            TempData["ErrorMessage"] = attError;
            return RedirectToAction(nameof(EstimationList));
        }

        if (!string.IsNullOrWhiteSpace(attPath))
        {
            FinanceAttachmentService.Delete(entry.AttachmentPath);
            entry.AttachmentPath = attPath;
            entry.AttachmentFileName = attName;
        }

        entry.Date = !string.IsNullOrWhiteSpace(entry.Date) ? entry.Date : DateTime.UtcNow.ToString("yyyy-MM-dd");
        entry.LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})";
        entry.Notes = notes ?? "";
        entry.Status = !string.IsNullOrWhiteSpace(entry.Status) ? entry.Status : "Draft";

        if (existing is null)
        {
            lead.Estimations.Add(entry);
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Estimation {entry.Number} saved successfully.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(EstimationList));
    }

    // POST /Finance/DeleteEstimation
    [HttpPost]
    public async Task<IActionResult> DeleteEstimation(string leadId, string number)
    {
        if (!CheckPermission("Estimation", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete estimations.";
            return RedirectToAction(nameof(EstimationList));
        }

        var lead = LeadStore.GetById(leadId);
        var existing = lead?.Estimations.FirstOrDefault(e => e.Number == number);
        if (existing is not null)
        {
            FinanceAttachmentService.Delete(existing.AttachmentPath);
            lead!.Estimations.Remove(existing);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Estimation {number} deleted successfully.";
        }

        return RedirectToAction("EstimationList");
    }

    // ===================== QUOTATION =====================

    // GET /Finance/QuotationList
    public IActionResult QuotationList()
    {
        if (!CheckPermission("Quotation", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Quotations.";
            return RedirectToAction("List", "Lead");
        }

        ViewData["Title"] = "Quotation List";
        ViewData["ActivePage"] = "FinanceQuotation";

        // Apply role‑based filtering: admins/managers see all, others only their assigned leads
        var currentUser = PermissionChecker.GetCurrentUser(_db, User);
        var isAdminOrManager = currentUser != null && PermissionChecker.IsAdminOrManager(User, currentUser);

        var rows = LeadStore.Leads
            .Where(lead => isAdminOrManager ||
                (lead.AssignedTo != null && currentUser != null &&
                 (string.Equals(lead.AssignedTo.Trim(), currentUser.Name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(lead.AssignedTo.Trim(), currentUser.Email?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(lead.AssignedTo.Trim(), currentUser.UserId?.Trim(), StringComparison.OrdinalIgnoreCase))))
            .SelectMany(lead => lead.Quotations.Select(q => new FinanceListRow
            {
                LeadId = lead.LeadId,
                LeadName = lead.LeadName,
                CompanyName = lead.CompanyName ?? "",
                MobileNumber = lead.MobileNumber ?? "",
                Email = lead.Email ?? "",
                Date = q.Date ?? "",
                Number = q.Number ?? "",
                Total = q.GrandTotal ?? "",
                Status = q.Status ?? "",
                AttachmentPath = q.AttachmentPath ?? "",
                AttachmentFileName = q.AttachmentFileName ?? "",
                Notes = q.Notes ?? "",
            }))
            .OrderByDescending(r => r.Number)
            .ToList();

        return View(rows);
    }

    // GET /Finance/QuotationDetails?leadId=LD-1001&number=QUO-2001
    public IActionResult QuotationDetails(string leadId, string number)
    {
        if (!CheckPermission("Quotation", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Quotation details.";
            return RedirectToAction(nameof(QuotationList));
        }

        var lead = LeadStore.GetById(leadId);
        var entry = lead?.Quotations.FirstOrDefault(q => q.Number == number);
        if (lead is null || entry is null)
        {
            return RedirectToAction("QuotationList");
        }

        ViewData["Title"] = "Quotation Details";
        ViewData["ActivePage"] = "FinanceQuotation";
        ViewData["Lead"] = lead;
        return View(entry);
    }

    // POST /Finance/SaveQuotation
    [HttpPost]
    public async Task<IActionResult> SaveQuotation(string? leadId, string? number, string? notes, IFormFile? attachment, string? returnUrl = null)
    {
        if (!CheckPermission("Quotation", "Create") && !CheckPermission("Quotation", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to create or edit quotations.";
            return RedirectToAction(nameof(QuotationList));
        }

        Lead? lead = null;
        QuotationEntry? existing = null;
        if (!string.IsNullOrWhiteSpace(number))
        {
            foreach (var l in LeadStore.Leads)
            {
                var found = l.Quotations.FirstOrDefault(q => q.Number == number);
                if (found is not null)
                {
                    lead = l;
                    existing = found;
                    break;
                }
            }
        }

        if (lead is null)
        {
            var selectedLeadId = !string.IsNullOrWhiteSpace(leadId) ? leadId : LeadStore.Leads.FirstOrDefault()?.LeadId;
            if (string.IsNullOrWhiteSpace(selectedLeadId))
            {
                TempData["ErrorMessage"] = "Please create or select a lead before adding a quotation.";
                return RedirectToAction(nameof(QuotationList));
            }

            lead = LeadStore.GetById(selectedLeadId);
            if (lead is null)
            {
                TempData["ErrorMessage"] = "Selected lead could not be found.";
                return RedirectToAction(nameof(QuotationList));
            }
        }

        var entry = existing ?? new QuotationEntry { Number = LeadStore.NextQuotationNumber() };

        var (attPath, attName, attError) = await FinanceAttachmentService.SaveAsync(attachment, "quotations");
        if (attError is not null)
        {
            TempData["ErrorMessage"] = attError;
            return RedirectToAction(nameof(QuotationList));
        }

        if (!string.IsNullOrWhiteSpace(attPath))
        {
            FinanceAttachmentService.Delete(entry.AttachmentPath);
            entry.AttachmentPath = attPath;
            entry.AttachmentFileName = attName;
        }

        entry.Date = !string.IsNullOrWhiteSpace(entry.Date) ? entry.Date : DateTime.UtcNow.ToString("yyyy-MM-dd");
        entry.LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})";
        entry.Notes = notes ?? "";
        entry.Status = !string.IsNullOrWhiteSpace(entry.Status) ? entry.Status : "Draft";

        if (existing is null)
        {
            lead.Quotations.Add(entry);
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Quotation {entry.Number} saved successfully.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(QuotationList));
    }

    // POST /Finance/DeleteQuotation
    [HttpPost]
    public async Task<IActionResult> DeleteQuotation(string leadId, string number)
    {
        if (!CheckPermission("Quotation", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete quotations.";
            return RedirectToAction(nameof(QuotationList));
        }

        var lead = LeadStore.GetById(leadId);
        var existing = lead?.Quotations.FirstOrDefault(q => q.Number == number);
        if (existing is not null)
        {
            FinanceAttachmentService.Delete(existing.AttachmentPath);
            lead!.Quotations.Remove(existing);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Quotation {number} deleted successfully.";
        }

        return RedirectToAction("QuotationList");
    }

    // ===================== PROPOSAL =====================

    // GET /Finance/ProposalList
    public IActionResult ProposalList()
    {
        if (!CheckPermission("Proposal", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Proposals.";
            return RedirectToAction("List", "Lead");
        }

        ViewData["Title"] = "Proposal List";
        ViewData["ActivePage"] = "FinanceProposal";

        // Apply role‑based filtering: admins/managers see all, others only their assigned leads
        var currentUser = PermissionChecker.GetCurrentUser(_db, User);
        var isAdminOrManager = currentUser != null && PermissionChecker.IsAdminOrManager(User, currentUser);

        var rows = LeadStore.Leads
            .Where(lead => isAdminOrManager ||
                (lead.AssignedTo != null && currentUser != null &&
                 (string.Equals(lead.AssignedTo.Trim(), currentUser.Name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(lead.AssignedTo.Trim(), currentUser.Email?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(lead.AssignedTo.Trim(), currentUser.UserId?.Trim(), StringComparison.OrdinalIgnoreCase))))
            .SelectMany(lead => lead.Proposals.Select(p => new FinanceListRow
            {
                LeadId = lead.LeadId,
                LeadName = lead.LeadName,
                CompanyName = lead.CompanyName ?? "",
                MobileNumber = lead.MobileNumber ?? "",
                Email = lead.Email ?? "",
                Date = p.Date ?? "",
                Number = p.Number ?? "",
                Total = p.Budget ?? "",
                Status = p.Status ?? "",
                AttachmentPath = p.AttachmentPath ?? "",
                AttachmentFileName = p.AttachmentFileName ?? "",
                Notes = p.Notes ?? p.Description ?? "",
            }))
            .OrderByDescending(r => r.Number)
            .ToList();

        return View(rows);
    }

    // GET /Finance/ProposalDetails?leadId=LD-1001&number=PRO-2001
    public IActionResult ProposalDetails(string leadId, string number)
    {
        if (!CheckPermission("Proposal", "View"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Proposal details.";
            return RedirectToAction(nameof(ProposalList));
        }

        var lead = LeadStore.GetById(leadId);
        var entry = lead?.Proposals.FirstOrDefault(p => p.Number == number);
        if (lead is null || entry is null)
        {
            return RedirectToAction("ProposalList");
        }

        ViewData["Title"] = "Proposal Details";
        ViewData["ActivePage"] = "FinanceProposal";
        ViewData["Lead"] = lead;
        return View(entry);
    }

    // POST /Finance/SaveProposal
    [HttpPost]
    public async Task<IActionResult> SaveProposal(string? leadId, string? number, string? notes, IFormFile? attachment, string? returnUrl = null)
    {
        if (!CheckPermission("Proposal", "Create") && !CheckPermission("Proposal", "Edit"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to create or edit proposals.";
            return RedirectToAction(nameof(ProposalList));
        }

        Lead? lead = null;
        ProposalEntry? existing = null;
        if (!string.IsNullOrWhiteSpace(number))
        {
            foreach (var l in LeadStore.Leads)
            {
                var found = l.Proposals.FirstOrDefault(p => p.Number == number);
                if (found is not null)
                {
                    lead = l;
                    existing = found;
                    break;
                }
            }
        }

        if (lead is null)
        {
            var selectedLeadId = !string.IsNullOrWhiteSpace(leadId) ? leadId : LeadStore.Leads.FirstOrDefault()?.LeadId;
            if (string.IsNullOrWhiteSpace(selectedLeadId))
            {
                TempData["ErrorMessage"] = "Please create or select a lead before adding a proposal.";
                return RedirectToAction(nameof(ProposalList));
            }

            lead = LeadStore.GetById(selectedLeadId);
            if (lead is null)
            {
                TempData["ErrorMessage"] = "Selected lead could not be found.";
                return RedirectToAction(nameof(ProposalList));
            }
        }

        var entry = existing ?? new ProposalEntry { Number = LeadStore.NextProposalNumber() };

        var (attPath, attName, attError) = await FinanceAttachmentService.SaveAsync(attachment, "proposals");
        if (attError is not null)
        {
            TempData["ErrorMessage"] = attError;
            return RedirectToAction(nameof(ProposalList));
        }

        if (!string.IsNullOrWhiteSpace(attPath))
        {
            FinanceAttachmentService.Delete(entry.AttachmentPath);
            entry.AttachmentPath = attPath;
            entry.AttachmentFileName = attName;
        }

        entry.Date = !string.IsNullOrWhiteSpace(entry.Date) ? entry.Date : DateTime.UtcNow.ToString("yyyy-MM-dd");
        entry.LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})";
        entry.Title = !string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : "Proposal";
        entry.Notes = notes ?? "";
        entry.Description = notes ?? "";
        entry.Status = !string.IsNullOrWhiteSpace(entry.Status) ? entry.Status : "Draft";

        if (existing is null)
        {
            lead.Proposals.Add(entry);
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Proposal {entry.Number} saved successfully.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(ProposalList));
    }

    // POST /Finance/DeleteProposal
    [HttpPost]
    public async Task<IActionResult> DeleteProposal(string leadId, string number)
    {
        if (!CheckPermission("Proposal", "Delete"))
        {
            TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete proposals.";
            return RedirectToAction(nameof(ProposalList));
        }

        var lead = LeadStore.GetById(leadId);
        var existing = lead?.Proposals.FirstOrDefault(p => p.Number == number);
        if (existing is not null)
        {
            FinanceAttachmentService.Delete(existing.AttachmentPath);
            lead!.Proposals.Remove(existing);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Proposal {number} deleted successfully.";
        }

        return RedirectToAction("ProposalList");
    }
}

/// <summary>Flat row used by the three sidebar Finance list pages (one lead + one finance record per row).</summary>
public class FinanceListRow
{
    public string LeadId { get; set; } = "";
    public string LeadName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string MobileNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Date { get; set; } = "";
    public string Number { get; set; } = "";
    public string Total { get; set; } = "0";
    public string Status { get; set; } = "";
    public string AttachmentPath { get; set; } = "";
    public string AttachmentFileName { get; set; } = "";
    public string Notes { get; set; } = "";
}