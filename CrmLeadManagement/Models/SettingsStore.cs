using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace CrmLeadManagement.Models;

public class UserRoleEntry
{
    public int Id { get; set; }
    public string? Name { get; set; } = "";
    public string? Email { get; set; } = "";
    public string? Role { get; set; } = "";
    public string? Status { get; set; } = "Active"; // Active / Inactive
    public string? ModulePermissions { get; set; } = ""; // Comma-separated list of permitted module names

    // Authentication fields
    public string? PasswordHash { get; set; }
    public int FailedLoginCount { get; set; } = 0;
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public bool HasPermission(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(ModulePermissions)) return true; // Default full access if unconfigured
        var list = ModulePermissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Any(m => string.Equals(m, moduleName, StringComparison.OrdinalIgnoreCase));
    }
}

public class FollowUpSettings
{
    // Always row Id = 1 — this is a single settings record, not a list.
    public int Id { get; set; } = 1;
    public int DefaultReminderDaysBefore { get; set; } = 1;
    public bool EnableEmailReminders { get; set; } = true;
    public bool EnableSmsReminders { get; set; } = false;
    public string? DefaultFollowUpType { get; set; } = "Call";
    public int AutoEscalateAfterDaysOverdue { get; set; } = 3;
}

/// <summary>
/// Same public API as before (Statuses, Sources, Users, FollowUp, Add/Remove*), so
/// SettingsController / SettingsApiController / LookupData don't need any changes.
/// Internally this now reads/writes SQL Server through AppDbContext instead of an
/// in-memory list.
/// </summary>
public static class SettingsStore
{
    public static List<string> Statuses => DbAccessor.Db.StatusOptions.OrderBy(s => s.Id).Select(s => s.Value).ToList();

    public static List<string> Sources => DbAccessor.Db.SourceOptions.OrderBy(s => s.Id).Select(s => s.Value).ToList();

    public static List<UserRoleEntry> Users => DbAccessor.Db.Users.OrderBy(u => u.Id).ToList();

    /// <summary>Employee names, sourced from Users/Roles so Assign dropdowns stay in sync with Settings.</summary>
    public static List<string> Employees => Users.Where(u => !string.IsNullOrWhiteSpace(u.Name)).Select(u => u.Name!).ToList();

    public static FollowUpSettings FollowUp
    {
        get
        {
            var db = DbAccessor.Db;
            var row = db.FollowUpSettings.FirstOrDefault(f => f.Id == 1);
            if (row is null)
            {
                row = new FollowUpSettings { Id = 1 };
                db.FollowUpSettings.Add(row);
                db.SaveChanges();
            }
            return row;
        }
    }

    public static void AddStatus(string status)
    {
        status = status.Trim();
        if (string.IsNullOrWhiteSpace(status)) return;
        var db = DbAccessor.Db;
        if (!db.StatusOptions.Any(s => s.Value.ToLower() == status.ToLower()))
        {
            db.StatusOptions.Add(new LeadStatusOption { Value = status });
            db.SaveChanges();
        }
    }

    public static void RemoveStatus(string status)
    {
        var db = DbAccessor.Db;
        var existing = db.StatusOptions.FirstOrDefault(s => s.Value.ToLower() == status.ToLower());
        if (existing is not null)
        {
            db.StatusOptions.Remove(existing);
            db.SaveChanges();
        }
    }

    public static void AddSource(string source)
    {
        source = source.Trim();
        if (string.IsNullOrWhiteSpace(source)) return;
        var db = DbAccessor.Db;
        if (!db.SourceOptions.Any(s => s.Value.ToLower() == source.ToLower()))
        {
            db.SourceOptions.Add(new LeadSourceOption { Value = source });
            db.SaveChanges();
        }
    }

    public static void RemoveSource(string source)
    {
        var db = DbAccessor.Db;
        var existing = db.SourceOptions.FirstOrDefault(s => s.Value.ToLower() == source.ToLower());
        if (existing is not null)
        {
            db.SourceOptions.Remove(existing);
            db.SaveChanges();
        }
    }

    public static void AddUser(UserRoleEntry user)
    {
        var db = DbAccessor.Db;
        db.Users.Add(user);
        db.SaveChanges();
    }

