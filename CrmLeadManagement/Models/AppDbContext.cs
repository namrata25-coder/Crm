using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CrmLeadManagement.Models;

/// <summary>
/// EF Core database context for PostgreSQL.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<UserRoleEntry> Users => Set<UserRoleEntry>();
    public DbSet<LeadStatusOption> StatusOptions => Set<LeadStatusOption>();
    public DbSet<LeadSourceOption> SourceOptions => Set<LeadSourceOption>();
    public DbSet<FollowUpSettings> FollowUpSettings => Set<FollowUpSettings>();
    public DbSet<TaxMaster> TaxMasters => Set<TaxMaster>();
    public DbSet<RoleMaster> RoleMasters => Set<RoleMaster>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<EstimationEntry> Estimations => Set<EstimationEntry>();
    public DbSet<QuotationEntry> Quotations => Set<QuotationEntry>();
    public DbSet<ProposalEntry> Proposals => Set<ProposalEntry>();
    public DbSet<FinanceLineItem> FinanceLineItems => Set<FinanceLineItem>();

    // Work Management
    public DbSet<WorkProject>          WorkProjects          => Set<WorkProject>();
    public DbSet<WorkMilestone>        WorkMilestones        => Set<WorkMilestone>();
    public DbSet<WorkTask>             WorkTasks             => Set<WorkTask>();
    public DbSet<WorkInvoice>          WorkInvoices          => Set<WorkInvoice>();
    public DbSet<WorkPayment>          WorkPayments          => Set<WorkPayment>();
    public DbSet<WorkRecurringInvoice> WorkRecurringInvoices => Set<WorkRecurringInvoice>();
    public DbSet<WorkFile>             WorkFiles             => Set<WorkFile>();
    public DbSet<WorkTimesheet>        WorkTimesheets        => Set<WorkTimesheet>();
    public DbSet<WorkExpense>          WorkExpenses          => Set<WorkExpense>();
    public DbSet<WorkNote>             WorkNotes             => Set<WorkNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var lead = modelBuilder.Entity<Lead>();
        lead.HasKey(l => l.Id);
        lead.HasIndex(l => l.LeadId).IsUnique();

        lead.OwnsMany(l => l.CallHistory,       b => b.WithOwner().HasForeignKey("LeadId_FK"));
        lead.OwnsMany(l => l.FollowUps,         b => b.WithOwner().HasForeignKey("LeadId_FK"));
        lead.OwnsMany(l => l.Requirements,      b => b.WithOwner().HasForeignKey("LeadId_FK"));
        lead.OwnsMany(l => l.Visits,            b => b.WithOwner().HasForeignKey("LeadId_FK"));
        lead.OwnsMany(l => l.AssignmentHistory, b => b.WithOwner().HasForeignKey("LeadId_FK"));

        lead.HasMany(l => l.Estimations).WithOne().HasForeignKey("LeadId_FK").OnDelete(DeleteBehavior.Cascade);
        lead.Navigation(l => l.Estimations).AutoInclude();

        lead.HasMany(l => l.Quotations).WithOne().HasForeignKey("LeadId_FK").OnDelete(DeleteBehavior.Cascade);
        lead.Navigation(l => l.Quotations).AutoInclude();

        lead.HasMany(l => l.Proposals).WithOne().HasForeignKey("LeadId_FK").OnDelete(DeleteBehavior.Cascade);
        lead.Navigation(l => l.Proposals).AutoInclude();

        modelBuilder.Entity<EstimationEntry>(est =>
        {
            est.ToTable("Estimations");
            est.HasKey(e => e.Id);
            est.HasMany(e => e.Items)
               .WithOne(i => i.Estimation)
               .HasForeignKey(i => i.EstimationId_FK)
               .HasPrincipalKey(e => e.Id)
               .OnDelete(DeleteBehavior.Restrict);
            est.Navigation(e => e.Items).AutoInclude();
        });

        modelBuilder.Entity<QuotationEntry>(quo =>
        {
            quo.ToTable("Quotations");
            quo.HasKey(q => q.Id);
            quo.HasMany(q => q.Items)
               .WithOne(i => i.Quotation)
               .HasForeignKey(i => i.QuotationId_FK)
               .HasPrincipalKey(q => q.Id)
               .OnDelete(DeleteBehavior.Restrict);
            quo.Navigation(q => q.Items).AutoInclude();
        });

        modelBuilder.Entity<ProposalEntry>(pro =>
        {
            pro.ToTable("Proposals");
            pro.HasKey(p => p.Id);
        });

        modelBuilder.Entity<FinanceLineItem>(item =>
        {
            item.ToTable("FinanceLineItems");
            item.HasKey(i => i.Id);
        });

        modelBuilder.Entity<UserRoleEntry>().HasKey(u => u.Id);
        modelBuilder.Entity<UserRoleEntry>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);

        modelBuilder.Entity<LeadStatusOption>().HasKey(s => s.Id);
        modelBuilder.Entity<LeadStatusOption>().HasIndex(s => s.Value).IsUnique();

        modelBuilder.Entity<LeadSourceOption>().HasKey(s => s.Id);
        modelBuilder.Entity<LeadSourceOption>().HasIndex(s => s.Value).IsUnique();

        modelBuilder.Entity<FollowUpSettings>().HasKey(f => f.Id);
        modelBuilder.Entity<FollowUpSettings>().Property(f => f.Id).ValueGeneratedNever();

        modelBuilder.Entity<TaxMaster>(t =>
        {
            t.ToTable("TaxMasters");
            t.HasKey(x => x.Id);
            t.Property(x => x.TaxRate).HasPrecision(18, 2);
        });

        modelBuilder.Entity<RoleMaster>(r =>
        {
            r.ToTable("RoleMasters");
            r.HasKey(x => x.Id);
            r.HasIndex(x => x.RoleName).IsUnique();
        });

        modelBuilder.Entity<AccessToken>(token =>
        {
            token.ToTable("AccessTokens");
            token.HasKey(t => t.Id);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            token.HasOne(t => t.User)
                 .WithMany()
                 .HasForeignKey(t => t.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Work Management ──────────────────────────────────────────────────

        // Project -> Milestones  (Cascade delete)
        modelBuilder.Entity<WorkProject>()
            .HasMany(p => p.Milestones)
            .WithOne(m => m.Project)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Project -> Tasks  (Cascade delete)
        modelBuilder.Entity<WorkProject>()
            .HasMany(p => p.Tasks)
            .WithOne(t => t.Project)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Milestone -> Tasks  — NoAction prevents multiple-cascade-paths SQL Server error
        modelBuilder.Entity<WorkMilestone>()
            .HasMany(m => m.Tasks)
            .WithOne(t => t.Milestone)
            .HasForeignKey(t => t.MilestoneId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        // Project -> Invoices  (Cascade delete)
        modelBuilder.Entity<WorkProject>()
            .HasMany(p => p.Invoices)
            .WithOne(i => i.Project)
            .HasForeignKey(i => i.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Invoice -> Milestone (NoAction)
        modelBuilder.Entity<WorkInvoice>()
            .HasOne(i => i.Milestone)
            .WithMany()
            .HasForeignKey(i => i.MilestoneId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<WorkInvoice>()
            .HasIndex(i => i.MilestoneId)
            .IsUnique()
            .HasFilter("\"MilestoneId\" IS NOT NULL");

        // Invoice -> Payments (Cascade delete)
        modelBuilder.Entity<WorkInvoice>()
            .HasMany(i => i.Payments)
            .WithOne(p => p.Invoice)
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Project -> Recurring Invoices (Cascade delete)
        modelBuilder.Entity<WorkRecurringInvoice>()
            .HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Recurring Invoice -> Milestone (NoAction)
        modelBuilder.Entity<WorkRecurringInvoice>()
            .HasOne(r => r.Milestone)
            .WithMany()
            .HasForeignKey(r => r.MilestoneId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        // Project -> Files  (Cascade delete)
        modelBuilder.Entity<WorkProject>()
            .HasMany(p => p.Files)
            .WithOne(f => f.Project)
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Project -> Timesheets  (Cascade delete)
        modelBuilder.Entity<WorkProject>()
            .HasMany(p => p.Timesheets)
            .WithOne(t => t.Project)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Task -> Timesheets (NoAction to prevent cascade cycle)
        modelBuilder.Entity<WorkTimesheet>()
            .HasOne(t => t.Task)
            .WithMany()
            .HasForeignKey(t => t.TaskId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        // Project -> Expenses  (Cascade delete)
        modelBuilder.Entity<WorkProject>()
            .HasMany(p => p.Expenses)
            .WithOne(e => e.Project)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Project -> WorkNotes  (Cascade delete)
        modelBuilder.Entity<WorkProject>()
            .HasMany(p => p.WorkNotes)
            .WithOne(n => n.Project)
            .HasForeignKey(n => n.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Decimal precisions
        modelBuilder.Entity<WorkProject>().Property(p => p.Budget).HasPrecision(18, 2);
        modelBuilder.Entity<WorkMilestone>().Property(m => m.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkMilestone>().Property(m => m.CompletedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkTask>().Property(t => t.TaskAmount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkTask>().Property(t => t.CompletedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkInvoice>().Property(i => i.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkInvoice>().Property(i => i.TaxRate).HasPrecision(18, 2);
        modelBuilder.Entity<WorkInvoice>().Property(i => i.Tax).HasPrecision(18, 2);
        modelBuilder.Entity<WorkInvoice>().Property(i => i.Discount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkInvoice>().Property(i => i.GrandTotal).HasPrecision(18, 2);
        modelBuilder.Entity<WorkInvoice>().Property(i => i.PaidAmount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkPayment>().Property(p => p.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkRecurringInvoice>().Property(r => r.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkRecurringInvoice>().Property(r => r.TaxRate).HasPrecision(18, 2);
        modelBuilder.Entity<WorkRecurringInvoice>().Property(r => r.Tax).HasPrecision(18, 2);
        modelBuilder.Entity<WorkRecurringInvoice>().Property(r => r.Discount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkRecurringInvoice>().Property(r => r.GrandTotal).HasPrecision(18, 2);
        modelBuilder.Entity<WorkTimesheet>().Property(t => t.HoursWorked).HasPrecision(18, 2);
        modelBuilder.Entity<WorkTimesheet>().Property(t => t.HourlyRate).HasPrecision(18, 2);
        modelBuilder.Entity<WorkTimesheet>().Property(t => t.TotalCost).HasPrecision(18, 2);
        modelBuilder.Entity<WorkExpense>().Property(e => e.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkExpense>().Property(e => e.TaxAmount).HasPrecision(18, 2);
        modelBuilder.Entity<WorkExpense>().Property(e => e.TotalAmount).HasPrecision(18, 2);

        // Auto-include child collections
        modelBuilder.Entity<WorkProject>().Navigation(p => p.Milestones).AutoInclude();
        modelBuilder.Entity<WorkProject>().Navigation(p => p.Tasks).AutoInclude();
        modelBuilder.Entity<WorkProject>().Navigation(p => p.Invoices).AutoInclude();
        modelBuilder.Entity<WorkProject>().Navigation(p => p.Files).AutoInclude();
        modelBuilder.Entity<WorkProject>().Navigation(p => p.Timesheets).AutoInclude();
        modelBuilder.Entity<WorkProject>().Navigation(p => p.Expenses).AutoInclude();
        modelBuilder.Entity<WorkProject>().Navigation(p => p.WorkNotes).AutoInclude();
        modelBuilder.Entity<WorkMilestone>().Navigation(m => m.Tasks).AutoInclude();
        modelBuilder.Entity<WorkInvoice>().Navigation(i => i.Payments).AutoInclude();

        // Enforce UTC DateTime for all PostgreSQL timestamptz columns
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                        v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
                        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v));
                }
            }
        }
    }
}

/// <summary>One selectable Lead Status value (Settings > Lead Status).</summary>
public class LeadStatusOption
{
    public int Id { get; set; }
    public string Value { get; set; } = "";
}

/// <summary>One selectable Lead Source value (Settings > Lead Sources).</summary>
public class LeadSourceOption
{
    public int Id { get; set; }
    public string Value { get; set; } = "";
}
