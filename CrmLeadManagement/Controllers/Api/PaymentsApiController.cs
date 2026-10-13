using System.Text;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for Payments module.
/// Base route: /api/payments
/// Supports payment creation, listing, invoice linking, validation against remaining balance, and status synchronization.
/// </summary>
[ApiController]
[Route("api/payments")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class PaymentsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<PaymentsApiController> _logger;

    public PaymentsApiController(AppDbContext db, ILogger<PaymentsApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string NextPaymentNumber()
    {
        var codes = _db.WorkPayments.Select(p => p.PaymentNumber).ToList();
        var max = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c =>
            {
                var trimmed = c!.Trim();
                var numPart = trimmed.StartsWith("PAY-", StringComparison.OrdinalIgnoreCase)
                    ? trimmed.Substring(4)
                    : (trimmed.StartsWith("PAY", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(3) : trimmed);
                return int.TryParse(numPart, out var v) ? v : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        int next = max + 1;
        string candidate = $"PAY-{next:D4}";
        while (codes.Any(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            next++;
            candidate = $"PAY-{next:D4}";
        }
        return candidate;
    }

    // GET /api/payments/next-code
    [HttpGet("next-code")]
    public ActionResult<object> GetNextCode()
    {
        return Ok(new { paymentNumber = NextPaymentNumber() });
    }

    // GET /api/payments
    // GET /api/payments?invoiceId=1&projectId=2&search=PAY
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PaymentResponseDto>>> GetAll(
        [FromQuery] int? invoiceId,
        [FromQuery] int? projectId,
        [FromQuery] string? method,
        [FromQuery] string? search)
    {
        try
        {
            var query = _db.WorkPayments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Project)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Milestone)
                .AsQueryable();

            if (invoiceId.HasValue && invoiceId.Value > 0)
                query = query.Where(p => p.InvoiceId == invoiceId.Value);

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(p => p.Invoice != null && p.Invoice.ProjectId == projectId.Value);

            if (!string.IsNullOrWhiteSpace(method) && !string.Equals(method, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(p => p.PaymentMethod == method);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(p =>
                    (p.PaymentNumber != null && p.PaymentNumber.ToLower().Contains(term)) ||
                    (p.TransactionReference != null && p.TransactionReference.ToLower().Contains(term)) ||
                    (p.PaymentMethod != null && p.PaymentMethod.ToLower().Contains(term)) ||
                    (p.Invoice != null && p.Invoice.InvoiceNumber != null && p.Invoice.InvoiceNumber.ToLower().Contains(term)) ||
                    (p.Invoice != null && p.Invoice.ClientName != null && p.Invoice.ClientName.ToLower().Contains(term)) ||
                    (p.Invoice != null && p.Invoice.ClientCompany != null && p.Invoice.ClientCompany.ToLower().Contains(term)) ||
                    (p.Invoice != null && p.Invoice.Project != null && p.Invoice.Project.ProjectName != null && p.Invoice.Project.ProjectName.ToLower().Contains(term)));
            }

            var payments = await query.OrderByDescending(p => p.Id).ToListAsync();
            var result = payments.Select(MapToDto).ToList();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/payments failed");
            return StatusCode(500, new { message = "Failed to retrieve payments", error = ex.Message });
        }
    }

    // GET /api/payments/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PaymentResponseDto>> GetById(int id)
    {
        try
        {
            var payment = await _db.WorkPayments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Project)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Milestone)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment is null)
                return NotFound(new { message = $"Payment with ID {id} not found." });

            return Ok(MapToDto(payment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/payments/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to retrieve payment", error = ex.Message });
        }
    }

    // GET /api/payments/invoice/{invoiceId}
    [HttpGet("invoice/{invoiceId:int}")]
    public async Task<ActionResult<IEnumerable<PaymentResponseDto>>> GetByInvoiceId(int invoiceId)
    {
        try
        {
            var payments = await _db.WorkPayments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Project)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Milestone)
                .Where(p => p.InvoiceId == invoiceId)
                .OrderByDescending(p => p.Id)
                .ToListAsync();

            return Ok(payments.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/payments/invoice/{InvoiceId} failed", invoiceId);
            return StatusCode(500, new { message = "Failed to retrieve invoice payments", error = ex.Message });
        }
    }

    // POST /api/payments
    [HttpPost]
    public async Task<ActionResult<PaymentResponseDto>> Create([FromBody] PaymentInputDto input)
    {
        try
        {
            if (input.InvoiceId <= 0)
                return BadRequest(new { message = "Valid Invoice is required." });

            if (input.Amount <= 0)
                return BadRequest(new { message = "Payment amount must be greater than zero." });

            var invoice = await _db.WorkInvoices
                .Include(i => i.Payments)
                .Include(i => i.Project)
                .Include(i => i.Milestone)
                .FirstOrDefaultAsync(i => i.Id == input.InvoiceId);

            if (invoice is null)
                return BadRequest(new { message = $"Invoice with ID {input.InvoiceId} does not exist." });

            var currentPaid = invoice.Payments.Sum(p => p.Amount);
            var remaining = Math.Max(0, invoice.GrandTotal - currentPaid);

            // Validation rule: payment amount must not exceed invoice's remaining amount
            if (input.Amount > remaining)
            {
                return BadRequest(new { message = $"Payment amount (₹{input.Amount:N2}) cannot exceed the invoice's remaining amount (₹{remaining:N2})." });
            }

            var paymentNumber = string.IsNullOrWhiteSpace(input.PaymentNumber)
                ? NextPaymentNumber()
                : input.PaymentNumber.Trim().ToUpper();

            var paymentDate = string.IsNullOrWhiteSpace(input.PaymentDate)
                ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                : input.PaymentDate.Trim();

            var payment = new WorkPayment
            {
                InvoiceId            = invoice.Id,
                PaymentNumber        = paymentNumber,
                Amount               = Math.Max(0, input.Amount),
                PaymentDate          = paymentDate,
                PaymentMethod        = string.IsNullOrWhiteSpace(input.PaymentMethod) ? "Bank Transfer" : input.PaymentMethod.Trim(),
                TransactionReference = input.TransactionReference?.Trim() ?? "",
                Notes                = input.Notes?.Trim() ?? "",
                CreatedDate          = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            };

            _db.WorkPayments.Add(payment);

            // Update invoice paid amounts and status dynamically
            invoice.Payments.Add(payment);
            var newTotalPaid = invoice.Payments.Sum(p => p.Amount);
            invoice.PaidAmount = newTotalPaid;
            invoice.PaymentDate = paymentDate;
            invoice.PaymentStatus = InvoicesApiController.ComputeDynamicStatus(invoice.GrandTotal, newTotalPaid, invoice.DueDate);

            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = payment.Id }, MapToDto(payment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /api/payments failed");
            return StatusCode(500, new { message = "Failed to create payment", error = ex.Message });
        }
    }

    // PUT /api/payments/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<PaymentResponseDto>> Update(int id, [FromBody] PaymentInputDto input)
    {
        try
        {
            var payment = await _db.WorkPayments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Payments)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Project)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Milestone)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment is null)
                return NotFound(new { message = $"Payment with ID {id} not found." });

            if (input.Amount <= 0)
                return BadRequest(new { message = "Payment amount must be greater than zero." });

            var invoice = payment.Invoice;
            if (invoice != null)
            {
                var otherPaymentsSum = invoice.Payments.Where(p => p.Id != id).Sum(p => p.Amount);
                var maxAllowed = Math.Max(0, invoice.GrandTotal - otherPaymentsSum);

                if (input.Amount > maxAllowed)
                {
                    return BadRequest(new { message = $"Payment amount (₹{input.Amount:N2}) exceeds the maximum allowed balance (₹{maxAllowed:N2})." });
                }
            }

            if (!string.IsNullOrWhiteSpace(input.PaymentNumber))
                payment.PaymentNumber = input.PaymentNumber.Trim().ToUpper();

            payment.Amount               = Math.Max(0, input.Amount);
            payment.PaymentDate          = string.IsNullOrWhiteSpace(input.PaymentDate) ? payment.PaymentDate : input.PaymentDate.Trim();
            payment.PaymentMethod        = string.IsNullOrWhiteSpace(input.PaymentMethod) ? payment.PaymentMethod : input.PaymentMethod.Trim();
            payment.TransactionReference = input.TransactionReference?.Trim() ?? payment.TransactionReference;
            payment.Notes                = input.Notes?.Trim() ?? payment.Notes;

            if (invoice != null)
            {
                var totalPaid = invoice.Payments.Sum(p => p.Id == id ? payment.Amount : p.Amount);
                invoice.PaidAmount = totalPaid;
                invoice.PaymentDate = payment.PaymentDate;
                invoice.PaymentStatus = InvoicesApiController.ComputeDynamicStatus(invoice.GrandTotal, totalPaid, invoice.DueDate);
            }

            await _db.SaveChangesAsync();

            return Ok(MapToDto(payment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /api/payments/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to update payment", error = ex.Message });
        }
    }

    // DELETE /api/payments/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var payment = await _db.WorkPayments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Payments)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment is null)
                return NotFound(new { message = $"Payment with ID {id} not found." });

            var invoice = payment.Invoice;
            _db.WorkPayments.Remove(payment);
            await _db.SaveChangesAsync();

            if (invoice != null)
            {
                var remainingPayments = await _db.WorkPayments.Where(p => p.InvoiceId == invoice.Id).ToListAsync();
                var totalPaid = remainingPayments.Sum(p => p.Amount);
                invoice.PaidAmount = totalPaid;
                invoice.PaymentDate = remainingPayments.OrderByDescending(p => p.Id).FirstOrDefault()?.PaymentDate ?? "";
                invoice.PaymentStatus = InvoicesApiController.ComputeDynamicStatus(invoice.GrandTotal, totalPaid, invoice.DueDate);

                await _db.SaveChangesAsync();
            }

            return Ok(new { message = "Payment deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /api/payments/{Id} failed", id);
            return StatusCode(500, new { message = "Failed to delete payment", error = ex.Message });
        }
    }

    // GET /api/payments/export
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? format, [FromQuery] int? projectId)
    {
        try
        {
            var query = _db.WorkPayments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Project)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i!.Milestone)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(p => p.Invoice != null && p.Invoice.ProjectId == projectId.Value);
            }

            var payments = await query.OrderByDescending(p => p.Id).ToListAsync();
            var dtos = payments.Select(MapToDto).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Payment Number,Payment Date,Invoice Number,Client,Project,Milestone,Invoice Total,Paid Amount,Remaining Due,Payment Method,Transaction Reference,Status,Notes");

            foreach (var p in dtos)
            {
                var row = string.Join(",",
                    EscapeCsv(p.PaymentNumber),
                    EscapeCsv(p.PaymentDate),
                    EscapeCsv(p.InvoiceNumber),
                    EscapeCsv(!string.IsNullOrWhiteSpace(p.ClientCompany) ? $"{p.ClientName} ({p.ClientCompany})" : p.ClientName),
                    EscapeCsv(p.ProjectName),
                    EscapeCsv(p.MilestoneName),
                    p.InvoiceGrandTotal.ToString("F2"),
                    p.Amount.ToString("F2"),
                    p.InvoiceRemainingAmount.ToString("F2"),
                    EscapeCsv(p.PaymentMethod),
                    EscapeCsv(p.TransactionReference),
                    EscapeCsv(p.InvoiceStatus),
                    EscapeCsv(p.Notes));
                sb.AppendLine(row);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var filename = projectId.HasValue ? $"project_{projectId.Value}_payments_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv" : $"payments_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /api/payments/export failed");
            return StatusCode(500, new { message = "Failed to export payments", error = ex.Message });
        }
    }

    private static string EscapeCsv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static PaymentResponseDto MapToDto(WorkPayment p)
    {
        var inv = p.Invoice;
        var totalPaid = inv?.Payments != null && inv.Payments.Any()
            ? inv.Payments.Sum(x => x.Amount)
            : (inv?.PaidAmount ?? 0);

        var grandTotal = inv?.GrandTotal ?? 0;
        var remaining = Math.Max(0, grandTotal - totalPaid);
        var dynamicStatus = InvoicesApiController.ComputeDynamicStatus(grandTotal, totalPaid, inv?.DueDate);

        var clientName = !string.IsNullOrWhiteSpace(inv?.ClientName)
            ? inv.ClientName
            : (inv?.Project?.ClientName ?? "");

        var milestoneName = inv?.Milestone?.MilestoneName ?? (inv?.MilestoneId.HasValue == true ? $"Milestone #{inv.MilestoneId}" : "Project Level");

        return new PaymentResponseDto
        {
            Id                     = p.Id,
            InvoiceId              = p.InvoiceId,
            InvoiceNumber          = inv?.InvoiceNumber ?? "",
            ProjectId              = inv?.ProjectId ?? 0,
            ProjectName            = inv?.Project?.ProjectName ?? "",
            ProjectCode            = inv?.Project?.ProjectCode ?? "",
            MilestoneId            = inv?.MilestoneId,
            MilestoneName          = milestoneName,
            ClientName             = clientName,
            ClientCompany          = inv?.ClientCompany ?? inv?.Project?.ClientCompany ?? "",
            PaymentNumber          = p.PaymentNumber ?? "",
            Amount                 = p.Amount,
            PaymentDate            = p.PaymentDate ?? "",
            PaymentMethod          = p.PaymentMethod ?? "Bank Transfer",
            TransactionReference   = p.TransactionReference ?? "",
            Notes                  = p.Notes ?? "",
            CreatedDate            = p.CreatedDate ?? "",
            InvoiceGrandTotal      = grandTotal,
            InvoicePaidAmount      = totalPaid,
            InvoiceRemainingAmount = remaining,
            InvoiceStatus          = dynamicStatus
        };
    }
}

public class PaymentInputDto
{
    public int? Id { get; set; }
    public int InvoiceId { get; set; }
    public string? PaymentNumber { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentDate { get; set; }
    public string? PaymentMethod { get; set; }
    public string? TransactionReference { get; set; }
    public string? Notes { get; set; }
}

public class PaymentResponseDto
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public int? MilestoneId { get; set; }
    public string MilestoneName { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientCompany { get; set; } = "";
    public string PaymentNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentDate { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string TransactionReference { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedDate { get; set; } = "";
    public decimal InvoiceGrandTotal { get; set; }
    public decimal InvoicePaidAmount { get; set; }
    public decimal InvoiceRemainingAmount { get; set; }
    public string InvoiceStatus { get; set; } = "Pending";
}
