using System.Text;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Invoices module.
/// Base route: /api/invoices
/// Supports milestone-based invoicing (1 milestone = 1 invoice), export, PDF generation, recurring invoices, and payment integration.
/// </summary>
[ApiController]
[Route("api/invoices")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class InvoicesApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<InvoicesApiController> _logger;

    public InvoicesApiController(AppDbContext db, ILogger<InvoicesApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string NextInvoiceNumber()
    {
        var codes = _db.WorkInvoices.Select(i => i.InvoiceNumber).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c =>
            {
                var trimmed = c!.Trim();
                var numPart = trimmed.StartsWith("INV-", StringComparison.OrdinalIgnoreCase)
                    ? trimmed.Substring(4)
                    : (trimmed.StartsWith("INV", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(3) : trimmed);
                return int.TryParse(numPart, out var v) ? v : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        int next = max + 1;
        string candidate = $"INV-{next:D4}";
        while (codes.Any(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            next++;
            candidate = $"INV-{next:D4}";
        }
        return candidate;
    }

    private string NextRecurringProfileNumber()
    {
        var codes = _db.WorkRecurringInvoices.Select(r => r.ProfileNumber).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c =>
            {
                var trimmed = c!.Trim();
                var numPart = trimmed.StartsWith("REC-", StringComparison.OrdinalIgnoreCase)
                    ? trimmed.Substring(4)
                    : (trimmed.StartsWith("REC", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(3) : trimmed);
                return int.TryParse(numPart, out var v) ? v : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        int next = max + 1;
        string candidate = $"REC-{next:D4}";
        while (codes.Any(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            next++;
            candidate = $"REC-{next:D4}";
        }
        return candidate;
    }

    // GET /api/invoices/next-code
    [HttpGet("next-code")]
    public ActionResult<object> GetNextCode()
    {
        return Ok(new { invoiceNumber = NextInvoiceNumber() });
    }

    // GET /api/invoices
    // GET /api/invoices?projectId=1&status=Pending&search=INV
    [HttpGet]
    public async Task<ActionResult<IEnumerable<InvoiceResponseDto>>> GetAll(
        [FromQuery] int? projectId,
        [FromQuery] string? client,
        [FromQuery] string? status,
        [FromQuery] string? search)
    {
        try
        {
            var query = _db.WorkInvoices
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .Include(i => i.Payments)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(i => i.ProjectId == projectId.Value);

            if (!string.IsNullOrWhiteSpace(client))
            {
                var cTerm = client.Trim().ToLower();
                query = query.Where(i =>
                    (i.ClientName != null && i.ClientName.ToLower().Contains(cTerm)) ||
                    (i.ClientCompany != null && i.ClientCompany.ToLower().Contains(cTerm)) ||
                    (i.Project != null && i.Project.ClientName != null && i.Project.ClientName.ToLower().Contains(cTerm)) ||
                    (i.Project != null && i.Project.ClientCompany != null && i.Project.ClientCompany.ToLower().Contains(cTerm)));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var sTerm = search.Trim().ToLower();
                query = query.Where(i =>
                    (i.InvoiceNumber != null && i.InvoiceNumber.ToLower().Contains(sTerm)) ||
                    (i.ClientName != null && i.ClientName.ToLower().Contains(sTerm)) ||
                    (i.ClientCompany != null && i.ClientCompany.ToLower().Contains(sTerm)) ||
                    (i.Project != null && i.Project.ProjectName != null && i.Project.ProjectName.ToLower().Contains(sTerm)) ||
                    (i.Milestone != null && i.Milestone.MilestoneName != null && i.Milestone.MilestoneName.ToLower().Contains(sTerm)));
            }

            var invoices = await query.OrderByDescending(i => i.Id).ToListAsync();

            // Fetch leads for email / mobile lookup
            var leads = await _db.Leads.AsNoTracking().ToListAsync();

            var result = invoices.Select(inv => MapToDto(inv, leads)).ToList();

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(r => string.Equals(r.PaymentStatus, status, StringComparison.OrdinalIgnoreCase) ||
                                           (status.Equals("Pending", StringComparison.OrdinalIgnoreCase) && r.PaymentStatus.Equals("Unpaid", StringComparison.OrdinalIgnoreCase)) ||
                                           (status.Equals("Partial Paid", StringComparison.OrdinalIgnoreCase) && r.PaymentStatus.Equals("Partially Paid", StringComparison.OrdinalIgnoreCase))).ToList();
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/invoices failed");
            return StatusCode(500, new { message = "Failed to retrieve invoices", error = ex.Message });
        }
    }

    // GET /api/invoices/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvoiceResponseDto>> GetById(int id)
    {
        try
        {
            var inv = await _db.WorkInvoices
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (inv is null)
                return NotFound(new { message = $"Invoice with ID {id} not found." });

            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            return Ok(MapToDto(inv, leads));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/invoices/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to retrieve invoice", error = ex.Message });
        }
    }

    // GET /api/invoices/project/{projectId}
    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<IEnumerable<InvoiceResponseDto>>> GetByProjectId(int projectId)
    {
        try
        {
            var invoices = await _db.WorkInvoices
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .Include(i => i.Payments)
                .Where(i => i.ProjectId == projectId)
                .OrderByDescending(i => i.Id)
                .ToListAsync();

            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            return Ok(invoices.Select(inv => MapToDto(inv, leads)).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/invoices/project/{ProjectId} failed", projectId);
            return StatusCode(500, new { message = "Failed to retrieve project invoices", error = ex.Message });
        }
    }

    // GET /api/invoices/milestone/{milestoneId}
    [HttpGet("milestone/{milestoneId:int}")]
    public async Task<ActionResult<InvoiceResponseDto>> GetByMilestoneId(int milestoneId)
    {
        try
        {
            var invoice = await _db.WorkInvoices
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.MilestoneId == milestoneId);

            if (invoice is null)
                return NotFound(new { message = $"No invoice found for milestone ID {milestoneId}." });

            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            return Ok(MapToDto(invoice, leads));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/invoices/milestone/{MilestoneId} failed", milestoneId);
            return StatusCode(500, new { message = "Failed to retrieve milestone invoice", error = ex.Message });
        }
    }

    // POST /api/invoices
    [HttpPost]
    public async Task<ActionResult<InvoiceResponseDto>> Create([FromBody] InvoiceInputDto input)
    {
        try
        {
            if (input.ProjectId <= 0)
                return BadRequest(new { message = "Project is required." });

            var project = await _db.WorkProjects.FindAsync(input.ProjectId);
            if (project is null)
                return BadRequest(new { message = $"Project with ID {input.ProjectId} does not exist." });

            WorkMilestone? milestone = null;
            if (input.MilestoneId.HasValue && input.MilestoneId.Value > 0)
            {
                milestone = await _db.WorkMilestones
                    .Include(m => m.Tasks)
                    .FirstOrDefaultAsync(m => m.Id == input.MilestoneId.Value && m.ProjectId == input.ProjectId);
                if (milestone is null)
                    return BadRequest(new { message = $"Milestone ID {input.MilestoneId} does not belong to Project {input.ProjectId}." });

                // Rule: 1 Milestone = 1 Invoice. Duplicate milestone invoice not allowed!
                var existingMilestoneInvoice = await _db.WorkInvoices
                    .AnyAsync(i => i.MilestoneId == input.MilestoneId.Value);
                if (existingMilestoneInvoice)
                {
                    return Conflict(new { message = $"An invoice already exists for milestone '{milestone.MilestoneName}'. Duplicate milestone invoices are not allowed." });
                }
            }

            if (string.IsNullOrWhiteSpace(input.InvoiceDate))
                input.InvoiceDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

            if (string.IsNullOrWhiteSpace(input.DueDate))
                input.DueDate = DateTime.UtcNow.AddDays(15).ToString("yyyy-MM-dd");

            // Validate date rule: Due Date cannot be before Invoice Date
            if (DateTime.TryParse(input.InvoiceDate, out var invDt) && DateTime.TryParse(input.DueDate, out var dueDt))
            {
                if (dueDt.Date < invDt.Date)
                    return BadRequest(new { message = "Due Date cannot be before Invoice Date." });
            }

            var subtotal = milestone is null
                ? Math.Max(0, input.Amount)
                : (milestone.Amount > 0 ? milestone.Amount : milestone.EffectiveAmount);
            var discount = Math.Max(0, input.Discount);
            if (discount > subtotal)
                discount = subtotal;

            var taxableAmount = Math.Max(0, subtotal - discount);
            var taxRate = input.TaxRate >= 0 ? input.TaxRate : 0m;
            var taxAmount = Math.Round(taxableAmount * (taxRate / 100m), 2);
            var grandTotal = taxableAmount + taxAmount;

            var invoiceNumber = string.IsNullOrWhiteSpace(input.InvoiceNumber)
                ? NextInvoiceNumber()
                : input.InvoiceNumber.Trim().ToUpper();

            // Client fallback from project if empty
            var clientName = milestone is not null
                ? project.ClientName ?? ""
                : (!string.IsNullOrWhiteSpace(input.ClientName) ? input.ClientName.Trim() : project.ClientName ?? "");

            var clientCompany = milestone is not null
                ? project.ClientCompany ?? ""
                : (!string.IsNullOrWhiteSpace(input.ClientCompany) ? input.ClientCompany.Trim() : project.ClientCompany ?? "");

            var invoice = new WorkInvoice
            {
                ProjectId          = input.ProjectId,
                MilestoneId        = input.MilestoneId > 0 ? input.MilestoneId : null,
                InvoiceNumber      = invoiceNumber,
                ClientName         = clientName,
                ClientCompany      = clientCompany,
                InvoiceDate        = input.InvoiceDate.Trim(),
                DueDate            = input.DueDate.Trim(),
                Amount             = subtotal,
                TaxRate            = taxRate,
                Tax                = taxAmount,
                TaxName            = input.TaxName?.Trim() ?? "",
                TaxType            = input.TaxType?.Trim() ?? "GST",
                Discount           = discount,
                GrandTotal         = grandTotal,
                PaidAmount         = 0,
                PaymentStatus      = "Pending",
                PaymentDate        = "",
                IsRecurring        = input.IsRecurring,
                RecurringFrequency = input.RecurringFrequency?.Trim() ?? "",
                Notes              = input.Notes?.Trim() ?? "",
                CreatedDate        = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            };

            _db.WorkInvoices.Add(invoice);
            await _db.SaveChangesAsync();

            // Load navigations for DTO
            await _db.Entry(invoice).Reference(i => i.Project).LoadAsync();
            if (invoice.MilestoneId.HasValue)
                await _db.Entry(invoice).Reference(i => i.Milestone).LoadAsync();

            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, MapToDto(invoice, leads));
        }
        catch (DbUpdateException ex)
        {
            if (input.MilestoneId.HasValue &&
                await _db.WorkInvoices.AnyAsync(i => i.MilestoneId == input.MilestoneId.Value))
            {
                _logger.LogWarning(ex, "Duplicate invoice attempt for milestone {MilestoneId}", input.MilestoneId);
                return Conflict(new { message = "An invoice already exists for this milestone. Duplicate milestone invoices are not allowed." });
            }

            _logger.LogError(ex, "POST /api/invoices failed");
            return StatusCode(500, new { message = "Failed to create invoice", error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/invoices failed");
            return StatusCode(500, new { message = "Failed to create invoice", error = ex.Message });
        }
    }

    // POST /api/invoices/recurring
    [HttpPost("recurring")]
    public async Task<ActionResult<RecurringInvoiceResponseDto>> CreateRecurring([FromBody] RecurringInvoiceInputDto input)
    {
        try
        {
            if (input.ProjectId <= 0)
                return BadRequest(new { message = "Valid Project is required." });

            var project = await _db.WorkProjects.FindAsync(input.ProjectId);
            if (project is null)
                return BadRequest(new { message = $"Project ID {input.ProjectId} not found." });

            var subtotal = Math.Max(0, input.Amount);
            var discount = Math.Max(0, input.Discount);
            if (discount > subtotal) discount = subtotal;

            var taxableAmount = Math.Max(0, subtotal - discount);
            var taxRate = input.TaxRate >= 0 ? input.TaxRate : 0m;
            var taxAmount = Math.Round(taxableAmount * (taxRate / 100m), 2);
            var grandTotal = taxableAmount + taxAmount;

            var profileNumber = string.IsNullOrWhiteSpace(input.ProfileNumber)
                ? NextRecurringProfileNumber()
                : input.ProfileNumber.Trim().ToUpper();

            var startDate = string.IsNullOrWhiteSpace(input.StartDate)
                ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                : input.StartDate.Trim();

            var nextDate = string.IsNullOrWhiteSpace(input.NextInvoiceDate)
                ? startDate
                : input.NextInvoiceDate.Trim();

            var profile = new WorkRecurringInvoice
            {
                ProjectId       = input.ProjectId,
                MilestoneId     = input.MilestoneId > 0 ? input.MilestoneId : null,
                ProfileNumber   = profileNumber,
                Title           = string.IsNullOrWhiteSpace(input.Title) ? (string.IsNullOrWhiteSpace(input.Description) ? $"Recurring Invoice for {project.ProjectName}" : input.Description.Trim()) : input.Title.Trim(),
                ClientName      = !string.IsNullOrWhiteSpace(input.ClientName) ? input.ClientName.Trim() : (project.ClientName ?? ""),
                ClientCompany   = !string.IsNullOrWhiteSpace(input.ClientCompany) ? input.ClientCompany.Trim() : (project.ClientCompany ?? ""),
                Frequency       = string.IsNullOrWhiteSpace(input.Frequency) ? "Monthly" : input.Frequency.Trim(),
                StartDate       = startDate,
                NextInvoiceDate = nextDate,
                EndDate         = input.EndDate?.Trim() ?? "",
                Amount          = subtotal,
                TaxRate         = taxRate,
                Tax             = taxAmount,
                TaxName         = input.TaxName?.Trim() ?? "",
                TaxType         = input.TaxType?.Trim() ?? "GST",
                Discount        = discount,
                GrandTotal      = grandTotal,
                Status          = string.IsNullOrWhiteSpace(input.Status) ? "Active" : input.Status.Trim(),
                TotalCycles     = input.TotalCycles > 0 ? input.TotalCycles : 12,
                CompletedCycles = 0,
                Notes           = input.Notes?.Trim() ?? input.Description?.Trim() ?? "",
                CreatedDate     = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            };

            _db.WorkRecurringInvoices.Add(profile);
            await _db.SaveChangesAsync();

            await _db.Entry(profile).Reference(p => p.Project).LoadAsync();
            if (profile.MilestoneId.HasValue)
                await _db.Entry(profile).Reference(p => p.Milestone).LoadAsync();

            return Ok(new RecurringInvoiceResponseDto
            {
                Id              = profile.Id,
                ProjectId       = profile.ProjectId,
                ProjectName     = profile.Project?.ProjectName ?? "",
                ProjectCode     = profile.Project?.ProjectCode ?? "",
                MilestoneId     = profile.MilestoneId,
                MilestoneName   = profile.Milestone?.MilestoneName ?? "",
                ProfileNumber   = profile.ProfileNumber ?? "",
                Title           = profile.Title ?? "",
                ClientName      = profile.ClientName ?? "",
                ClientCompany   = profile.ClientCompany ?? "",
                Frequency       = profile.Frequency ?? "Monthly",
                StartDate       = profile.StartDate ?? "",
                NextInvoiceDate = profile.NextInvoiceDate ?? "",
                EndDate         = profile.EndDate ?? "",
                Amount          = profile.Amount,
                TaxRate         = profile.TaxRate,
                Tax             = profile.Tax,
                TaxName         = profile.TaxName ?? "",
                TaxType         = profile.TaxType ?? "",
                Discount        = profile.Discount,
                GrandTotal      = profile.GrandTotal,
                Status          = profile.Status ?? "Active",
                TotalCycles     = profile.TotalCycles,
                CompletedCycles = profile.CompletedCycles,
                Notes           = profile.Notes ?? "",
                CreatedDate     = profile.CreatedDate ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/invoices/recurring failed");
            return StatusCode(500, new { message = "Failed to create recurring invoice", error = ex.Message });
        }
    }

    // PUT /api/invoices/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<InvoiceResponseDto>> Update(int id, [FromBody] InvoiceInputDto input)
    {
        try
        {
            var invoice = await _db.WorkInvoices
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice is null)
                return NotFound(new { message = $"Invoice with ID {id} not found." });

            var project = invoice.Project;
            if (input.ProjectId > 0 && input.ProjectId != invoice.ProjectId)
            {
                project = await _db.WorkProjects.FirstOrDefaultAsync(p => p.Id == input.ProjectId);
                if (project is null)
                    return BadRequest(new { message = $"Project ID {input.ProjectId} does not exist." });
                invoice.ProjectId = input.ProjectId;
                invoice.Project = project;
            }

            WorkMilestone? selectedMilestone = null;
            if (input.MilestoneId.HasValue && input.MilestoneId.Value > 0)
            {
                selectedMilestone = await _db.WorkMilestones
                    .Include(m => m.Tasks)
                    .FirstOrDefaultAsync(m => m.Id == input.MilestoneId.Value && m.ProjectId == invoice.ProjectId);
                if (selectedMilestone is null)
                    return BadRequest(new { message = $"Milestone ID {input.MilestoneId} does not belong to Project {invoice.ProjectId}." });

                // Check duplicate milestone invoice
                var duplicate = await _db.WorkInvoices.AnyAsync(i => i.MilestoneId == input.MilestoneId.Value && i.Id != id);
                if (duplicate)
                    return Conflict(new { message = "An invoice already exists for this milestone. Duplicate milestone invoices are not allowed." });

                invoice.MilestoneId = input.MilestoneId.Value;
                invoice.Milestone = selectedMilestone;
            }
            else
            {
                invoice.MilestoneId = null;
                invoice.Milestone = null;
            }

            if (!string.IsNullOrWhiteSpace(input.InvoiceDate))
            {
                if (!string.IsNullOrWhiteSpace(input.DueDate) &&
                    DateTime.TryParse(input.InvoiceDate, out var invDt) &&
                    DateTime.TryParse(input.DueDate, out var dueDt) &&
                    dueDt.Date < invDt.Date)
                {
                    return BadRequest(new { message = "Due Date cannot be before Invoice Date." });
                }
                invoice.InvoiceDate = input.InvoiceDate.Trim();
            }

            if (!string.IsNullOrWhiteSpace(input.DueDate))
                invoice.DueDate = input.DueDate.Trim();

            if (!string.IsNullOrWhiteSpace(input.InvoiceNumber))
                invoice.InvoiceNumber = input.InvoiceNumber.Trim().ToUpper();

            invoice.ClientName         = selectedMilestone is not null ? project?.ClientName ?? "" : input.ClientName?.Trim() ?? invoice.ClientName;
            invoice.ClientCompany      = selectedMilestone is not null ? project?.ClientCompany ?? "" : input.ClientCompany?.Trim() ?? invoice.ClientCompany;
            
            var subtotal = selectedMilestone is null
                ? Math.Max(0, input.Amount)
                : (selectedMilestone.Amount > 0 ? selectedMilestone.Amount : selectedMilestone.EffectiveAmount);
            var discount = Math.Max(0, input.Discount);
            if (discount > subtotal) discount = subtotal;

            var taxableAmount = Math.Max(0, subtotal - discount);
            var taxRate = input.TaxRate >= 0 ? input.TaxRate : 0m;
            var taxAmount = Math.Round(taxableAmount * (taxRate / 100m), 2);
            var grandTotal = taxableAmount + taxAmount;

            invoice.Amount             = subtotal;
            invoice.TaxRate            = taxRate;
            invoice.Tax                = taxAmount;
            if (!string.IsNullOrWhiteSpace(input.TaxName)) invoice.TaxName = input.TaxName.Trim();
            if (!string.IsNullOrWhiteSpace(input.TaxType)) invoice.TaxType = input.TaxType.Trim();
            invoice.Discount           = discount;
            invoice.GrandTotal         = grandTotal;
            invoice.IsRecurring        = input.IsRecurring;
            invoice.RecurringFrequency = input.RecurringFrequency?.Trim() ?? "";
            invoice.Notes              = input.Notes?.Trim() ?? "";

            // Recalculate status and paid amounts
            var totalPaid = invoice.Payments.Sum(p => p.Amount);
            invoice.PaidAmount = totalPaid;
            invoice.PaymentStatus = ComputeDynamicStatus(grandTotal, totalPaid, invoice.DueDate);

            await _db.SaveChangesAsync();

            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            return Ok(MapToDto(invoice, leads));
        }
        catch (DbUpdateException ex)
        {
            if (input.MilestoneId.HasValue &&
                await _db.WorkInvoices.AnyAsync(i => i.MilestoneId == input.MilestoneId.Value && i.Id != id))
            {
                _logger.LogWarning(ex, "Duplicate invoice update attempt for milestone {MilestoneId}", input.MilestoneId);
                return Conflict(new { message = "An invoice already exists for this milestone. Duplicate milestone invoices are not allowed." });
            }

            _logger.LogError(ex, "PUT /api/invoices/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to update invoice", error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /api/invoices/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to update invoice", error = ex.Message });
        }
    }

    // DELETE /api/invoices/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var invoice = await _db.WorkInvoices
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice is null)
                return NotFound(new { message = $"Invoice with ID {id} not found." });

            _db.WorkInvoices.Remove(invoice);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Invoice and associated payments deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /api/invoices/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to delete invoice", error = ex.Message });
        }
    }

    // GET /api/invoices/export
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? format, [FromQuery] int? projectId)
    {
        try
        {
            var query = _db.WorkInvoices
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .Include(i => i.Payments)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(i => i.ProjectId == projectId.Value);
            }

            var invoices = await query
                .OrderByDescending(i => i.Id)
                .ToListAsync();

            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            var dtos = invoices.Select(i => MapToDto(i, leads)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Invoice Number,Client,Project,Milestone,Invoice Date,Due Date,Subtotal,Tax,Grand Total,Paid Amount,Remaining Amount,Status");

            foreach (var inv in dtos)
            {
                var row = string.Join(",",
                    EscapeCsv(inv.InvoiceNumber),
                    EscapeCsv(!string.IsNullOrWhiteSpace(inv.ClientCompany) ? $"{inv.ClientName} ({inv.ClientCompany})" : inv.ClientName),
                    EscapeCsv(inv.ProjectName),
                    EscapeCsv(inv.MilestoneName),
                    EscapeCsv(inv.InvoiceDate),
                    EscapeCsv(inv.DueDate),
                    inv.Amount.ToString("F2"),
                    $"{inv.TaxRate}% ({inv.Tax:F2})",
                    inv.GrandTotal.ToString("F2"),
                    inv.PaidAmount.ToString("F2"),
                    inv.RemainingAmount.ToString("F2"),
                    EscapeCsv(inv.PaymentStatus));
                sb.AppendLine(row);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var filename = projectId.HasValue ? $"project_{projectId.Value}_invoices_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv" : $"invoices_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/invoices/export failed");
            return StatusCode(500, new { message = "Failed to export invoices", error = ex.Message });
        }
    }

    // GET /api/invoices/{id}/pdf
    [HttpGet("{id:int}/pdf")]
    public async Task<ActionResult<InvoicePdfDto>> GetPdf(int id)
    {
        try
        {
            var inv = await _db.WorkInvoices
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (inv is null)
                return NotFound(new { message = $"Invoice with ID {id} not found." });

            var leads = await _db.Leads.AsNoTracking().ToListAsync();
            var dto = MapToDto(inv, leads);

            var taxableAmount = Math.Max(0, dto.Amount - dto.Discount);
            var taxType = (dto.TaxType ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(taxType)) taxType = "GST";

            decimal cgstRate = 0, cgstAmount = 0, sgstRate = 0, sgstAmount = 0, igstRate = 0, igstAmount = 0;

            if (taxType == "IGST")
            {
                igstRate = dto.TaxRate;
                igstAmount = dto.Tax;
            }
            else if (taxType == "CGST")
            {
                cgstRate = dto.TaxRate;
                cgstAmount = dto.Tax;
            }
            else if (taxType == "SGST")
            {
                sgstRate = dto.TaxRate;
                sgstAmount = dto.Tax;
            }
            else // Default "GST" -> Intra-state: CGST (50%) + SGST (50%)
            {
                cgstRate = Math.Round(dto.TaxRate / 2m, 2);
                cgstAmount = Math.Round(dto.Tax / 2m, 2);
                sgstRate = Math.Round(dto.TaxRate / 2m, 2);
                sgstAmount = Math.Round(dto.Tax / 2m, 2);
            }

            var pdfDto = new InvoicePdfDto
            {
                CompanyName              = "Nimbus CRM Technologies Pvt. Ltd.",
                CompanyAddress           = "Level 5, Tower B, Cyber City, Bangalore - 560103, Karnataka, India",
                CompanyEmail             = "billing@nimbuscrm.com",
                CompanyPhone             = "+91 80 4123 4567",
                CompanyGstin             = "29AABCN1234F1Z5",
                CompanyPan               = "AABCN1234F",
                CompanyWebsite           = "www.nimbuscrm.com",
                InvoiceNumber            = dto.InvoiceNumber,
                InvoiceDate              = dto.InvoiceDate,
                DueDate                  = dto.DueDate,
                ClientName               = dto.ClientName,
                ClientCompany            = dto.ClientCompany,
                ClientEmail              = dto.ClientEmail,
                ClientMobile             = dto.ClientMobile,
                ProjectName              = dto.ProjectName,
                ProjectCode              = dto.ProjectCode,
                MilestoneName            = dto.MilestoneName,
                MilestoneCode            = dto.MilestoneCode,
                MilestoneDescription      = dto.MilestoneDescription,
                MilestoneProgressPercent = dto.MilestoneProgressPercent,
                Subtotal                 = dto.Amount,
                Discount                 = dto.Discount,
                TaxableAmount            = taxableAmount,
                TaxRate                  = dto.TaxRate,
                TaxAmount                = dto.Tax,
                TaxName                  = dto.TaxName,
                TaxType                  = dto.TaxType,
                CgstRate                 = cgstRate,
                CgstAmount               = cgstAmount,
                SgstRate                 = sgstRate,
                SgstAmount               = sgstAmount,
                IgstRate                 = igstRate,
                IgstAmount               = igstAmount,
                GrandTotal               = dto.GrandTotal,
                PaidAmount               = dto.PaidAmount,
                RemainingAmount          = dto.RemainingAmount,
                PaymentStatus            = dto.PaymentStatus,
                BankName                 = "HDFC Bank Ltd.",
                BankAccountName          = "Nimbus CRM Technologies Pvt. Ltd.",
                BankAccountNumber        = "50200012345678",
                BankIfsc                 = "HDFC0000123",
                BankBranch               = "Koramangala, Bangalore - 560034",
                UpiId                    = "nimbuscrm@hdfcbank",
                PaymentHistory           = dto.Payments,
                Notes                    = dto.Notes
            };

            return Ok(pdfDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/invoices/{Id}/pdf failed", id);
            return StatusCode(500, new { message = "Failed to retrieve invoice PDF data", error = ex.Message });
        }
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    public static string ComputeDynamicStatus(decimal grandTotal, decimal paidAmount, string? dueDate)
    {
        if (paidAmount >= grandTotal && grandTotal > 0)
            return "Paid";
        if (paidAmount > 0 && paidAmount < grandTotal)
            return "Partial Paid";
        if (!string.IsNullOrWhiteSpace(dueDate) && DateTime.TryParse(dueDate, out var dDate) && dDate.Date < DateTime.UtcNow.Date && (grandTotal - paidAmount) > 0)
            return "Overdue";
        return "Pending";
    }

    private static InvoiceResponseDto MapToDto(WorkInvoice inv, List<Lead> leads)
    {
        var totalPaid = inv.Payments != null && inv.Payments.Any()
            ? inv.Payments.Sum(p => p.Amount)
            : inv.PaidAmount;

        var remaining = Math.Max(0, inv.GrandTotal - totalPaid);
        var dynamicStatus = ComputeDynamicStatus(inv.GrandTotal, totalPaid, inv.DueDate);

        var clientName = !string.IsNullOrWhiteSpace(inv.ClientName)
            ? inv.ClientName
            : (inv.Project?.ClientName ?? "");

        var clientCompany = !string.IsNullOrWhiteSpace(inv.ClientCompany)
            ? inv.ClientCompany
            : (inv.Project?.ClientCompany ?? "");

        // Find email and mobile from lead records
        var matchedLead = leads.FirstOrDefault(l =>
            (!string.IsNullOrWhiteSpace(l.LeadName) && string.Equals(l.LeadName, clientName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(l.CompanyName) && string.Equals(l.CompanyName, clientCompany, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(l.CompanyName) && string.Equals(l.CompanyName, clientName, StringComparison.OrdinalIgnoreCase)));

        var clientEmail = matchedLead?.Email ?? "";
        var clientMobile = matchedLead?.MobileNumber ?? "";

        var milestoneName = inv.Milestone?.MilestoneName ?? (inv.MilestoneId.HasValue ? $"Milestone #{inv.MilestoneId}" : "Project Level");
        var milestoneCode = inv.Milestone?.MilestoneCode ?? "";
        var milestoneDesc = inv.Milestone?.Description ?? "";
        var milestoneProgress = inv.Milestone?.ProgressPercent ?? 0;

        return new InvoiceResponseDto
        {
            Id                       = inv.Id,
            InvoiceNumber            = inv.InvoiceNumber ?? "",
            ProjectId                = inv.ProjectId,
            ProjectName              = inv.Project?.ProjectName ?? "",
            ProjectCode              = inv.Project?.ProjectCode ?? "",
            MilestoneId              = inv.MilestoneId,
            MilestoneName            = milestoneName,
            MilestoneCode            = milestoneCode,
            MilestoneDescription      = milestoneDesc,
            MilestoneProgressPercent = milestoneProgress,
            ClientName               = clientName,
            ClientCompany            = clientCompany,
            ClientEmail              = clientEmail,
            ClientMobile             = clientMobile,
            InvoiceDate              = inv.InvoiceDate ?? "",
            DueDate                  = inv.DueDate ?? "",
            Amount                   = inv.Amount,
            TaxRate                  = inv.TaxRate,
            Tax                      = inv.Tax,
            TaxName                  = inv.TaxName ?? "",
            TaxType                  = inv.TaxType ?? "",
            Discount                 = inv.Discount,
            GrandTotal               = inv.GrandTotal,
            PaidAmount               = totalPaid,
            RemainingAmount          = remaining,
            PaymentStatus            = dynamicStatus,
            PaymentDate              = inv.PaymentDate ?? "",
            IsRecurring              = inv.IsRecurring,
            RecurringFrequency       = inv.RecurringFrequency ?? "",
            Notes                    = inv.Notes ?? "",
            CreatedDate              = inv.CreatedDate ?? "",
            Payments                 = (inv.Payments ?? new List<WorkPayment>()).Select(p => new PaymentItemDto
            {
                Id                   = p.Id,
                InvoiceId            = p.InvoiceId,
                PaymentNumber        = p.PaymentNumber ?? "",
                Amount               = p.Amount,
                PaymentDate          = p.PaymentDate ?? "",
                PaymentMethod        = p.PaymentMethod ?? "Bank Transfer",
                TransactionReference = p.TransactionReference ?? "",
                Notes                = p.Notes ?? "",
                CreatedDate          = p.CreatedDate ?? ""
            }).OrderByDescending(p => p.Id).ToList()
        };
    }
}

public class InvoiceInputDto
{
    public int? Id { get; set; }
    public int ProjectId { get; set; }
    public int? MilestoneId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? ClientName { get; set; }
    public string? ClientCompany { get; set; }
    public string? InvoiceDate { get; set; }
    public string? DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; }
    public string? TaxName { get; set; }
    public string? TaxType { get; set; }
    public decimal Discount { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurringFrequency { get; set; }
    public string? Notes { get; set; }
}

public class InvoiceResponseDto
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public int? MilestoneId { get; set; }
    public string MilestoneName { get; set; } = "";
    public string MilestoneCode { get; set; } = "";
    public string MilestoneDescription { get; set; } = "";
    public int MilestoneProgressPercent { get; set; }
    public string ClientName { get; set; } = "";
    public string ClientCompany { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientMobile { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string DueDate { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal Tax { get; set; }
    public string TaxName { get; set; } = "";
    public string TaxType { get; set; } = "";
    public decimal Discount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string PaymentStatus { get; set; } = "Pending";
    public string PaymentDate { get; set; } = "";
    public bool IsRecurring { get; set; }
    public string RecurringFrequency { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedDate { get; set; } = "";
    public List<PaymentItemDto> Payments { get; set; } = new();
    public int PaymentsCount => Payments?.Count ?? 0;
}

public class PaymentItemDto
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string PaymentNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentDate { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string TransactionReference { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedDate { get; set; } = "";
}

public class InvoicePdfDto
{
    public string CompanyName { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string CompanyEmail { get; set; } = "";
    public string CompanyPhone { get; set; } = "";
    public string CompanyGstin { get; set; } = "";
    public string CompanyPan { get; set; } = "";
    public string CompanyWebsite { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientCompany { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientMobile { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string MilestoneName { get; set; } = "";
    public string MilestoneCode { get; set; } = "";
    public string MilestoneDescription { get; set; } = "";
    public int MilestoneProgressPercent { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public string TaxName { get; set; } = "";
    public string TaxType { get; set; } = "";
    public decimal CgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstRate { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstRate { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string BankName { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string BankAccountNumber { get; set; } = "";
    public string BankIfsc { get; set; } = "";
    public string BankBranch { get; set; } = "";
    public string UpiId { get; set; } = "";
    public List<PaymentItemDto> PaymentHistory { get; set; } = new();
    public string Notes { get; set; } = "";
}
