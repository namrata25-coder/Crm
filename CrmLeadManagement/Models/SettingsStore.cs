using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PasswordHash { get; set; }
    public int FailedLoginCount { get; set; } = 0;
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool MustChangePassword { get; set; } = false;
    public string? CreatedBy { get; set; }
    public DateTime? LastPasswordChangeAt { get; set; }
    public bool IsAuthorizedUser { get; set; } = false;

    /// <summary>Identifies if the account possesses Administrator authority.</summary>
    public bool IsAdminAccount =>
        string.Equals(Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Role?.Trim(), "Super Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Role?.Trim(), "Administrator", StringComparison.OrdinalIgnoreCase);

    /// <summary>Identifies if the account is authorized for User login (non-admin or explicitly authorized user).</summary>
    public bool IsUserAccount =>
        !IsAdminAccount || IsAuthorizedUser;

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

    public static string AddUser(UserRoleEntry user, string? customPassword = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.Name))
        {
            throw new ArgumentException("Name is required.", nameof(user));
        }

        var email = user.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            throw new ArgumentException("A valid email address is required.", nameof(user));
        }

        var db = DbAccessor.Db;
        var normalizedEmail = email.ToLowerInvariant();
        if (db.Users.Any(existing => existing.Email != null && existing.Email.ToLower() == normalizedEmail))
        {
            throw new DuplicateUserEmailException(email);
        }

        user.Name = user.Name.Trim();
        user.Id = 0;
        user.Email = normalizedEmail;
        user.Role = user.Role?.Trim() ?? "";
        user.Status = string.IsNullOrWhiteSpace(user.Status) ? "Active" : user.Status.Trim();
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = null;
        user.CreatedAt = DateTime.UtcNow;
        user.MustChangePassword = false;

        if (string.IsNullOrWhiteSpace(user.ModulePermissions) && !string.IsNullOrWhiteSpace(user.Role))
        {
            var roleMaster = db.RoleMasters.FirstOrDefault(r => r.RoleName == user.Role);
            if (roleMaster != null)
            {
                var perms = roleMaster.GetPermissions();
                var permittedModules = perms.Where(p => p.View).Select(p => p.Module);
                user.ModulePermissions = string.Join(",", permittedModules);
            }
        }

        var passwordToUse = !string.IsNullOrWhiteSpace(customPassword) ? customPassword.Trim() : CrmLeadManagement.Security.PasswordGenerator.Generate();
        var hasher = new PasswordHasher<UserRoleEntry>();

        if (!string.IsNullOrWhiteSpace(customPassword) && !string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var adminUser = db.Users.FirstOrDefault(u => u.Role == "Admin");
            if (adminUser != null && !string.IsNullOrWhiteSpace(adminUser.PasswordHash) &&
                hasher.VerifyHashedPassword(adminUser, adminUser.PasswordHash, passwordToUse) == PasswordVerificationResult.Success)
            {
                throw new ArgumentException("User password must be different from the administrator password.", nameof(customPassword));
            }
        }

        user.PasswordHash = hasher.HashPassword(user, passwordToUse);

        db.Users.Add(user);
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(user).State = EntityState.Detached;
            if (db.Users.Any(existing => existing.Email != null && existing.Email.ToLower() == normalizedEmail))
            {
                throw new DuplicateUserEmailException(email, ex);
            }

            throw;
        }

        return passwordToUse;
    }

    private static string GeneratePassword() => CrmLeadManagement.Security.PasswordGenerator.Generate();

    public static void RemoveUser(string email)
    {
        var db = DbAccessor.Db;
        var existing = db.Users.FirstOrDefault(u => u.Email != null && u.Email.ToLower() == email.ToLower());
        if (existing is not null)
        {
            if (string.Equals(existing.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existing.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Administrator accounts cannot be deleted.");
            }
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
                new UserRoleEntry{ Name="Ananya Sharma", Email="ananya.sharma@visioncrm.com", Role="Sales Manager", Status="Active" },
                new UserRoleEntry{ Name="Rohit Verma", Email="rohit.verma@visioncrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Priya Nair", Email="priya.nair@visioncrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Karan Mehta", Email="karan.mehta@visioncrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Sneha Kulkarni", Email="sneha.kulkarni@visioncrm.com", Role="Telecaller", Status="Active" },
                new UserRoleEntry{ Name="Arjun Desai", Email="arjun.desai@visioncrm.com", Role="Telecaller", Status="Inactive" },
            });
        }

        // Seed or update Admin from configuration ("Seed:AdminEmail", "Seed:AdminPassword"), never logging the password.
        var hasher = new PasswordHasher<UserRoleEntry>();
        var adminEmail = config?["Seed:AdminEmail"]?.Trim() ?? "admin@visioncrm.com";
        var adminPassword = config?["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";

        var existingAdmin = db.Users.FirstOrDefault(u => u.Email != null && 
            (u.Email.ToLower() == adminEmail.ToLower() || u.Email.ToLower() == "admin@nimbuscrm.com" || u.Email.ToLower() == "admin@visioncrm.com"));
        if (existingAdmin == null)
        {
            var admin = new UserRoleEntry
            {
                Name = "Administrator",
                Email = adminEmail,
                Role = "Admin",
                Status = "Active",
                IsAuthorizedUser = false
            };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
            db.Users.Add(admin);
            logger?.LogInformation("Seeded administrator account for {AdminEmail}.", adminEmail);
        }
        else
        {
            existingAdmin.Email = adminEmail;
            existingAdmin.Role = "Admin";
            existingAdmin.Status = "Active";
            existingAdmin.IsAuthorizedUser = false;
            if (string.IsNullOrWhiteSpace(existingAdmin.PasswordHash) ||
                hasher.VerifyHashedPassword(existingAdmin, existingAdmin.PasswordHash, adminPassword) != PasswordVerificationResult.Success)
            {
                existingAdmin.PasswordHash = hasher.HashPassword(existingAdmin, adminPassword);
                existingAdmin.FailedLoginCount = 0;
                existingAdmin.LockoutUntil = null;
                logger?.LogInformation("Synchronized password for administrator account {AdminEmail}.", adminEmail);
            }
        }

        // Seed or update standard User account with dedicated user role and user credentials
        var userEmail = "user@visioncrm.com";
        var userPassword = "UserPassword123!";
        var existingUser = db.Users.FirstOrDefault(u => u.Email != null && 
            (u.Email.ToLower() == userEmail.ToLower() || u.Email.ToLower() == "user@nimbuscrm.com"));
        if (existingUser == null)
        {
            var stdUser = new UserRoleEntry
            {
                Name = "Standard User",
                Email = userEmail,
                Role = "User",
                Status = "Active",
                IsAuthorizedUser = true
            };
            stdUser.PasswordHash = hasher.HashPassword(stdUser, userPassword);
            db.Users.Add(stdUser);
            logger?.LogInformation("Seeded standard user account for {UserEmail}.", userEmail);
        }
        else
        {
            existingUser.Email = userEmail;
            existingUser.Role = "User";
            existingUser.Status = "Active";
            existingUser.IsAuthorizedUser = true;
            if (string.IsNullOrWhiteSpace(existingUser.PasswordHash))
            {
                existingUser.PasswordHash = hasher.HashPassword(existingUser, userPassword);
            }
        }

        // Initialize individual distinct passwords for sample team members if they don't have a password yet,
        // or if their password inadvertently matched the administrator's password.
        foreach (var teamMember in db.Users.Where(u => u.Role != "Admin" && u.Role != "Super Admin"))
        {
            bool isMatchingAdmin = !string.IsNullOrWhiteSpace(teamMember.PasswordHash) &&
                hasher.VerifyHashedPassword(teamMember, teamMember.PasswordHash, adminPassword) == PasswordVerificationResult.Success;

            if (string.IsNullOrWhiteSpace(teamMember.PasswordHash) || isMatchingAdmin)
            {
                var firstWord = (teamMember.Name ?? "User").Split(' ')[0];
                var userDefaultPassword = $"{firstWord}@123!";
                teamMember.PasswordHash = hasher.HashPassword(teamMember, userDefaultPassword);
            }
        }

        // Migrate any remaining @nimbuscrm.com emails to @visioncrm.com in existing databases safely
        var existingEmails = db.Users.Where(u => u.Email != null).Select(u => u.Email!.ToLower()).ToHashSet();
        foreach (var u in db.Users.Where(u => u.Email != null && u.Email.Contains("@nimbuscrm.com")).ToList())
        {
            var newEmail = u.Email.Replace("@nimbuscrm.com", "@visioncrm.com");
            if (existingEmails.Contains(newEmail.ToLower()))
            {
                if (u.Email.ToLower() == "admin@nimbuscrm.com" || u.Email.ToLower() == "user@nimbuscrm.com")
                {
                    db.Users.Remove(u);
                }
                else
                {
                    u.Email = u.Email.Replace("@nimbuscrm.com", $"_{u.Id}@visioncrm.com");
                    existingEmails.Add(u.Email.ToLower());
                }
            }
            else
            {
                existingEmails.Remove(u.Email.ToLower());
                u.Email = newEmail;
                existingEmails.Add(newEmail.ToLower());
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