    public static void RemoveUser(string email)
    {
        var db = DbAccessor.Db;
        var existing = db.Users.FirstOrDefault(u => u.Email != null && u.Email.ToLower() == email.ToLower());
        if (existing is not null)
        {
            db.Users.Remove(existing);
            db.SaveChanges();
        }
    }

    public static void UpdateFollowUpSettings(FollowUpSettings updated)
    {
        var db = DbAccessor.Db;
        var row = FollowUp; // ensures the row exists and is tracked
        row.DefaultReminderDaysBefore = updated.DefaultReminderDaysBefore;
        row.EnableEmailReminders = updated.EnableEmailReminders;
        row.EnableSmsReminders = updated.EnableSmsReminders;
        row.DefaultFollowUpType = updated.DefaultFollowUpType;
        row.AutoEscalateAfterDaysOverdue = updated.AutoEscalateAfterDaysOverdue;
        db.SaveChanges();
    }

    public static List<TaxMaster> Taxes => DbAccessor.Db.TaxMasters.OrderBy(t => t.TaxType).ThenBy(t => t.TaxRate).ToList();
    public static List<TaxMaster> ActiveTaxes => DbAccessor.Db.TaxMasters.Where(t => t.Status == "Active").OrderBy(t => t.TaxType).ThenBy(t => t.TaxRate).ToList();

    public static void AddTax(TaxMaster tax)
    {
        var db = DbAccessor.Db;
        tax.TaxName = tax.TaxName?.Trim() ?? "";
        tax.TaxType = tax.TaxType?.Trim() ?? "GST";
        tax.Description = tax.Description?.Trim() ?? "";
        tax.Status = string.IsNullOrWhiteSpace(tax.Status) ? "Active" : tax.Status.Trim();
        db.TaxMasters.Add(tax);
        db.SaveChanges();
    }

    public static void UpdateTax(TaxMaster tax)
    {
        var db = DbAccessor.Db;
        var existing = db.TaxMasters.FirstOrDefault(t => t.Id == tax.Id);
        if (existing is not null)
        {
            existing.TaxName = tax.TaxName?.Trim() ?? "";
            existing.TaxType = tax.TaxType?.Trim() ?? "GST";
            existing.TaxRate = tax.TaxRate;
            existing.Description = tax.Description?.Trim() ?? "";
            existing.Status = string.IsNullOrWhiteSpace(tax.Status) ? "Active" : tax.Status.Trim();
            db.SaveChanges();
        }
    }

    public static void DeleteTax(int id)
    {
        var db = DbAccessor.Db;
        var existing = db.TaxMasters.FirstOrDefault(t => t.Id == id);
        if (existing is not null)
        {
            db.TaxMasters.Remove(existing);
            db.SaveChanges();
        }
    }

