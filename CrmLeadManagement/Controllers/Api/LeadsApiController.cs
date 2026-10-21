using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for leads, backed by the same in-memory LeadStore used by the MVC views.
/// Base route: /api/leads
/// </summary>
[ApiController]
[Route("api/leads")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class LeadsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<LeadsApiController> _logger;

    public LeadsApiController(AppDbContext db, ILogger<LeadsApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Saves pending changes right here, inside the action, before we return any result.
    /// This is what actually fixes the "200 OK then ERR_INCOMPLETE_CHUNKED_ENCODING" symptom:
    /// previously SaveChangesAsync() only ran in a trailing middleware in Program.cs, i.e.
    /// *after* this action had already returned Ok(...) and MVC had started writing the 200
    /// response to the client. If the save failed at that point it was too late to send a
    /// proper error — the connection was simply aborted mid-stream. Saving here means a DB
    /// failure is caught before any response is written, so the caller gets a clean 500 with
    /// a complete JSON body instead of a truncated chunked response.
    /// </summary>
    private async Task<bool> TrySaveAsync(string action)
    {
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save changes while performing '{Action}'.", action);
            return false;
        }
    }

    // GET /api/leads
    // GET /api/leads?status=Qualified&assignedTo=Ananya+Sharma&search=orion
    [HttpGet]
    public ActionResult<IEnumerable<Lead>> GetAll([FromQuery] string? status, [FromQuery] string? assignedTo, [FromQuery] string? search)
    {
        var leads = LeadStore.Leads.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
            leads = leads.Where(l => string.Equals(l.LeadStatus, status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(assignedTo))
            leads = leads.Where(l => string.Equals(l.AssignedTo, assignedTo, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            leads = leads.Where(l =>
                (!string.IsNullOrEmpty(l.LeadName) && l.LeadName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(l.CompanyName) && l.CompanyName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(l.MobileNumber) && l.MobileNumber.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(l.Email) && l.Email.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return Ok(leads.ToList());
    }

    // GET /api/leads/assigned  (all leads that currently have an AssignedTo value)
    [HttpGet("assigned")]
    public ActionResult<IEnumerable<Lead>> GetAssigned()
    {
        return Ok(LeadStore.Leads.Where(l => !string.IsNullOrWhiteSpace(l.AssignedTo)).ToList());
    }

    // GET /api/leads/count
    // GET /api/leads/counts
    // GET /api/leads/stats
    // GET /api/leads/statistics
    [HttpGet("count", Order = -1)]
    [HttpGet("counts", Order = -1)]
    [HttpGet("stats", Order = -1)]
    [HttpGet("statistics", Order = -1)]
    public ActionResult<LeadCountSummaryDto> GetCount([FromQuery] string? assignedTo)
    {
        var leads = LeadStore.Leads.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(assignedTo))
        {
            leads = leads.Where(l => string.Equals(l.AssignedTo, assignedTo, StringComparison.OrdinalIgnoreCase));
        }

        var list = leads.ToList();
        var total = list.Count;
        var assigned = list.Count(l => !string.IsNullOrWhiteSpace(l.AssignedTo));
        var unassigned = total - assigned;

        var byStatus = list
            .Where(l => !string.IsNullOrWhiteSpace(l.LeadStatus))
            .GroupBy(l => l.LeadStatus!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        var byPriority = list
            .Where(l => !string.IsNullOrWhiteSpace(l.Priority))
            .GroupBy(l => l.Priority!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new LeadCountSummaryDto
        {
            Total = total,
            Assigned = assigned,
            Unassigned = unassigned,
            ByStatus = byStatus,
            ByPriority = byPriority
        });
    }

    // GET /api/leads/{id}
    [HttpGet("{id}")]
    public ActionResult<object> GetById(string id)
    {
        if (string.Equals(id, "stats", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "statistics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "count", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "counts", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(GetCount(null).Value);
        }

        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });
        return Ok(lead);
    }

    // POST /api/leads
    // POST /api/leads/add
    // POST /api/leads/create
    [HttpPost]
    [HttpPost("add")]
    [HttpPost("create")]
    public ActionResult<Lead> Create([FromBody] LeadInputModel input)
    {
        if (string.IsNullOrWhiteSpace(input.LeadName))
            return BadRequest(new ApiError { Message = "LeadName is required." });

        // Ignore any client-supplied LeadId on create; the store always assigns the next id.
        input.LeadId = "";
        var lead = LeadStore.AddLead(input.ToLead());
        return CreatedAtAction(nameof(GetById), new { id = lead.LeadId }, lead);
    }

    // PUT /api/leads/{id}
    [HttpPut("{id}")]
    public ActionResult<Lead> Update(string id, [FromBody] LeadInputModel input)
    {
        if (LeadStore.GetById(id) is null)
            return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        input.LeadId = id;
        LeadStore.UpdateLead(input.ToLead());
        return Ok(LeadStore.GetById(id));
    }

    // DELETE /api/leads/{id}
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        var removed = LeadStore.DeleteLead(id);
        if (!removed) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });
        return NoContent();
    }

    // POST /api/leads/{id}/calls
    // POST /api/leads/{id}/call-details
    [HttpPost("{id}/calls")]
    [HttpPost("{id}/call-details")]
    public async Task<IActionResult> AddCall(string id, [FromBody] AddCallRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        if (string.IsNullOrWhiteSpace(request.CallDate) || string.IsNullOrWhiteSpace(request.CallTime) || string.IsNullOrWhiteSpace(request.CallStatus))
            return BadRequest(new ApiError { Message = "CallDate, CallTime and CallStatus are required." });

        var remarkText = !string.IsNullOrWhiteSpace(request.Remark) 
            ? request.Remark 
            : (!string.IsNullOrWhiteSpace(request.Remarks) ? request.Remarks : (request.Notes ?? ""));

        var newCall = new CallHistoryEntry
        {
            CallDate = request.CallDate,
            CallTime = request.CallTime,
            CallType = string.IsNullOrWhiteSpace(request.CallType) ? "Outgoing" : request.CallType,
            CallStatus = request.CallStatus,
            Duration = request.Duration ?? "",
            Employee = lead.AssignedTo,
            Notes = remarkText
        };

        lead.CallHistory.Add(newCall);

        if (!await TrySaveAsync($"add call detail for lead '{id}'"))
        {
            return StatusCode(500, new ApiError { Message = "Failed to save call detail to database." });
        }

        return Ok(new
        {
            success = true,
            message = "Call detail saved successfully",
            leadId = lead.LeadId,
            call = newCall,
            lead = lead
        });
    }

    // POST /api/leads/{id}/followups
    [HttpPost("{id}/followups")]
    public async Task<IActionResult> AddFollowUp(string id, [FromBody] AddFollowUpRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        if (string.IsNullOrWhiteSpace(request.FollowUpDate) || string.IsNullOrWhiteSpace(request.FollowUpTime) || string.IsNullOrWhiteSpace(request.Status))
            return BadRequest(new ApiError { Message = "FollowUpDate, FollowUpTime and Status are required." });

        var newFollowUp = new FollowUpEntry
        {
            FollowUpDate = request.FollowUpDate,
            FollowUpTime = request.FollowUpTime,
            FollowUpType = string.IsNullOrWhiteSpace(request.FollowUpType) ? "Call" : request.FollowUpType,
            AssignedEmployee = lead.AssignedTo,
            Status = request.Status,
            Remarks = request.Remarks ?? ""
        };

        lead.FollowUps.Add(newFollowUp);

        // Keep the lead's "next follow-up" quick-glance field in sync with the latest entry.
        lead.FollowUpDate = request.FollowUpDate;

        if (!await TrySaveAsync($"add follow-up for lead '{id}'"))
        {
            return StatusCode(500, new ApiError { Message = "Failed to save follow-up to database." });
        }

        return Ok(new
        {
            success = true,
            message = "Follow-up saved successfully",
            leadId = lead.LeadId,
            followUp = newFollowUp,
            lead = lead
        });
    }

    // POST /api/leads/{id}/visits
    [HttpPost("{id}/visits")]
    public async Task<IActionResult> AddVisit(string id, [FromBody] AddVisitRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        if (string.IsNullOrWhiteSpace(request.VisitDate) || string.IsNullOrWhiteSpace(request.VisitTime) || string.IsNullOrWhiteSpace(request.VisitLocation))
            return BadRequest(new ApiError { Message = "VisitDate, VisitTime and VisitLocation are required." });

        var newVisit = new VisitEntry
        {
            VisitDate = request.VisitDate,
            VisitTime = request.VisitTime,
            VisitLocation = request.VisitLocation,
            Employee = !string.IsNullOrWhiteSpace(request.Employee) ? request.Employee : (lead.AssignedTo ?? ""),
            Outcome = !string.IsNullOrWhiteSpace(request.Outcome) ? request.Outcome : "Interested",
            Conversation = request.Conversation ?? "",
            Remarks = request.Remarks ?? ""
        };

        lead.Visits.Add(newVisit);

        if (!await TrySaveAsync($"add visit for lead '{id}'"))
        {
            return StatusCode(500, new ApiError { Message = "Failed to save visit to database." });
        }

        return Ok(new
        {
            success = true,
            message = "Visit saved successfully",
            leadId = lead.LeadId,
            visit = newVisit,
            lead = lead
        });
    }

    // POST /api/leads/{id}/requirements   (JSON body, no image)
    [HttpPost("{id}/requirements")]
    public ActionResult<Lead> AddRequirement(string id, [FromBody] AddRequirementRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Priority) || string.IsNullOrWhiteSpace(request.Status))
            return BadRequest(new ApiError { Message = "Title, Priority and Status are required." });

        lead.Requirements.Add(new RequirementEntry
        {
            Title = request.Title,
            Description = request.Description ?? "",
            Priority = request.Priority,
            ExpectedDate = request.ExpectedDate ?? "",
            Budget = request.Budget ?? "",
            Status = request.Status
        });

        return Ok(lead);
    }

    // POST /api/leads/{id}/requirements/upload   (multipart/form-data, with an optional image file)
    [HttpPost("{id}/requirements/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<Lead>> AddRequirementWithImage(string id, [FromForm] string title, [FromForm] string? description,
        [FromForm] string priority, [FromForm] string? expectedDate, [FromForm] string? budget, [FromForm] string status,
        IFormFile? image)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(priority) || string.IsNullOrWhiteSpace(status))
            return BadRequest(new ApiError { Message = "Title, Priority and Status are required." });

        string imagePath = "";
        if (image is not null && image.Length > 0)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext = Path.GetExtension(image.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                return BadRequest(new ApiError { Message = "Unsupported image type." });

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

        lead.Requirements.Add(new RequirementEntry
        {
            Title = title,
            Description = description ?? "",
            Priority = priority,
            ExpectedDate = expectedDate ?? "",
            Budget = budget ?? "",
            Status = status,
            ImagePath = imagePath
        });

        return Ok(lead);
    }

    // POST /api/leads/{id}/assign
    [HttpPost("{id}/assign")]
    public ActionResult<Lead> AssignLead(string id, [FromBody] AssignLeadRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        if (string.IsNullOrWhiteSpace(request.AssignedEmployee) || string.IsNullOrWhiteSpace(request.AssignmentDate))
            return BadRequest(new ApiError { Message = "AssignedEmployee and AssignmentDate are required." });

        lead.AssignmentHistory.Add(new AssignmentEntry
        {
            AssignedEmployee = request.AssignedEmployee,
            AssignmentDate = request.AssignmentDate,
            Remarks = request.Remarks ?? ""
        });

        lead.AssignedTo = request.AssignedEmployee;
        lead.AssignedDate = request.AssignmentDate;

        return Ok(lead);
    }

    // ===================== FINANCE: ESTIMATION =====================

    // GET /api/leads/{id}/estimations
    [HttpGet("{id}/estimations")]
    public ActionResult<IEnumerable<EstimationEntry>> GetEstimations(string id)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });
        return Ok(lead.Estimations);
    }

    // POST /api/leads/{id}/estimations
    [HttpPost("{id}/estimations")]
    public async Task<IActionResult> AddEstimation(string id, [FromBody] EstimationRequest request)
    {
        if (request is null || request.Items is null || request.Items.Count == 0)
            return BadRequest(new ApiError { Message = "At least one line item is required." });

        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var entry = BuildEstimation(lead, LeadStore.NextEstimationNumber(), request);
        lead.Estimations.Add(entry);

        if (!await TrySaveAsync($"create estimation for lead '{id}'"))
        {
            return StatusCode(500, new ApiError { Message = "Failed to create estimation." });
        }

        return Ok(new
        {
            success = true,
            message = "Estimation created successfully",
            id = entry.Id,
            number = entry.Number,
            estimation = entry
        });
    }

    // PUT /api/leads/{id}/estimations/{number}
    [HttpPut("{id}/estimations/{number}")]
    public async Task<IActionResult> UpdateEstimation(string id, string number, [FromBody] EstimationRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var existing = lead.Estimations.FirstOrDefault(e => e.Number == number);
        if (existing is null) return NotFound(new ApiError { Message = $"Estimation '{number}' not found." });

        var updated = BuildEstimation(lead, number, request);
        lead.Estimations[lead.Estimations.IndexOf(existing)] = updated;

        if (!await TrySaveAsync($"update estimation '{number}' for lead '{id}'"))
        {
            return StatusCode(500, new ApiError { Message = "Failed to update estimation." });
        }

        return Ok(new { success = true, message = "Estimation updated successfully", estimation = updated });
    }

    // DELETE /api/leads/{id}/estimations/{number}
    [HttpDelete("{id}/estimations/{number}")]
    public async Task<IActionResult> DeleteEstimation(string id, string number)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var existing = lead.Estimations.FirstOrDefault(e => e.Number == number);
        if (existing is null) return NotFound(new ApiError { Message = $"Estimation '{number}' not found." });

        lead.Estimations.Remove(existing);

        if (!await TrySaveAsync($"delete estimation '{number}' for lead '{id}'"))
        {
            return StatusCode(500, new ApiError { Message = "Failed to delete estimation." });
        }

        return NoContent();
    }

    // ===================== FINANCE: QUOTATION =====================

    // GET /api/leads/{id}/quotations
    [HttpGet("{id}/quotations")]
    public ActionResult<IEnumerable<QuotationEntry>> GetQuotations(string id)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });
        return Ok(lead.Quotations);
    }

    // POST /api/leads/{id}/quotations
    [HttpPost("{id}/quotations")]
    public async Task<IActionResult> AddQuotation(string id, [FromBody] QuotationRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var entry = BuildQuotation(lead, LeadStore.NextQuotationNumber(), request);
        lead.Quotations.Add(entry);

        if (!await TrySaveAsync($"create quotation for lead '{id}'"))
            return StatusCode(500, new ApiError { Message = "Failed to create quotation." });

        return Ok(new { success = true, message = "Quotation created successfully", id = entry.Id, number = entry.Number, quotation = entry });
    }

    // PUT /api/leads/{id}/quotations/{number}
    [HttpPut("{id}/quotations/{number}")]
    public async Task<IActionResult> UpdateQuotation(string id, string number, [FromBody] QuotationRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var existing = lead.Quotations.FirstOrDefault(q => q.Number == number);
        if (existing is null) return NotFound(new ApiError { Message = $"Quotation '{number}' not found." });

        var updated = BuildQuotation(lead, number, request);
        lead.Quotations[lead.Quotations.IndexOf(existing)] = updated;

        if (!await TrySaveAsync($"update quotation '{number}' for lead '{id}'"))
            return StatusCode(500, new ApiError { Message = "Failed to update quotation." });

        return Ok(new { success = true, message = "Quotation updated successfully", quotation = updated });
    }

    // DELETE /api/leads/{id}/quotations/{number}
    [HttpDelete("{id}/quotations/{number}")]
    public async Task<IActionResult> DeleteQuotation(string id, string number)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var existing = lead.Quotations.FirstOrDefault(q => q.Number == number);
        if (existing is null) return NotFound(new ApiError { Message = $"Quotation '{number}' not found." });

        lead.Quotations.Remove(existing);

        if (!await TrySaveAsync($"delete quotation '{number}' for lead '{id}'"))
            return StatusCode(500, new ApiError { Message = "Failed to delete quotation." });

        return NoContent();
    }

    // ===================== FINANCE: PROPOSAL =====================

    // GET /api/leads/{id}/proposals
    [HttpGet("{id}/proposals")]
    public ActionResult<IEnumerable<ProposalEntry>> GetProposals(string id)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });
        return Ok(lead.Proposals);
    }

    // POST /api/leads/{id}/proposals
    [HttpPost("{id}/proposals")]
    public async Task<IActionResult> AddProposal(string id, [FromBody] ProposalRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new ApiError { Message = "Title is required." });

        var entry = BuildProposal(lead, LeadStore.NextProposalNumber(), request);
        lead.Proposals.Add(entry);

        if (!await TrySaveAsync($"create proposal for lead '{id}'"))
            return StatusCode(500, new ApiError { Message = "Failed to create proposal." });

        return Ok(new { success = true, message = "Proposal created successfully", id = entry.Id, number = entry.Number, proposal = entry });
    }

    // PUT /api/leads/{id}/proposals/{number}
    [HttpPut("{id}/proposals/{number}")]
    public async Task<IActionResult> UpdateProposal(string id, string number, [FromBody] ProposalRequest request)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var existing = lead.Proposals.FirstOrDefault(p => p.Number == number);
        if (existing is null) return NotFound(new ApiError { Message = $"Proposal '{number}' not found." });

        var updated = BuildProposal(lead, number, request);
        lead.Proposals[lead.Proposals.IndexOf(existing)] = updated;

        if (!await TrySaveAsync($"update proposal '{number}' for lead '{id}'"))
            return StatusCode(500, new ApiError { Message = "Failed to update proposal." });

        return Ok(new { success = true, message = "Proposal updated successfully", proposal = updated });
    }

    // DELETE /api/leads/{id}/proposals/{number}
    [HttpDelete("{id}/proposals/{number}")]
    public async Task<IActionResult> DeleteProposal(string id, string number)
    {
        var lead = LeadStore.GetById(id);
        if (lead is null) return NotFound(new ApiError { Message = $"Lead '{id}' not found." });

        var existing = lead.Proposals.FirstOrDefault(p => p.Number == number);
        if (existing is null) return NotFound(new ApiError { Message = $"Proposal '{number}' not found." });

        lead.Proposals.Remove(existing);

        if (!await TrySaveAsync($"delete proposal '{number}' for lead '{id}'"))
            return StatusCode(500, new ApiError { Message = "Failed to delete proposal." });

        return NoContent();
    }

    private static decimal ParseDec(string? s) => decimal.TryParse(s, out var v) ? v : 0;

    private static List<FinanceLineItem> ToLineItems(List<FinanceLineItemRequest> items) =>
        items.Select(i =>
        {
            var amount = ParseDec(i.Quantity) * ParseDec(i.Rate);
            return new FinanceLineItem { ItemName = i.ItemName, Quantity = i.Quantity, Rate = i.Rate, Amount = amount.ToString("0.##") };
        }).ToList();

    private static EstimationEntry BuildEstimation(Lead lead, string number, EstimationRequest request)
    {
        var items = ToLineItems(request.Items);
        var subtotal = items.Sum(i => ParseDec(i.Amount));
        var total = subtotal - ParseDec(request.Discount) + ParseDec(request.Tax);

        return new EstimationEntry
        {
            Number = number,
            Date = request.Date,
            LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})",
            ProjectRequirement = request.ProjectRequirement,
            Items = items,
            Subtotal = subtotal.ToString("0.##"),
            Discount = request.Discount,
            Tax = request.Tax,
            Total = total.ToString("0.##"),
            ValidUntil = request.ValidUntil,
            TermsConditions = request.TermsConditions,
            Notes = request.Notes,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Draft" : request.Status
        };
    }

    private static QuotationEntry BuildQuotation(Lead lead, string number, QuotationRequest request)
    {
        var items = ToLineItems(request.Items);
        var subtotal = items.Sum(i => ParseDec(i.Amount));
        var grandTotal = subtotal - ParseDec(request.Discount) + ParseDec(request.Tax);

        return new QuotationEntry
        {
            Number = number,
            Date = request.Date,
            LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})",
            ProjectRequirement = request.ProjectRequirement,
            Items = items,
            Subtotal = subtotal.ToString("0.##"),
            Discount = request.Discount,
            Tax = request.Tax,
            GrandTotal = grandTotal.ToString("0.##"),
            ValidUntil = request.ValidUntil,
            TermsConditions = request.TermsConditions,
            Notes = request.Notes,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Draft" : request.Status
        };
    }

    private static ProposalEntry BuildProposal(Lead lead, string number, ProposalRequest request) => new()
    {
        Number = number,
        Date = request.Date,
        LeadCustomer = $"{lead.LeadName} ({lead.CompanyName})",
        ProjectRequirement = request.ProjectRequirement,
        Title = request.Title,
        ScopeOfWork = request.ScopeOfWork,
        Description = request.Description,
        Deliverables = request.Deliverables,
        Timeline = request.Timeline,
        Budget = request.Budget,
        TermsConditions = request.TermsConditions,
        Notes = request.Notes,
        Status = string.IsNullOrWhiteSpace(request.Status) ? "Draft" : request.Status
    };
}
