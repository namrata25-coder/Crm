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

        string? connectionString = null;

        // 1. Prefer ConnectionStrings:DefaultConnection (from appsettings or ConnectionStrings__DefaultConnection env var)
        var configConn = config.GetConnectionString("DefaultConnection")
                      ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                      ?? Environment.GetEnvironmentVariable("ConnectionStrings:DefaultConnection");

        if (!string.IsNullOrWhiteSpace(configConn) && !configConn.Contains("trusted_connection", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = ConnectionStringHelper.FromUrl(configConn);
        }
        else
        {
            // 2. Otherwise, read DATABASE_URL
            var databaseUrl = config["DATABASE_URL"]
                           ?? Environment.GetEnvironmentVariable("DATABASE_URL");

            if (!string.IsNullOrWhiteSpace(databaseUrl))
            {
                connectionString = ConnectionStringHelper.FromUrl(databaseUrl);
            }
        }

        // 3. Fallback for design-time tooling when running locally without environment variables
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=127.0.0.1;Port=5432;Database=crm;Username=postgres;Password=postgres;SSL Mode=Prefer;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
