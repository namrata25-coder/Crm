using Npgsql;

namespace CrmLeadManagement.Services;

/// <summary>
/// Helper to convert database connection strings from various formats (e.g., URL format used by Render/Heroku)
/// into a validated Npgsql-compatible connection string.
/// </summary>
public static class ConnectionStringHelper
{
    /// <summary>
    /// Parses a database connection URL (e.g. postgres://user:password@host:port/database?sslmode=require)
    /// or a standard key-value connection string (e.g. Host=...;Database=...) into a valid Npgsql connection string.
    /// Supports URL-encoded credentials, database names, query parameters, and appropriate SSL modes.
    /// </summary>
    public static string FromUrl(string connectionStringOrUrl)
    {
        if (string.IsNullOrWhiteSpace(connectionStringOrUrl))
        {
            return string.Empty;
        }

        var trimmed = connectionStringOrUrl.Trim();

        // 1. If already in key-value connection string format (e.g. contains "Host=" or "Server=")
        if (trimmed.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var csb = new NpgsqlConnectionStringBuilder(trimmed);
                // For remote hosts, ensure SSL is enabled by default if not explicitly specified
                if (!IsLocalHost(csb.Host) && !trimmed.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains("SslMode", StringComparison.OrdinalIgnoreCase))
                {
                    csb.SslMode = SslMode.Require;
                }
                return csb.ConnectionString;
            }
            catch
            {
                // Return trimmed as fallback if already key-value format
                return trimmed;
            }
        }

        // 2. Parse URI format (postgres:// or postgresql://)
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new FormatException(
                "Invalid database connection format. Expected a PostgreSQL URL " +
                "('postgres://user:password@host:port/database') or key-value connection string ('Host=...;Database=...').");
        }

        var builder = new NpgsqlConnectionStringBuilder();

        // Host and Port
        builder.Host = uri.Host;
        if (uri.Port > 0)
        {
            builder.Port = uri.Port;
        }
        else
        {
            builder.Port = 5432;
        }

        // Credentials (URL-decoded)
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        // Database name (from AbsolutePath, strip leading slash and URL-decode)
        var dbName = uri.AbsolutePath.TrimStart('/');
        if (!string.IsNullOrEmpty(dbName))
        {
            builder.Database = Uri.UnescapeDataString(dbName);
        }

        // Query parameters (e.g. ?sslmode=require&application_name=crm)
        bool sslModeSpecified = false;
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var query = uri.Query.TrimStart('?');
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(kv[0]).Trim().ToLowerInvariant();
                var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]).Trim() : string.Empty;

                switch (key)
                {
                    case "sslmode":
                        sslModeSpecified = true;
                        switch (value.ToLowerInvariant())
                        {
                            case "disable":
                                builder.SslMode = SslMode.Disable;
                                break;
                            case "allow":
                                builder.SslMode = SslMode.Allow;
                                break;
                            case "prefer":
                                builder.SslMode = SslMode.Prefer;
                                break;
                            case "require":
                                builder.SslMode = SslMode.Require;
                                break;
                            case "verify-ca":
                                builder.SslMode = SslMode.VerifyCA;
                                break;
                            case "verify-full":
                                builder.SslMode = SslMode.VerifyFull;
                                break;
                            default:
                                builder.SslMode = SslMode.Require;
                                break;
                        }
                        break;

                    case "application_name":
                    case "applicationname":
                        if (!string.IsNullOrEmpty(value))
                        {
                            builder.ApplicationName = value;
                        }
                        break;

                    case "searchpath":
                    case "search_path":
                        if (!string.IsNullOrEmpty(value))
                        {
                            builder.SearchPath = value;
                        }
                        break;

                    case "channelbinding":
                    case "channel_binding":
                        if (Enum.TryParse<ChannelBinding>(value, true, out var cb))
                        {
                            builder.ChannelBinding = cb;
                        }
                        break;
                }
            }
        }

        // Default SSL Mode if not explicitly provided in query parameters:
        // - Localhost (127.0.0.1 / localhost): SSL Mode=Prefer
        // - Remote / Render PostgreSQL: SSL Mode=Require
        if (!sslModeSpecified)
        {
            builder.SslMode = IsLocalHost(builder.Host) ? SslMode.Prefer : SslMode.Require;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Returns a safe description of the connection details (Host, Port, Database, Username, SSL settings)
    /// with all passwords and secrets stripped. Never logs passwords.
    /// </summary>
    public static string GetSafeConnectionDescription(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "[Not Configured]";
        }

        try
        {
            var csb = new NpgsqlConnectionStringBuilder(connectionString);
            return $"Host='{csb.Host}', Port={csb.Port}, Database='{csb.Database}', Username='{csb.Username}', SslMode={csb.SslMode}";
        }
        catch
        {
            return "[Connection string present; sensitive values redacted]";
        }
    }

    /// <summary>
    /// Checks whether the specified host points to localhost / loopback.
    /// </summary>
    public static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the specified host is a Render private network internal hostname (e.g. dpg-xxxx-a or *.render.internal).
    /// </summary>
    public static bool IsRenderInternalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        return (host.StartsWith("dpg-", StringComparison.OrdinalIgnoreCase) && !host.Contains(".render.com", StringComparison.OrdinalIgnoreCase)) ||
               host.EndsWith(".render.internal", StringComparison.OrdinalIgnoreCase);
    }
}
