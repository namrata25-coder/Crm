using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using CrmLeadManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Services;

public interface ITokenService
{
    Task<(string PlainToken, DateTime ExpiresAt)> IssueTokenAsync(int userId, string? deviceId, CancellationToken cancellationToken = default);
    Task<bool> RevokeTokenAsync(int tokenId, CancellationToken cancellationToken = default);
    Task<bool> RevokeTokenStringAsync(string tokenOrId, CancellationToken cancellationToken = default);
    Task<int> RevokeAllUserTokensAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> RevokeOtherUserTokensAsync(int userId, int currentTokenId, CancellationToken cancellationToken = default);
    Task<AccessToken?> ValidateTokenAsync(string plainToken, CancellationToken cancellationToken = default);
    Task TouchTokenAsync(int tokenId, DateTime? updatedExpiresAt = null, CancellationToken cancellationToken = default);
}

public class TokenService : ITokenService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<TokenService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(string PlainToken, DateTime ExpiresAt)> IssueTokenAsync(int userId, string? deviceId, CancellationToken cancellationToken = default)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plainToken = Base64Url.EncodeToString(tokenBytes);

        var tokenHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        var tokenHashHex = Convert.ToHexString(tokenHashBytes);

        int tokenHours = _configuration.GetValue("BearerAuth:TokenHours", 12);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(tokenHours);

        var tokenRecord = new AccessToken
        {
            UserId = userId,
            TokenHash = tokenHashHex,
            DeviceId = deviceId,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            LastUsedAt = now
        };

        _db.AccessTokens.Add(tokenRecord);
        await _db.SaveChangesAsync(cancellationToken);

        return (plainToken, expiresAt);
    }

    public async Task<bool> RevokeTokenAsync(int tokenId, CancellationToken cancellationToken = default)
    {
        var affected = await _db.AccessTokens
            .Where(t => t.Id == tokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

        return affected > 0;
    }

    public async Task<bool> RevokeTokenStringAsync(string tokenOrId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenOrId)) return false;

        if (int.TryParse(tokenOrId, out var tokenId))
        {
            return await RevokeTokenAsync(tokenId, cancellationToken);
        }

        var tokenHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(tokenOrId));
        var tokenHashHex = Convert.ToHexString(tokenHashBytes);

        var affected = await _db.AccessTokens
            .Where(t => t.TokenHash == tokenHashHex && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

        return affected > 0;
    }

    public async Task<int> RevokeAllUserTokensAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _db.AccessTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task<int> RevokeOtherUserTokensAsync(int userId, int currentTokenId, CancellationToken cancellationToken = default)
    {
        return await _db.AccessTokens
            .Where(t => t.UserId == userId && t.Id != currentTokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task<AccessToken?> ValidateTokenAsync(string plainToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainToken)) return null;

        var tokenHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        var tokenHashHex = Convert.ToHexString(tokenHashBytes);

        var accessToken = await _db.AccessTokens
            .Include(t => t.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHashHex, cancellationToken);

        if (accessToken == null) return null;

        // Constant-time check
        var storedHashBytes = Encoding.UTF8.GetBytes(accessToken.TokenHash);
        var inputHashBytes = Encoding.UTF8.GetBytes(tokenHashHex);
        if (!CryptographicOperations.FixedTimeEquals(storedHashBytes, inputHashBytes))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (accessToken.RevokedAt.HasValue || accessToken.ExpiresAt <= now)
        {
            return null;
        }

        if (accessToken.User == null || !string.Equals(accessToken.User.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return accessToken;
    }

    public async Task TouchTokenAsync(int tokenId, DateTime? updatedExpiresAt = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (updatedExpiresAt.HasValue)
        {
            await _db.AccessTokens
                .Where(t => t.Id == tokenId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.LastUsedAt, now)
                    .SetProperty(t => t.ExpiresAt, updatedExpiresAt.Value), cancellationToken);
        }
        else
        {
            await _db.AccessTokens
                .Where(t => t.Id == tokenId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.LastUsedAt, now), cancellationToken);
        }
    }
}
