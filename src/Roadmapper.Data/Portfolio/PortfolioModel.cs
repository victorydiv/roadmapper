using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>EF mapping for the Application Portfolio module, kept apart from the roadmap mapping in <see cref="RoadmapDbContext"/>.</summary>
internal static class PortfolioModel
{
    private const int Name = PortfolioRules.MaxNameLength;
    private const int Notes = PortfolioRules.MaxNotesLength;

    public static void ConfigurePortfolio(this ModelBuilder b)
    {
        // Same conventions as the roadmap tables: enums as text, names compared case-insensitively where they must be unique.
        b.Entity<Contact>(e =>
        {
            e.Property(c => c.Name).IsRequired().HasMaxLength(Name);
            e.Property(c => c.Title).HasMaxLength(Name);
            e.Property(c => c.Email).HasMaxLength(Name);
            e.Property(c => c.Phone).HasMaxLength(50);
            e.Property(c => c.Department).HasMaxLength(100);
            e.Property(c => c.Notes).HasMaxLength(2000);
            // A vendor's people go with the vendor.
            e.HasOne(c => c.Vendor).WithMany(v => v.Staff).HasForeignKey(c => c.VendorId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.VendorId);
            e.HasIndex(c => c.Name);
        });

        b.Entity<Vendor>(e =>
        {
            e.Property(v => v.Name).IsRequired().HasMaxLength(Name).UseCollation("NOCASE");
            e.Property(v => v.Website).HasMaxLength(500);
            e.Property(v => v.SupportPortalUrl).HasMaxLength(500);
            e.Property(v => v.SupportPhone).HasMaxLength(50);
            e.Property(v => v.Notes).HasMaxLength(Notes);
            e.HasIndex(v => v.Name).IsUnique();
            e.HasOne(v => v.AccountManager).WithMany().HasForeignKey(v => v.AccountManagerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(v => v.TechnicalContact).WithMany().HasForeignKey(v => v.TechnicalContactId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<VendorEscalationContact>(e =>
        {
            e.ToTable("VendorEscalationContacts");
            e.HasKey(x => new { x.VendorId, x.ContactId });
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<Vendor>().WithMany(v => v.EscalationContacts).HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Contract>(e =>
        {
            e.Property(c => c.Name).IsRequired().HasMaxLength(Name);
            e.Property(c => c.ContractNumber).HasMaxLength(100);
            e.Property(c => c.Notes).HasMaxLength(Notes);
            e.HasOne(c => c.Vendor).WithMany(v => v.Contracts).HasForeignKey(c => c.VendorId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.VendorId);
            e.HasIndex(c => c.RenewalDate);
        });

        b.Entity<Application>(e =>
        {
            e.Property(a => a.Name).IsRequired().HasMaxLength(Name).UseCollation("NOCASE");
            e.Property(a => a.Description).HasMaxLength(Notes);
            e.Property(a => a.Notes).HasMaxLength(Notes);
            e.Property(a => a.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(a => a.Criticality).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Hosting).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(a => a.Name).IsUnique();
            e.HasIndex(a => a.Status);
            e.HasIndex(a => a.Criticality);
            // One application per product roadmap.
            e.HasIndex(a => a.ProductId).IsUnique().HasFilter("\"ProductId\" IS NOT NULL");
            e.HasOne(a => a.Vendor).WithMany(v => v.Applications).HasForeignKey(a => a.VendorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.Contract).WithMany().HasForeignKey(a => a.ContractId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.Product).WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.BusinessOwner).WithMany().HasForeignKey(a => a.BusinessOwnerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.TechnicalOwner).WithMany().HasForeignKey(a => a.TechnicalOwnerId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ApplicationSupportContact>(e =>
        {
            e.ToTable("ApplicationSupportContacts");
            e.HasKey(x => new { x.ApplicationId, x.ContactId, x.Role });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<Application>().WithMany(a => a.SupportContacts).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApplicationInitiative>(e =>
        {
            e.ToTable("ApplicationInitiatives");
            e.HasKey(x => new { x.ApplicationId, x.RoadmapItemId });
            e.Property(x => x.ImpactType).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Application).WithMany(a => a.Initiatives).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            // Deleting a roadmap item (or its product) drops its application links.
            e.HasOne(x => x.RoadmapItem).WithMany().HasForeignKey(x => x.RoadmapItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.RoadmapItemId);
        });

        b.Entity<Integration>(e =>
        {
            e.Property(i => i.Name).IsRequired().HasMaxLength(Name).UseCollation("NOCASE");
            e.Property(i => i.MessageFormat).HasMaxLength(100);
            e.Property(i => i.BusinessPurpose).IsRequired().HasMaxLength(2000);
            e.Property(i => i.Notes).HasMaxLength(Notes);
            e.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Direction).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Frequency).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Criticality).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.ReviewIntervalMonths).HasDefaultValue(12);
            // An application can't be deleted while integrations still use it; the service explains which ones.
            e.HasOne(i => i.Source).WithMany().HasForeignKey(i => i.SourceApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Target).WithMany().HasForeignKey(i => i.TargetApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Via).WithMany().HasForeignKey(i => i.ViaApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Owner).WithMany().HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(i => i.SourceApplicationId);
            e.HasIndex(i => i.TargetApplicationId);
            e.HasIndex(i => i.ViaApplicationId);
            e.ToTable(t => t.HasCheckConstraint("CK_Integrations_SourceNotTarget", "\"SourceApplicationId\" <> \"TargetApplicationId\""));
        });

        b.Entity<IntegrationRisk>(e =>
        {
            e.ToTable("IntegrationRisks", t => t.HasCheckConstraint("CK_IntegrationRisks_Scale",
                "\"Likelihood\" BETWEEN 1 AND 5 AND \"Impact\" BETWEEN 1 AND 5"));
            e.Property(r => r.Description).IsRequired().HasMaxLength(1000);
            e.Property(r => r.Mitigation).HasMaxLength(2000);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(r => r.Integration).WithMany(i => i.Risks).HasForeignKey(r => r.IntegrationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Owner).WithMany().HasForeignKey(r => r.OwnerId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => r.IntegrationId);
        });

        b.Entity<PortfolioSettings>(e =>
        {
            e.Property(s => s.Id).ValueGeneratedNever();
            e.HasData(new PortfolioSettings());
        });

        b.Entity<PortfolioAuditEntry>(e =>
        {
            e.ToTable("PortfolioAuditEntries");
            e.Property(a => a.EntityType).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.EntityName).HasMaxLength(300);
            e.Property(a => a.Field).HasMaxLength(300);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => a.ChangedAt);
        });
    }
}
