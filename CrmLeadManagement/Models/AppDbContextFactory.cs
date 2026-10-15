using CrmLeadManagement.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CrmLeadManagement.Models;

/// <summary>
/// Design-time DbContext factory used by EF Core command line tools (e.g. dotnet ef migrations).
/// Discovers the connection string from environment variables or configuration files.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        string? connectionString = null;

        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            connectionString = ConnectionStringHelper.FromUrl(databaseUrl);
        }
        else
        {
            var configConn = config.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(configConn) && !configConn.Contains("trusted_connection", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = configConn;
            }
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fallback for design-time tooling when no environment variable is provided
            connectionString = "Host=localhost;Port=5432;Database=crm;Username=postgres;Password=postgres";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