    public static void ToggleTaxStatus(int id)
    {
        var db = DbAccessor.Db;
        var existing = db.TaxMasters.FirstOrDefault(t => t.Id == id);
        if (existing is not null)
        {
            existing.Status = string.Equals(existing.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
            db.SaveChanges();
        }
    }

    /// <summary>Called once at startup (see Program.cs) to seed the same sample statuses/
    /// sources/users the old in-memory version shipped with, but only if the tables are
    /// empty — so it never overwrites real data on subsequent runs.</summary>
    public static void SeedIfEmpty(AppDbContext db, IConfiguration? config = null, ILogger? logger = null)
    {
        if (!db.StatusOptions.Any())
        {
            db.StatusOptions.AddRange(
                new[] { "New", "Contacted", "Qualified", "Proposal Sent", "Negotiation", "Won", "Lost" }
                    .Select(v => new LeadStatusOption { Value = v }));
        }

        if (!db.SourceOptions.Any())
        {
            db.SourceOptions.AddRange(
                new[] { "Website", "Referral", "Cold Call", "Social Media", "Trade Show", "Advertisement", "Partner" }
                    .Select(v => new LeadSourceOption { Value = v }));
        }

        if (!db.Users.Any())
        {
            db.Users.AddRange(new List<UserRoleEntry>
            {
                new UserRoleEntry{ Name="Ananya Sharma", Email="ananya.sharma@nimbuscrm.com", Role="Sales Manager", Status="Active" },
                new UserRoleEntry{ Name="Rohit Verma", Email="rohit.verma@nimbuscrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Priya Nair", Email="priya.nair@nimbuscrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Karan Mehta", Email="karan.mehta@nimbuscrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Sneha Kulkarni", Email="sneha.kulkarni@nimbuscrm.com", Role="Telecaller", Status="Active" },
                new UserRoleEntry{ Name="Arjun Desai", Email="arjun.desai@nimbuscrm.com", Role="Telecaller", Status="Inactive" },
            });
        }

        // Seed or update Admin from configuration ("Seed:AdminEmail", "Seed:AdminPassword"), never logging the password.
        if (config != null)
        {
            var adminEmail = config["Seed:AdminEmail"]?.Trim();
            var adminPassword = config["Seed:AdminPassword"]?.Trim();

            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                var existingAdmin = db.Users.FirstOrDefault(u => u.Email != null && u.Email.ToLower() == adminEmail.ToLower());
                var hasher = new PasswordHasher<UserRoleEntry>();
                if (existingAdmin == null)
                {
                    var admin = new UserRoleEntry
                    {
                        Name = "Administrator",
                        Email = adminEmail,
                        Role = "Admin",
                        Status = "Active"
                    };
                    admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
                    db.Users.Add(admin);
                    logger?.LogInformation("Seeded administrator account for {AdminEmail}.", adminEmail);
                }
                else
                {
                    existingAdmin.Role = "Admin";
                    existingAdmin.Status = "Active";
                    existingAdmin.PasswordHash = hasher.HashPassword(existingAdmin, adminPassword);
                    logger?.LogInformation("Updated credentials for administrator account {AdminEmail}.", adminEmail);
                }

                // Also initialize default password for other active team members who don't have a password yet
                foreach (var user in db.Users.Where(u => string.IsNullOrWhiteSpace(u.PasswordHash)))
                {
                    user.PasswordHash = hasher.HashPassword(user, adminPassword);
                }
            }
        }

        if (!db.FollowUpSettings.Any())
        {
            db.FollowUpSettings.Add(new FollowUpSettings { Id = 1 });
        }

        if (!db.TaxMasters.Any())
        {
            db.TaxMasters.AddRange(new List<TaxMaster>
            {
                new TaxMaster { TaxName = "GST 18%", TaxType = "GST", TaxRate = 18.00m, Description = "Standard 18% GST (CGST 9% + SGST 9%)", Status = "Active" },
                new TaxMaster { TaxName = "GST 12%", TaxType = "GST", TaxRate = 12.00m, Description = "12% GST (CGST 6% + SGST 6%)", Status = "Active" },
                new TaxMaster { TaxName = "GST 5%", TaxType = "GST", TaxRate = 5.00m, Description = "5% Concessional GST (CGST 2.5% + SGST 2.5%)", Status = "Active" },
                new TaxMaster { TaxName = "GST 28%", TaxType = "GST", TaxRate = 28.00m, Description = "28% Luxury GST (CGST 14% + SGST 14%)", Status = "Active" },
                new TaxMaster { TaxName = "CGST 9%", TaxType = "CGST", TaxRate = 9.00m, Description = "Central GST (9% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "SGST 9%", TaxType = "SGST", TaxRate = 9.00m, Description = "State GST (9% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "CGST 6%", TaxType = "CGST", TaxRate = 6.00m, Description = "Central GST (6% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "SGST 6%", TaxType = "SGST", TaxRate = 6.00m, Description = "State GST (6% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "CGST 2.5%", TaxType = "CGST", TaxRate = 2.50m, Description = "Central GST (2.5% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "SGST 2.5%", TaxType = "SGST", TaxRate = 2.50m, Description = "State GST (2.5% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 18%", TaxType = "IGST", TaxRate = 18.00m, Description = "Integrated GST (18% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 12%", TaxType = "IGST", TaxRate = 12.00m, Description = "Integrated GST (12% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 5%", TaxType = "IGST", TaxRate = 5.00m, Description = "Integrated GST (5% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 28%", TaxType = "IGST", TaxRate = 28.00m, Description = "Integrated GST (28% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "Zero Tax 0%", TaxType = "GST", TaxRate = 0.00m, Description = "Zero Tax / Tax Exempt", Status = "Active" }
            });
        }

        db.SaveChanges();
    }
}
