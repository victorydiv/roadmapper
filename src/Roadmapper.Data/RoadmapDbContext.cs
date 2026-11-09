using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Data;

public class RoadmapDbContext(DbContextOptions<RoadmapDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<RoadmapItem> Items => Set<RoadmapItem>();
    public DbSet<ItemScore> ItemScores => Set<ItemScore>();
    public DbSet<Theme> Themes => Set<Theme>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();
    public DbSet<TaskTemplateLine> TaskTemplateLines => Set<TaskTemplateLine>();
    public DbSet<ScoringCriterion> ScoringCriteria => Set<ScoringCriterion>();
    public DbSet<ScoringCriterionOption> ScoringCriterionOptions => Set<ScoringCriterionOption>();
    public DbSet<ItemRubricAnswer> ItemRubricAnswers => Set<ItemRubricAnswer>();

    // Application Portfolio & Integration Management
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<VendorEscalationContact> VendorEscalationContacts => Set<VendorEscalationContact>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationSupportContact> ApplicationSupportContacts => Set<ApplicationSupportContact>();
    public DbSet<ApplicationInitiative> ApplicationInitiatives => Set<ApplicationInitiative>();
    public DbSet<TradingPartner> TradingPartners => Set<TradingPartner>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<IntegrationRisk> IntegrationRisks => Set<IntegrationRisk>();
    public DbSet<BusinessProcess> BusinessProcesses => Set<BusinessProcess>();
    public DbSet<ProcessApplication> ProcessApplications => Set<ProcessApplication>();
    public DbSet<ProcessIntegration> ProcessIntegrations => Set<ProcessIntegration>();
    public DbSet<ProcessPainPoint> ProcessPainPoints => Set<ProcessPainPoint>();
    public DbSet<ProcessKpi> ProcessKpis => Set<ProcessKpi>();
    public DbSet<Benefit> Benefits => Set<Benefit>();
    public DbSet<BenefitMeasurement> BenefitMeasurements => Set<BenefitMeasurement>();
    public DbSet<PortfolioSettings> PortfolioSettings => Set<PortfolioSettings>();
    public DbSet<PortfolioAuditEntry> PortfolioAuditEntries => Set<PortfolioAuditEntry>();

    /// <summary>Turned off while re-deriving rollups and scores, so history only shows deliberate edits.</summary>
    public bool AuditEnabled { get; set; } = true;

    /// <summary>Writes field-level audit rows alongside every async save. (All app writes are async.)</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (!AuditEnabled) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        ChangeTracker.DetectChanges();
        var pending = AuditTrail.Capture(ChangeTracker);
        var portfolio = PortfolioAuditTrail.Capture(ChangeTracker);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (pending.Count > 0)
            AuditEntries.AddRange(await AuditTrail.ResolveAsync(this, pending, cancellationToken));
        if (portfolio.Count > 0)
            PortfolioAuditEntries.AddRange(await PortfolioAuditTrail.ResolveAsync(this, portfolio, cancellationToken));
        if (pending.Count + portfolio.Count > 0)
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        return result;
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums are stored as text so the SQLite file stays readable in any DB browser.
        b.Entity<Product>(e =>
        {
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.ScoringModel).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.WsjfScale).HasMaxLength(200);
        });

        b.Entity<RoadmapItem>(e =>
        {
            e.ToTable("Items");
            e.Property(i => i.Title).IsRequired().HasMaxLength(300);
            e.Property(i => i.Owner).HasMaxLength(100);
            e.Property(i => i.ItemType).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Horizon).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.SourceTemplateName).HasMaxLength(TemplateRules.MaxNameLength);
            e.Property(i => i.TemplateBatchId).HasMaxLength(40);
            e.HasIndex(i => i.TemplateBatchId);

            e.HasOne(i => i.Product).WithMany(p => p.Items).HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Parent).WithMany(p => p.Children).HasForeignKey(i => i.ParentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Theme).WithMany().HasForeignKey(i => i.ThemeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.Score).WithOne().HasForeignKey<ItemScore>(s => s.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Tags).WithMany(t => t.Items).UsingEntity("ItemTags");
            e.HasIndex(i => new { i.ProductId, i.ParentId, i.SortOrder });
        });

        b.Entity<ItemScore>(e =>
        {
            e.HasKey(s => s.ItemId);
            e.Property(s => s.Moscow).HasConversion<string>().HasMaxLength(20);
            // A set of flags, so stored as a number (BusinessValue = 1, TimeCriticality = 2, RiskReduction = 4, JobSize = 8).
            e.Property(s => s.ManualFactors).HasConversion<int>();
        });

        b.Entity<Theme>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(100);
            e.Property(t => t.Color).IsRequired().HasMaxLength(20);
            e.HasOne<Product>().WithMany(p => p.Themes).HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Tag>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(100);
            e.HasOne<Product>().WithMany(p => p.Tags).HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.ProductId, t.Name }).IsUnique();
        });

        b.Entity<Dependency>(e =>
        {
            e.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne(d => d.Predecessor).WithMany(i => i.Successors).HasForeignKey(d => d.PredecessorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Successor).WithMany(i => i.Predecessors).HasForeignKey(d => d.SuccessorId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.PredecessorId, d.SuccessorId }).IsUnique();
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.ItemTitle).HasMaxLength(300);
            e.Property(a => a.Field).HasMaxLength(100);
            e.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.ProductId, a.ChangedAt });
            e.HasIndex(a => a.ItemId);
        });

        b.Entity<Snapshot>(e =>
        {
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.Note).HasMaxLength(1000);
            e.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.ProductId, s.TakenAt });
        });

        b.Entity<TaskTemplate>(e =>
        {
            // NOCASE so "EDI standard" and "EDI Standard" count as the same name.
            e.Property(t => t.Name).IsRequired().HasMaxLength(TemplateRules.MaxNameLength).UseCollation("NOCASE");
            e.Property(t => t.Description).HasMaxLength(2000);
            e.HasOne<Product>().WithMany().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.ProductId, t.Name }).IsUnique();
        });

        b.Entity<TaskTemplateLine>(e =>
        {
            e.Property(l => l.Title).IsRequired().HasMaxLength(TemplateRules.MaxTitleLength);
            e.Property(l => l.Owner).HasMaxLength(100);
            // Deleting a tag just drops it from template lines.
            e.HasMany(l => l.Tags).WithMany().UsingEntity("TaskTemplateLineTags");
            e.HasIndex(l => new { l.TemplateId, l.SortOrder });
        });

        b.Entity<ScoringCriterion>(e =>
        {
            e.ToTable("ScoringCriteria");
            e.Property(c => c.Name).IsRequired().HasMaxLength(RubricRules.MaxNameLength);
            e.Property(c => c.Help).HasMaxLength(RubricRules.MaxHelpLength);
            e.Property(c => c.Factor).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Product>().WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.Options).WithOne().HasForeignKey(o => o.CriterionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.ProductId, c.SortOrder });
        });

        b.Entity<ScoringCriterionOption>(e =>
        {
            e.ToTable("ScoringCriterionOptions");
            e.Property(o => o.Label).HasMaxLength(RubricRules.MaxNameLength);
        });

        b.Entity<ItemRubricAnswer>(e =>
        {
            e.ToTable("ItemRubricAnswers");
            e.HasKey(a => new { a.ItemId, a.CriterionId });
            e.HasOne<RoadmapItem>().WithMany(i => i.RubricAnswers).HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ScoringCriterion>().WithMany().HasForeignKey(a => a.CriterionId).OnDelete(DeleteBehavior.Cascade);
            // Removing a choice from a pick-list removes the answers that picked it.
            e.HasOne<ScoringCriterionOption>().WithMany().HasForeignKey(a => a.OptionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.ConfigurePortfolio();
    }
}
