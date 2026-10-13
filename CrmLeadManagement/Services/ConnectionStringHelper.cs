namespace CrmLeadManagement.Services;

/// <summary>
/// Helper to convert database connection strings from various formats (e.g., URL format used by Render/Heroku)
/// into an Npgsql-compatible connection string.
/// </summary>
public static class ConnectionStringHelper
{
    /// <summary>
    /// Parses a database connection URL (e.g. postgres://user:password@host:port/database)
    /// into a valid Npgsql connection string with SSL Mode=Require and Trust Server Certificate=true.
    /// If the input is already in key-value connection string format, returns it as-is.
    /// </summary>
    public static string FromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        // If already in key-value format (e.g. "Host=..."), return directly
        if (url.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new FormatException($"Invalid database URL format: '{url}'. Expected format 'postgres://user:pass@host:port/database'.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;";
    }
}
