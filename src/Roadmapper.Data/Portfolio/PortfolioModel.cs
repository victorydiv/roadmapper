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
