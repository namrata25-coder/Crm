using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Recurring Invoices module.
/// Base route: /api/recurring-invoices
/// </summary>
[ApiController]
[Route("api/recurring-invoices")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class RecurringInvoicesApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<RecurringInvoicesApiController> _logger;

    public RecurringInvoicesApiController(AppDbContext db, ILogger<RecurringInvoicesApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string NextProfileNumber()
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

    // GET /api/recurring-invoices/next-code
    [HttpGet("next-code")]
    public ActionResult<object> GetNextCode()
    {
        return Ok(new { profileNumber = NextProfileNumber() });
    }

    // GET /api/recurring-invoices
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RecurringInvoiceResponseDto>>> GetAll(
        [FromQuery] int? projectId,
        [FromQuery] string? status,
        [FromQuery] string? search)
    {
        try
        {
            var query = _db.WorkRecurringInvoices
                .Include(r => r.Project)
                .Include(r => r.Milestone)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(r => r.ProjectId == projectId.Value);

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(r => r.Status == status);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(r =>
                    (r.ProfileNumber != null && r.ProfileNumber.ToLower().Contains(term)) ||
                    (r.Title != null && r.Title.ToLower().Contains(term)) ||
                    (r.ClientName != null && r.ClientName.ToLower().Contains(term)) ||
                    (r.ClientCompany != null && r.ClientCompany.ToLower().Contains(term)) ||
                    (r.Project != null && r.Project.ProjectName != null && r.Project.ProjectName.ToLower().Contains(term)));
            }

            var user = PermissionChecker.GetCurrentUser(_db, User);
            var isAdmin = PermissionChecker.IsAdminOrManager(User, user);
            if (!isAdmin)
            {
                var accessibleProjectIds = await ProjectAccessPolicy.GetAccessibleProjectIdsAsync(_db, User);
                query = query.Where(r => accessibleProjectIds.Contains(r.ProjectId));
            }
            var list = await query.OrderByDescending(r => r.Id).ToListAsync();
            return Ok(list.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/recurring-invoices failed");
            return StatusCode(500, new { message = "Failed to retrieve recurring profiles", error = ex.Message });
        }
    }

    // GET /api/recurring-invoices/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RecurringInvoiceResponseDto>> GetById(int id)
    {
        try
        {
            var profile = await _db.WorkRecurringInvoices
                .Include(r => r.Project)
                .Include(r => r.Milestone)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (profile is null)
                return NotFound(new { message = $"Recurring profile with ID {id} not found." });

            var user = PermissionChecker.GetCurrentUser(_db, User);
            var isAdmin = PermissionChecker.IsAdminOrManager(User, user);
            if (!isAdmin && !ProjectAccessPolicy.CanAccess(_db, User, profile.Project))
                return Forbid();

            return Ok(MapToDto(profile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/recurring-invoices/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to retrieve profile", error = ex.Message });
        }
    }

    // POST /api/recurring-invoices
    [HttpPost]
    public async Task<ActionResult<RecurringInvoiceResponseDto>> Create([FromBody] RecurringInvoiceInputDto input)
    {
        try
        {
            if (input.ProjectId <= 0)
                return BadRequest(new { message = "Valid ProjectId is required." });

            var project = await _db.WorkProjects.FindAsync(input.ProjectId);
            if (project is null)
                return BadRequest(new { message = $"Project ID {input.ProjectId} not found." });

            var amount = Math.Max(0, input.Amount);
            var discount = Math.Max(0, input.Discount);
            if (discount > amount) discount = amount;
            var taxableAmount = Math.Max(0, amount - discount);
            var taxRate = Math.Max(0, input.TaxRate);
            var tax = Math.Round(taxableAmount * (taxRate / 100m), 2);
            var grandTotal = Math.Max(0, taxableAmount + tax);

            var profileNumber = string.IsNullOrWhiteSpace(input.ProfileNumber)
                ? NextProfileNumber()
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
                Title           = string.IsNullOrWhiteSpace(input.Title) ? $"Recurring billing for {project.ProjectName}" : input.Title.Trim(),
                ClientName      = !string.IsNullOrWhiteSpace(input.ClientName) ? input.ClientName.Trim() : (project.ClientName ?? ""),
                ClientCompany   = !string.IsNullOrWhiteSpace(input.ClientCompany) ? input.ClientCompany.Trim() : (project.ClientCompany ?? ""),
                Frequency       = string.IsNullOrWhiteSpace(input.Frequency) ? "Monthly" : input.Frequency.Trim(),
                StartDate       = startDate,
                NextInvoiceDate = nextDate,
                EndDate         = input.EndDate?.Trim() ?? "",
                Amount          = amount,
                TaxRate         = taxRate,
                Tax             = tax,
                TaxName         = input.TaxName?.Trim() ?? "",
                TaxType         = input.TaxType?.Trim() ?? "GST",
                Discount        = discount,
                GrandTotal      = grandTotal,
                Status          = string.IsNullOrWhiteSpace(input.Status) ? "Active" : input.Status.Trim(),
                TotalCycles     = input.TotalCycles > 0 ? input.TotalCycles : 12,
                CompletedCycles = 0,
                Notes           = input.Notes?.Trim() ?? "",
                CreatedDate     = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            };

            _db.WorkRecurringInvoices.Add(profile);
            await _db.SaveChangesAsync();

            await _db.Entry(profile).Reference(p => p.Project).LoadAsync();
            if (profile.MilestoneId.HasValue)
                await _db.Entry(profile).Reference(p => p.Milestone).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = profile.Id }, MapToDto(profile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/recurring-invoices failed");
            return StatusCode(500, new { message = "Failed to create recurring profile", error = ex.Message });
        }
    }

    // PUT /api/recurring-invoices/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<RecurringInvoiceResponseDto>> Update(int id, [FromBody] RecurringInvoiceInputDto input)
    {
        try
        {
            var profile = await _db.WorkRecurringInvoices
                .Include(r => r.Project)
                .Include(r => r.Milestone)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (profile is null)
                return NotFound(new { message = $"Recurring profile with ID {id} not found." });

            // Permission check: non-admin users can only modify profiles within accessible projects
            var user = PermissionChecker.GetCurrentUser(_db, User);
            var isAdmin = PermissionChecker.IsAdminOrManager(User, user);
            if (!isAdmin && !ProjectAccessPolicy.CanAccess(_db, User, profile.Project))
                return Forbid();

            if (input.ProjectId > 0 && input.ProjectId != profile.ProjectId)
            {
                var projectExists = await _db.WorkProjects.AnyAsync(p => p.Id == input.ProjectId);
                if (!projectExists)
                    return BadRequest(new { message = $"Project ID {input.ProjectId} does not exist." });
                profile.ProjectId = input.ProjectId;
            }

            if (input.MilestoneId.HasValue && input.MilestoneId.Value > 0)
                profile.MilestoneId = input.MilestoneId.Value;
            else
                profile.MilestoneId = null;

            var amount = Math.Max(0, input.Amount);
            var discount = Math.Max(0, input.Discount);
            if (discount > amount) discount = amount;
            var taxableAmount = Math.Max(0, amount - discount);
            var taxRate = Math.Max(0, input.TaxRate);
            var tax = Math.Round(taxableAmount * (taxRate / 100m), 2);
            var grandTotal = Math.Max(0, taxableAmount + tax);

            profile.Title           = string.IsNullOrWhiteSpace(input.Title) ? profile.Title : input.Title.Trim();
            profile.ClientName      = input.ClientName?.Trim() ?? profile.ClientName;
            profile.ClientCompany   = input.ClientCompany?.Trim() ?? profile.ClientCompany;
            profile.Frequency       = string.IsNullOrWhiteSpace(input.Frequency) ? profile.Frequency : input.Frequency.Trim();
            profile.StartDate       = input.StartDate?.Trim() ?? profile.StartDate;
            profile.NextInvoiceDate = input.NextInvoiceDate?.Trim() ?? profile.NextInvoiceDate;
            profile.EndDate         = input.EndDate?.Trim() ?? "";
            profile.Amount          = amount;
            profile.TaxRate         = taxRate;
            profile.Tax             = tax;
            if (input.TaxName != null) profile.TaxName = input.TaxName.Trim();
            if (input.TaxType != null) profile.TaxType = input.TaxType.Trim();
            profile.Discount        = discount;
            profile.GrandTotal      = grandTotal;
            profile.Status          = string.IsNullOrWhiteSpace(input.Status) ? profile.Status : input.Status.Trim();
            profile.TotalCycles     = input.TotalCycles > 0 ? input.TotalCycles : profile.TotalCycles;
            profile.Notes           = input.Notes?.Trim() ?? profile.Notes;

            await _db.SaveChangesAsync();

            return Ok(MapToDto(profile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /api/recurring-invoices/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to update profile", error = ex.Message });
        }
    }

    // POST /api/recurring-invoices/{id}/generate
    [HttpPost("{id:int}/generate")]
    public async Task<ActionResult<InvoiceResponseDto>> GenerateNow(int id)
    {
        try
        {
            var profile = await _db.WorkRecurringInvoices
                .Include(r => r.Project)
                .Include(r => r.Milestone)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (profile is null)
                return NotFound(new { message = $"Recurring profile with ID {id} not found." });

            var invDate = string.IsNullOrWhiteSpace(profile.NextInvoiceDate) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : profile.NextInvoiceDate;
            var dueDate = DateTime.TryParse(invDate, out var dt) ? dt.AddDays(15).ToString("yyyy-MM-dd") : invDate;

            var newInvoice = new WorkInvoice
            {
                ProjectId          = profile.ProjectId,
                MilestoneId        = profile.MilestoneId,
                InvoiceNumber      = NextInvoiceNumber(),
                ClientName         = profile.ClientName,
                ClientCompany      = profile.ClientCompany,
                InvoiceDate        = invDate,
                DueDate            = dueDate,
                Amount             = profile.Amount,
                TaxRate            = profile.TaxRate,
                Tax                = profile.Tax,
                TaxName            = profile.TaxName ?? "",
                TaxType            = profile.TaxType ?? "GST",
                Discount           = profile.Discount,
                GrandTotal         = profile.GrandTotal,
                PaidAmount         = 0,
                PaymentStatus      = "Pending",
                PaymentDate        = "",
                IsRecurring        = true,
                RecurringFrequency = profile.Frequency,
                Notes              = $"Generated from recurring schedule {profile.ProfileNumber}. {profile.Notes}".Trim(),
                CreatedDate        = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            };

            _db.WorkInvoices.Add(newInvoice);

            profile.CompletedCycles += 1;
            if (profile.CompletedCycles >= profile.TotalCycles)
            {
                profile.Status = "Completed";
            }
            else if (DateTime.TryParse(profile.NextInvoiceDate, out var nextDt))
            {
                if (string.Equals(profile.Frequency, "Yearly", StringComparison.OrdinalIgnoreCase))
                    profile.NextInvoiceDate = nextDt.AddYears(1).ToString("yyyy-MM-dd");
                else if (string.Equals(profile.Frequency, "Half-Yearly", StringComparison.OrdinalIgnoreCase))
                    profile.NextInvoiceDate = nextDt.AddMonths(6).ToString("yyyy-MM-dd");
                else if (string.Equals(profile.Frequency, "Quarterly", StringComparison.OrdinalIgnoreCase))
                    profile.NextInvoiceDate = nextDt.AddMonths(3).ToString("yyyy-MM-dd");
                else
                    profile.NextInvoiceDate = nextDt.AddMonths(1).ToString("yyyy-MM-dd");
            }

            await _db.SaveChangesAsync();

            return Ok(new { message = "Invoice generated successfully", invoiceId = newInvoice.Id, invoiceNumber = newInvoice.InvoiceNumber });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/recurring-invoices/{Id}/generate failed", id);
            return StatusCode(500, new { message = "Failed to generate invoice", error = ex.Message });
        }
    }

    // DELETE /api/recurring-invoices/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var profile = await _db.WorkRecurringInvoices
                .Include(r => r.Project)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (profile is null)
                return NotFound(new { message = $"Recurring profile with ID {id} not found." });

            var user = PermissionChecker.GetCurrentUser(_db, User);
            var isAdmin = PermissionChecker.IsAdminOrManager(User, user);
            if (!isAdmin && !ProjectAccessPolicy.CanAccess(_db, User, profile.Project))
                return Forbid();

            _db.WorkRecurringInvoices.Remove(profile);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Recurring profile deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /api/recurring-invoices/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to delete recurring profile", error = ex.Message });
        }
    }

    private static RecurringInvoiceResponseDto MapToDto(WorkRecurringInvoice r)
    {
        return new RecurringInvoiceResponseDto
        {
            Id              = r.Id,
            ProjectId       = r.ProjectId,
            ProjectName     = r.Project?.ProjectName ?? "",
            ProjectCode     = r.Project?.ProjectCode ?? "",
            MilestoneId     = r.MilestoneId,
            MilestoneName   = r.Milestone?.MilestoneName ?? "",
            ProfileNumber   = r.ProfileNumber ?? "",
            Title           = r.Title ?? "",
            ClientName      = r.ClientName ?? r.Project?.ClientName ?? "",
            ClientCompany   = r.ClientCompany ?? r.Project?.ClientCompany ?? "",
            Frequency       = r.Frequency ?? "Monthly",
            StartDate       = r.StartDate ?? "",
            NextInvoiceDate = r.NextInvoiceDate ?? "",
            EndDate         = r.EndDate ?? "",
            Amount          = r.Amount,
            TaxRate         = r.TaxRate,
            Tax             = r.Tax,
            TaxName         = r.TaxName ?? "",
            TaxType         = r.TaxType ?? "GST",
            Discount        = r.Discount,
            GrandTotal      = r.GrandTotal,
            Status          = r.Status ?? "Active",
            TotalCycles     = r.TotalCycles,
            CompletedCycles = r.CompletedCycles,
            Notes           = r.Notes ?? "",
            CreatedDate     = r.CreatedDate ?? ""
        };
    }
}

public class RecurringInvoiceInputDto
{
    public int? Id { get; set; }
    public int ProjectId { get; set; }
    public int? MilestoneId { get; set; }
    public string? ProfileNumber { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ClientName { get; set; }
    public string? ClientCompany { get; set; }
    public string? Frequency { get; set; }
    public string? StartDate { get; set; }
    public string? NextInvoiceDate { get; set; }
    public string? EndDate { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; }
    public string? TaxName { get; set; }
    public string? TaxType { get; set; }
    public decimal Discount { get; set; }
    public string? Status { get; set; }
    public int TotalCycles { get; set; }
    public string? Notes { get; set; }
}

public class RecurringInvoiceResponseDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public int? MilestoneId { get; set; }
    public string MilestoneName { get; set; } = "";
    public string ProfileNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientCompany { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string NextInvoiceDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal Tax { get; set; }
    public string TaxName { get; set; } = "";
    public string TaxType { get; set; } = "";
    public decimal Discount { get; set; }
    public decimal GrandTotal { get; set; }
    public string Status { get; set; } = "Active";
    public int TotalCycles { get; set; }
    public int CompletedCycles { get; set; }
    public string Notes { get; set; } = "";
    public string CreatedDate { get; set; } = "";
}
