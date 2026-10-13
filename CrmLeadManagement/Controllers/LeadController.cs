using CrmLeadManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class LeadController : Controller
{
    // GET /Lead/List
    public IActionResult List()
    {
        ViewData["Title"] = "Lead List";
        ViewData["ActivePage"] = "LeadList";
        return View(LeadStore.Leads);
    }

    // GET /Lead/Assigned
    public IActionResult Assigned()
    {
        ViewData["Title"] = "Assigned Leads";
        ViewData["ActivePage"] = "AssignedList";
        return View(LeadStore.Leads);
    }

    // GET /Lead/Details?id=LD-1001&tab=call
    // subtab is only relevant when tab=finance (estimation | quotation | proposal)
    public IActionResult Details(string id, string? tab, string? subtab)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null)
        {
            return RedirectToAction("List");
        }

        ViewData["Title"] = "Lead Details";
        ViewData["ActivePage"] = "LeadList";
        ViewData["ActiveTab"] = string.IsNullOrWhiteSpace(tab) ? "call" : tab;
        ViewData["ActiveFinanceSubTab"] = string.IsNullOrWhiteSpace(subtab) ? "estimation" : subtab;
        return View(lead);
    }

    /// <summary>
    /// Handles both Add (empty LeadId) and Edit/Reassign (existing LeadId) from the
    /// shared lead modal. Shared by both the Lead List and Assigned List pages, which
    /// each pass their own URL back in returnUrl so the user lands back where they were.
    /// </summary>
    [HttpPost]
    public IActionResult Save(LeadInputModel Input, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(Input.LeadId))
        {
            LeadStore.AddLead(Input.ToLead());
        }
        else
        {
            LeadStore.UpdateLead(Input.ToLead());
        }

        return RedirectToReturnUrl(returnUrl, "List");
    }

    [HttpPost]
    public IActionResult Delete(string id, string? returnUrl)
    {
        LeadStore.DeleteLead(id);
        return RedirectToReturnUrl(returnUrl, "List");
    }

    /// <summary>Logs a new call against the lead and returns to the Call tab.</summary>
    [HttpPost]
    public IActionResult AddCall(string leadId, string callDate, string callTime, string? callType, string callStatus, string? duration, string? remarks, string? remark)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        var remarkText = !string.IsNullOrWhiteSpace(remark) ? remark : (remarks ?? "");

        if (!string.IsNullOrWhiteSpace(callDate) && !string.IsNullOrWhiteSpace(callTime) && !string.IsNullOrWhiteSpace(callStatus))
        {
            lead.CallHistory.Add(new CallHistoryEntry
            {
                CallDate = callDate,
                CallTime = callTime,
                CallType = string.IsNullOrWhiteSpace(callType) ? "Outgoing" : callType,
                CallStatus = callStatus,
                Duration = duration ?? "",
                Employee = lead.AssignedTo,
                Notes = remarkText
            });
            DbAccessor.Db.SaveChanges();
        }

        return RedirectToAction("Details", new { id = leadId, tab = "call" });
    }

    /// <summary>Logs a new follow-up against the lead and returns to the Follow-up tab.</summary>
    [HttpPost]
    public IActionResult AddFollowUp(string leadId, string followUpDate, string followUpTime, string status, string? remarks, string? followUpType = "Call")
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        if (!string.IsNullOrWhiteSpace(followUpDate) && !string.IsNullOrWhiteSpace(followUpTime) && !string.IsNullOrWhiteSpace(status))
        {
            lead.FollowUps.Add(new FollowUpEntry
            {
                FollowUpDate = followUpDate,
                FollowUpTime = followUpTime,
                FollowUpType = string.IsNullOrWhiteSpace(followUpType) ? "Call" : followUpType,
                AssignedEmployee = lead.AssignedTo,
                Status = status,
                Remarks = remarks ?? ""
            });

            // Keep the lead's "next follow-up" quick-glance field in sync with the latest entry.
            lead.FollowUpDate = followUpDate;
            DbAccessor.Db.SaveChanges();
        }

        return RedirectToAction("Details", new { id = leadId, tab = "followup" });
    }

    /// <summary>Logs a new visit against the lead and returns to the Visit tab.</summary>
    [HttpPost]
    public IActionResult AddVisit(string leadId, string visitDate, string visitTime, string visitLocation, string? employee, string? outcome, string? conversation, string? remarks)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        if (!string.IsNullOrWhiteSpace(visitDate) && !string.IsNullOrWhiteSpace(visitTime) && !string.IsNullOrWhiteSpace(visitLocation))
        {
            lead.Visits.Add(new VisitEntry
            {
                VisitDate = visitDate,
                VisitTime = visitTime,
                VisitLocation = visitLocation,
                Employee = !string.IsNullOrWhiteSpace(employee) ? employee : (lead.AssignedTo ?? ""),
                Outcome = !string.IsNullOrWhiteSpace(outcome) ? outcome : "Interested",
                Conversation = conversation ?? "",
                Remarks = remarks ?? ""
            });
            DbAccessor.Db.SaveChanges();
        }

        return RedirectToAction("Details", new { id = leadId, tab = "visit" });
    }

    /// <summary>Adds a new requirement (with optional image) against the lead and returns to the Requirement tab.</summary>
    [HttpPost]
    public async Task<IActionResult> AddRequirement(
        string leadId,
        string? title, string? description, string? priority, string? expectedDate, string? budget, string? status, IFormFile? image,
        List<string>? category, List<string>? items, List<string>? location,
        List<string>? h1, List<string>? h2, List<string>? h3,
        List<string>? w1, List<string>? w2, List<string>? w3,
        List<string>? types, List<string>? glass, List<string>? topology)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        var counts = new[] {
            category?.Count ?? 0,
            items?.Count ?? 0,
            location?.Count ?? 0,
            h1?.Count ?? 0,
            h2?.Count ?? 0,
            h3?.Count ?? 0,
            w1?.Count ?? 0,
            w2?.Count ?? 0,
            w3?.Count ?? 0,
            types?.Count ?? 0,
            glass?.Count ?? 0,
            topology?.Count ?? 0
        };
        var count = counts.Max();

        if (count > 0)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var uploadsFolder = Path.Combine(FinanceAttachmentService.UploadsPath, "requirements");

            for (int i = 0; i < count; i++)
            {
                var cat = category?.ElementAtOrDefault(i)?.Trim() ?? "";
                var itm = items?.ElementAtOrDefault(i)?.Trim() ?? "";
                var loc = location?.ElementAtOrDefault(i)?.Trim() ?? "";
                var _h1 = h1?.ElementAtOrDefault(i)?.Trim() ?? "";
                var _h2 = h2?.ElementAtOrDefault(i)?.Trim() ?? "";
                var _h3 = h3?.ElementAtOrDefault(i)?.Trim() ?? "";
                var _w1 = w1?.ElementAtOrDefault(i)?.Trim() ?? "";
                var _w2 = w2?.ElementAtOrDefault(i)?.Trim() ?? "";
                var _w3 = w3?.ElementAtOrDefault(i)?.Trim() ?? "";
                var typ = types?.ElementAtOrDefault(i)?.Trim() ?? "";
                var gls = glass?.ElementAtOrDefault(i)?.Trim() ?? "";
                var top = topology?.ElementAtOrDefault(i)?.Trim() ?? "";

                // Skip completely empty items
                if (string.IsNullOrWhiteSpace(cat) && string.IsNullOrWhiteSpace(itm) &&
                    string.IsNullOrWhiteSpace(loc) && string.IsNullOrWhiteSpace(_h1) &&
                    string.IsNullOrWhiteSpace(_h2) && string.IsNullOrWhiteSpace(_h3) &&
                    string.IsNullOrWhiteSpace(_w1) && string.IsNullOrWhiteSpace(_w2) &&
                    string.IsNullOrWhiteSpace(_w3) && string.IsNullOrWhiteSpace(typ) &&
                    string.IsNullOrWhiteSpace(gls) && string.IsNullOrWhiteSpace(top))
                {
                    continue;
                }

                string imagePath = "";
                var file = Request.Form.Files.GetFile($"requirementImage_{i}") 
                           ?? Request.Form.Files.GetFile($"requirementImage[{i}]") 
                           ?? Request.Form.Files.ElementAtOrDefault(i);
                var base64Data = Request.Form[$"requirementImageData_{i}"].FirstOrDefault() 
                                 ?? Request.Form[$"requirementImageData[{i}]"].FirstOrDefault() 
                                 ?? (Request.Form["requirementImageData"].Count > i ? Request.Form["requirementImageData"][i] : null);

                if (file is not null && file.Length > 0)
                {
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (allowedExtensions.Contains(ext))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                        var fileName = $"{Guid.NewGuid()}{ext}";
                        var filePath = Path.Combine(uploadsFolder, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }
                        imagePath = $"/uploads/requirements/{fileName}";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(base64Data))
                {
                    try
                    {
                        var rawData = base64Data;
                        string ext = ".jpg";
                        if (rawData.Contains(","))
                        {
                            var parts = rawData.Split(new[] { ',' }, 2);
                            var header = parts[0];
                            rawData = parts[1];
                            if (header.Contains("image/png")) ext = ".png";
                            else if (header.Contains("image/webp")) ext = ".webp";
                            else if (header.Contains("image/gif")) ext = ".gif";
                            else ext = ".jpg";
                        }

                        var bytes = Convert.FromBase64String(rawData);
                        if (bytes.Length > 0)
                        {
                            Directory.CreateDirectory(uploadsFolder);
                            var fileName = $"{Guid.NewGuid()}{ext}";
                            var filePath = Path.Combine(uploadsFolder, fileName);
                            await System.IO.File.WriteAllBytesAsync(filePath, bytes);
                            imagePath = $"/uploads/requirements/{fileName}";
                        }
                    }
                    catch
                    {
                        // Ignore corrupt base64 string and continue gracefully
                    }
                }

                // Build Item Title
                string itemTitle;
                if (!string.IsNullOrWhiteSpace(cat) && !string.IsNullOrWhiteSpace(itm))
                    itemTitle = $"{cat} — {itm}";
                else if (!string.IsNullOrWhiteSpace(itm))
                    itemTitle = itm;
                else if (!string.IsNullOrWhiteSpace(cat))
                    itemTitle = cat;
                else
                    itemTitle = $"Requirement Item #{i + 1}";

                // Build Description with location, dimensions, types, glass, topology
                var descParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(loc)) descParts.Add($"Location: {loc}");

                var hParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(_h1)) hParts.Add($"H1: {_h1}");
                if (!string.IsNullOrWhiteSpace(_h2)) hParts.Add($"H2: {_h2}");
                if (!string.IsNullOrWhiteSpace(_h3)) hParts.Add($"H3: {_h3}");
                if (hParts.Count > 0) descParts.Add($"Height: {string.Join(", ", hParts)}");

                var wParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(_w1)) wParts.Add($"W1: {_w1}");
                if (!string.IsNullOrWhiteSpace(_w2)) wParts.Add($"W2: {_w2}");
                if (!string.IsNullOrWhiteSpace(_w3)) wParts.Add($"W3: {_w3}");
                if (wParts.Count > 0) descParts.Add($"Width: {string.Join(", ", wParts)}");

                if (!string.IsNullOrWhiteSpace(typ)) descParts.Add($"Types: {typ}");
                if (!string.IsNullOrWhiteSpace(gls)) descParts.Add($"Glass: {gls}");
                if (!string.IsNullOrWhiteSpace(top)) descParts.Add($"Topology: {top}");

                lead.Requirements.Add(new RequirementEntry
                {
                    Title = itemTitle,
                    Description = string.Join(" | ", descParts),
                    Priority = priority ?? "Medium",
                    ExpectedDate = expectedDate ?? "",
                    Budget = budget ?? "",
                    Status = status ?? "New",
                    ImagePath = imagePath,
                    ItemName = itm,
                    RequirementType = cat
                });
            }

            return RedirectToAction("Details", new { id = leadId, tab = "requirement" });
        }

        // Backward compatibility for legacy single-form posts
        if (!string.IsNullOrWhiteSpace(title))
        {
            string imagePath = "";
            if (image is not null && image.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var ext = Path.GetExtension(image.FileName).ToLowerInvariant();
                if (allowedExtensions.Contains(ext))
                {
                    var uploadsFolder = Path.Combine(FinanceAttachmentService.UploadsPath, "requirements");
                    Directory.CreateDirectory(uploadsFolder);
                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(stream);
                    }
                    imagePath = $"/uploads/requirements/{fileName}";
                }
            }

            lead.Requirements.Add(new RequirementEntry
            {
                Title = title,
                Description = description ?? "",
                Priority = priority ?? "Medium",
                ExpectedDate = expectedDate ?? "",
                Budget = budget ?? "",
                Status = status ?? "New",
                ImagePath = imagePath
            });
        }

        return RedirectToAction("Details", new { id = leadId, tab = "requirement" });
    }

    /// <summary>Assigns or re-assigns the lead to an employee and logs the change.</summary>
    [HttpPost]
    public IActionResult AssignLead(string leadId, string assignedEmployee, string assignmentDate, string? remarks)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        if (!string.IsNullOrWhiteSpace(assignedEmployee) && !string.IsNullOrWhiteSpace(assignmentDate))
        {
            lead.AssignmentHistory.Add(new AssignmentEntry
            {
                AssignedEmployee = assignedEmployee,
                AssignmentDate = assignmentDate,
                Remarks = remarks ?? ""
            });

            lead.AssignedTo = assignedEmployee;
            lead.AssignedDate = assignmentDate;
        }

        return RedirectToAction("Details", new { id = leadId, tab = "assign" });
    }

    // ===================== FINANCE: ESTIMATION =====================

    /// <summary>Creates a new Estimation, or updates an existing one when 'number' is supplied. Always linked to leadId.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveEstimation(string leadId, string? number, string? date, string? projectRequirement,
        List<string>? itemName, List<string>? itemQty, List<string>? itemRate,
        string? discount, string? tax, string? validUntil, string? terms, string? notes, string? status, IFormFile? attachment,
        string? taxRate, string? taxName, string? taxType)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        var items = BuildLineItems(itemName, itemQty, itemRate);
        var subtotal = items.Sum(i => ParseDec(i.Amount));
        var discountVal = ParseDec(discount);
        var taxable = Math.Max(0, subtotal - discountVal);
        var rateVal = ParseDec(taxRate);
        var taxVal = rateVal > 0 ? (taxable * (rateVal / 100m)) : ParseDec(tax);
        var total = taxable + taxVal;

        var existing = string.IsNullOrWhiteSpace(number) ? null : lead.Estimations.FirstOrDefault(e => e.Number == number);
        var entry = existing ?? new EstimationEntry { Number = LeadStore.NextEstimationNumber() };

        var (attPath, attName, attError) = await FinanceAttachmentService.SaveAsync(attachment, "estimations");
        if (attError is not null)
        {
            TempData["EstimationError"] = attError;
            return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "estimation" });
        }
        if (!string.IsNullOrWhiteSpace(attPath))
        {
            // Replacing an existing attachment — remove the old file so uploads don't pile up.
            FinanceAttachmentService.Delete(entry.AttachmentPath);
            entry.AttachmentPath = attPath;
            entry.AttachmentFileName = attName;
        }

        entry.Date = !string.IsNullOrWhiteSpace(date) ? date : (!string.IsNullOrWhiteSpace(entry.Date) ? entry.Date : DateTime.UtcNow.ToString("yyyy-MM-dd"));
        entry.LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})";
        if (projectRequirement != null) entry.ProjectRequirement = projectRequirement;
        if (items.Count > 0 || existing is null) entry.Items = items;
        if (subtotal > 0 || existing is null) entry.Subtotal = subtotal.ToString("0.##");
        if (discount != null) entry.Discount = discount;
        entry.Tax = taxVal.ToString("0.##");
        if (!string.IsNullOrWhiteSpace(taxRate)) entry.TaxRate = taxRate;
        if (!string.IsNullOrWhiteSpace(taxName)) entry.TaxName = taxName;
        if (!string.IsNullOrWhiteSpace(taxType)) entry.TaxType = taxType;
        if (total > 0 || existing is null) entry.Total = total.ToString("0.##");
        if (validUntil != null) entry.ValidUntil = validUntil;
        if (terms != null) entry.TermsConditions = terms;
        entry.Notes = notes ?? "";
        entry.Status = !string.IsNullOrWhiteSpace(status) ? status : (!string.IsNullOrWhiteSpace(entry.Status) ? entry.Status : "Draft");

        if (existing is null) lead.Estimations.Add(entry);

        return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "estimation" });
    }

    [HttpPost]
    public IActionResult DeleteEstimation(string leadId, string number)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is not null)
        {
            var existing = lead.Estimations.FirstOrDefault(e => e.Number == number);
            if (existing is not null)
            {
                FinanceAttachmentService.Delete(existing.AttachmentPath);
                lead.Estimations.Remove(existing);
            }
        }
        return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "estimation" });
    }

    // ===================== FINANCE: QUOTATION =====================

    /// <summary>Creates a new Quotation, or updates an existing one when 'number' is supplied. Always linked to leadId.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveQuotation(string leadId, string? number, string? date, string? projectRequirement,
        List<string>? itemName, List<string>? itemQty, List<string>? itemRate,
        string? discount, string? tax, string? validUntil, string? terms, string? notes, string? status, IFormFile? attachment,
        string? taxRate, string? taxName, string? taxType)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        var items = BuildLineItems(itemName, itemQty, itemRate);
        var subtotal = items.Sum(i => ParseDec(i.Amount));
        var discountVal = ParseDec(discount);
        var taxable = Math.Max(0, subtotal - discountVal);
        var rateVal = ParseDec(taxRate);
        var taxVal = rateVal > 0 ? (taxable * (rateVal / 100m)) : ParseDec(tax);
        var grandTotal = taxable + taxVal;

        var existing = string.IsNullOrWhiteSpace(number) ? null : lead.Quotations.FirstOrDefault(q => q.Number == number);
        var entry = existing ?? new QuotationEntry { Number = LeadStore.NextQuotationNumber() };

        var (attPath, attName, attError) = await FinanceAttachmentService.SaveAsync(attachment, "quotations");
        if (attError is not null)
        {
            TempData["QuotationError"] = attError;
            return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "quotation" });
        }
        if (!string.IsNullOrWhiteSpace(attPath))
        {
            FinanceAttachmentService.Delete(entry.AttachmentPath);
            entry.AttachmentPath = attPath;
            entry.AttachmentFileName = attName;
        }

        entry.Date = !string.IsNullOrWhiteSpace(date) ? date : (!string.IsNullOrWhiteSpace(entry.Date) ? entry.Date : DateTime.UtcNow.ToString("yyyy-MM-dd"));
        entry.LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})";
        if (projectRequirement != null) entry.ProjectRequirement = projectRequirement;
        if (items.Count > 0 || existing is null) entry.Items = items;
        if (subtotal > 0 || existing is null) entry.Subtotal = subtotal.ToString("0.##");
        if (discount != null) entry.Discount = discount;
        entry.Tax = taxVal.ToString("0.##");
        if (!string.IsNullOrWhiteSpace(taxRate)) entry.TaxRate = taxRate;
        if (!string.IsNullOrWhiteSpace(taxName)) entry.TaxName = taxName;
        if (!string.IsNullOrWhiteSpace(taxType)) entry.TaxType = taxType;
        if (grandTotal > 0 || existing is null) entry.GrandTotal = grandTotal.ToString("0.##");
        if (validUntil != null) entry.ValidUntil = validUntil;
        if (terms != null) entry.TermsConditions = terms;
        entry.Notes = notes ?? "";
        entry.Status = !string.IsNullOrWhiteSpace(status) ? status : (!string.IsNullOrWhiteSpace(entry.Status) ? entry.Status : "Draft");

        if (existing is null) lead.Quotations.Add(entry);

        return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "quotation" });
    }

    [HttpPost]
    public IActionResult DeleteQuotation(string leadId, string number)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is not null)
        {
            var existing = lead.Quotations.FirstOrDefault(q => q.Number == number);
            if (existing is not null)
            {
                FinanceAttachmentService.Delete(existing.AttachmentPath);
                lead.Quotations.Remove(existing);
            }
        }
        return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "quotation" });
    }

    // ===================== FINANCE: PROPOSAL =====================

    /// <summary>Creates a new Proposal, or updates an existing one when 'number' is supplied. Always linked to leadId.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveProposal(string leadId, string? number, string? date, string? projectRequirement,
        string? title, string? scopeOfWork, string? description, string? deliverables, string? timeline,
        string? budget, string? terms, string? notes, string? status, IFormFile? attachment,
        string? taxRate, string? taxName, string? taxType)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is null) return RedirectToAction("List");

        var existing = string.IsNullOrWhiteSpace(number) ? null : lead.Proposals.FirstOrDefault(p => p.Number == number);
        var entry = existing ?? new ProposalEntry { Number = LeadStore.NextProposalNumber() };

        var (attPath, attName, attError) = await FinanceAttachmentService.SaveAsync(attachment, "proposals");
        if (attError is not null)
        {
            TempData["ProposalError"] = attError;
            return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "proposal" });
        }
        if (!string.IsNullOrWhiteSpace(attPath))
        {
            FinanceAttachmentService.Delete(entry.AttachmentPath);
            entry.AttachmentPath = attPath;
            entry.AttachmentFileName = attName;
        }

        entry.Date = !string.IsNullOrWhiteSpace(date) ? date : (!string.IsNullOrWhiteSpace(entry.Date) ? entry.Date : DateTime.UtcNow.ToString("yyyy-MM-dd"));
        entry.LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})";
        if (projectRequirement != null) entry.ProjectRequirement = projectRequirement;
        entry.Title = !string.IsNullOrWhiteSpace(title) ? title : (!string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : "Proposal");
        if (scopeOfWork != null) entry.ScopeOfWork = scopeOfWork;
        entry.Description = description ?? (notes ?? "");
        if (deliverables != null) entry.Deliverables = deliverables;
        if (timeline != null) entry.Timeline = timeline;
        if (budget != null) entry.Budget = budget;
        if (!string.IsNullOrWhiteSpace(taxRate)) entry.TaxRate = taxRate;
        if (!string.IsNullOrWhiteSpace(taxName)) entry.TaxName = taxName;
        if (!string.IsNullOrWhiteSpace(taxType)) entry.TaxType = taxType;
        if (terms != null) entry.TermsConditions = terms;
        entry.Notes = notes ?? (description ?? "");
        entry.Status = !string.IsNullOrWhiteSpace(status) ? status : (!string.IsNullOrWhiteSpace(entry.Status) ? entry.Status : "Draft");

        if (existing is null) lead.Proposals.Add(entry);

        return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "proposal" });
    }

    [HttpPost]
    public IActionResult DeleteProposal(string leadId, string number)
    {
        var lead = LeadStore.GetById(leadId);
        if (lead is not null)
        {
            var existing = lead.Proposals.FirstOrDefault(p => p.Number == number);
            if (existing is not null)
            {
                FinanceAttachmentService.Delete(existing.AttachmentPath);
                lead.Proposals.Remove(existing);
            }
        }
        return RedirectToAction("Details", new { id = leadId, tab = "finance", subtab = "proposal" });
    }

    private static decimal ParseDec(string? s) => decimal.TryParse(s, out var v) ? v : 0;

    /// <summary>Builds the Items/Services line-item list from the parallel form arrays posted by the Finance forms.</summary>
    private static List<FinanceLineItem> BuildLineItems(List<string>? names, List<string>? qtys, List<string>? rates)
    {
        var items = new List<FinanceLineItem>();
        if (names is null) return items;

        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (string.IsNullOrWhiteSpace(name)) continue;

            var qty = (qtys is not null && i < qtys.Count) ? qtys[i] : "";
            var rate = (rates is not null && i < rates.Count) ? rates[i] : "";
            var amount = ParseDec(qty) * ParseDec(rate);

            items.Add(new FinanceLineItem { ItemName = name, Quantity = qty, Rate = rate, Amount = amount.ToString("0.##") });
        }

        return items;
    }

    private IActionResult RedirectToReturnUrl(string? returnUrl, string fallbackAction)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(fallbackAction);
    }
}
