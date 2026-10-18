using CrmLeadManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Services;

public interface IAuditLogService
{
    Task LogAsync(string action, string? performedBy, int? targetUserId, string? targetUserEmail, string? details, string? ipAddress = null);
    Task<List<AuditLog>> GetRecentLogsAsync(int count = 50);
}

public class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(AppDbContext db, ILogger<AuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(string action, string? performedBy, int? targetUserId, string? targetUserEmail, string? details, string? ipAddress = null)
    {
        try
        {
            var entry = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                Action = action,
                PerformedBy = performedBy ?? "System",
                TargetUserId = targetUserId,
                TargetUserEmail = targetUserEmail,
                Details = details,
                IpAddress = ipAddress
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
            _logger.LogInformation("AuditLog [{Action}] performed by {User} on target {TargetEmail}: {Details}", action, performedBy, targetUserEmail, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist AuditLog [{Action}] for target {TargetEmail}", action, targetUserEmail);
        }
    }

    public async Task<List<AuditLog>> GetRecentLogsAsync(int count = 50)
    {
        return await _db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Take(Math.Clamp(count, 1, 200))
            .ToListAsync();
    }
}
