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
            // A trading partner's people go with the partner.
            e.HasOne(c => c.TradingPartner).WithMany(p => p.Staff).HasForeignKey(c => c.TradingPartnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.VendorId);
            e.HasIndex(c => c.TradingPartnerId);
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

        b.Entity<TradingPartner>(e =>
        {
            e.ToTable("TradingPartners");
            e.Property(p => p.Name).IsRequired().HasMaxLength(Name).UseCollation("NOCASE");
            e.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Importance).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.AccountCode).HasMaxLength(100);
            e.Property(p => p.EdiQualifier).HasMaxLength(2);
            e.Property(p => p.EdiId).HasMaxLength(35);
            e.Property(p => p.GsId).HasMaxLength(35);
            e.Property(p => p.Notes).HasMaxLength(Notes);
            e.HasIndex(p => p.Name).IsUnique();
            e.HasOne(p => p.RelationshipOwner).WithMany().HasForeignKey(p => p.RelationshipOwnerId).OnDelete(DeleteBehavior.SetNull);
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
            // Likewise a trading partner with integrations.
            e.HasOne(i => i.SourcePartner).WithMany().HasForeignKey(i => i.SourcePartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.TargetPartner).WithMany().HasForeignKey(i => i.TargetPartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.SourcePartnerId);
            e.HasIndex(i => i.TargetPartnerId);
            e.HasOne(i => i.Owner).WithMany().HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(i => i.SourceApplicationId);
            e.HasIndex(i => i.TargetApplicationId);
            e.HasIndex(i => i.ViaApplicationId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Integrations_SourceNotTarget", "\"SourceApplicationId\" <> \"TargetApplicationId\"");
                // Each end is exactly one of: an application, a trading partner.
                t.HasCheckConstraint("CK_Integrations_OneSource", "(\"SourceApplicationId\" IS NULL) <> (\"SourcePartnerId\" IS NULL)");
                t.HasCheckConstraint("CK_Integrations_OneTarget", "(\"TargetApplicationId\" IS NULL) <> (\"TargetPartnerId\" IS NULL)");
            });
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

        b.Entity<BusinessProcess>(e =>
        {
            e.ToTable("BusinessProcesses");
            e.Property(p => p.Name).IsRequired().HasMaxLength(Name).UseCollation("NOCASE");
            e.Property(p => p.Description).HasMaxLength(Notes);
            e.Property(p => p.Domain).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.ParentProcessId);
            // A process with sub-processes can't be deleted until they're moved or deleted; the service explains.
            e.HasOne(p => p.ParentProcess).WithMany(p => p.Children).HasForeignKey(p => p.ParentProcessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.ProcessOwner).WithMany().HasForeignKey(p => p.ProcessOwnerId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ProcessApplication>(e =>
        {
            e.ToTable("ProcessApplications");
            e.HasKey(x => new { x.ProcessId, x.ApplicationId });
            e.Property(x => x.Usage).HasMaxLength(500);
            e.HasOne(x => x.Process).WithMany(p => p.Applications).HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ApplicationId);
        });

        b.Entity<ProcessIntegration>(e =>
        {
            e.ToTable("ProcessIntegrations");
            e.HasKey(x => new { x.ProcessId, x.IntegrationId });
            e.HasOne(x => x.Process).WithMany(p => p.Integrations).HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Integration).WithMany().HasForeignKey(x => x.IntegrationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.IntegrationId);
        });

        b.Entity<ProcessPainPoint>(e =>
        {
            e.ToTable("ProcessPainPoints");
            e.Property(x => x.Description).IsRequired().HasMaxLength(1000);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Process).WithMany(p => p.PainPoints).HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.SetNull);
            // Deleting the roadmap item (or its product) just unlinks it.
            e.HasOne(x => x.AddressedBy).WithMany().HasForeignKey(x => x.AddressedByItemId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ProcessId);
            e.HasIndex(x => x.ApplicationId);
            e.HasIndex(x => x.AddressedByItemId);
        });

        b.Entity<ProcessKpi>(e =>
        {
            e.ToTable("ProcessKpis");
            e.Property(x => x.Name).IsRequired().HasMaxLength(Name);
            e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Process).WithMany(p => p.Kpis).HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ProcessId);
        });

        b.Entity<Benefit>(e =>
        {
            e.ToTable("Benefits");
            e.Property(x => x.Description).IsRequired().HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(20);
            // Deleting the roadmap item (or its product) deletes the benefits it was to deliver.
            e.HasOne(x => x.RoadmapItem).WithMany().HasForeignKey(x => x.RoadmapItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Kpi).WithMany().HasForeignKey(x => x.KpiId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.RoadmapItemId);
            e.HasIndex(x => x.ApplicationId);
            e.HasIndex(x => x.KpiId);
        });

        b.Entity<BenefitMeasurement>(e =>
        {
            e.ToTable("BenefitMeasurements");
            e.Property(x => x.Source).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Benefit).WithMany(x => x.Measurements).HasForeignKey(x => x.BenefitId).OnDelete(DeleteBehavior.Cascade);
            // One reading per benefit per day.
            e.HasIndex(x => new { x.BenefitId, x.MeasuredOn }).IsUnique();
        });

        b.Entity<Decision>(e =>
        {
            e.ToTable("Decisions");
            e.Property(d => d.Title).IsRequired().HasMaxLength(300);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Context).HasMaxLength(Notes);
            e.Property(d => d.DecisionText).HasColumnName("Decision").IsRequired().HasMaxLength(Notes);
            e.Property(d => d.Outcome).HasMaxLength(Notes);
            e.Property(d => d.DecidedBy).HasMaxLength(Name);
            e.HasOne(d => d.SupersededBy).WithMany().HasForeignKey(d => d.SupersededById).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(d => d.DecidedOn);
        });

        b.Entity<DecisionAlternative>(e =>
        {
            e.ToTable("DecisionAlternatives");
            e.Property(a => a.Option).IsRequired().HasMaxLength(500);
            e.Property(a => a.Pros).HasMaxLength(2000);
            e.Property(a => a.Cons).HasMaxLength(2000);
            e.Property(a => a.WhyRejected).HasMaxLength(2000);
            e.HasOne<Decision>().WithMany(d => d.Alternatives).HasForeignKey(a => a.DecisionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.DecisionId);
        });

        b.Entity<DecisionLink>(e =>
        {
            // Exactly one target per link.
            e.ToTable("DecisionLinks", t => t.HasCheckConstraint("CK_DecisionLinks_OneTarget",
                "(\"ApplicationId\" IS NOT NULL) + (\"IntegrationId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContractId\" IS NOT NULL)"
                + " + (\"ProcessId\" IS NOT NULL) + (\"RoadmapItemId\" IS NOT NULL) + (\"TradingPartnerId\" IS NOT NULL) = 1"));
            e.HasOne(l => l.Decision).WithMany(d => d.Links).HasForeignKey(l => l.DecisionId).OnDelete(DeleteBehavior.Cascade);
            // A link goes when what it points at does; the decision itself stays.
            e.HasOne(l => l.Application).WithMany().HasForeignKey(l => l.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Integration).WithMany().HasForeignKey(l => l.IntegrationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Vendor).WithMany().HasForeignKey(l => l.VendorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Contract).WithMany().HasForeignKey(l => l.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Process).WithMany().HasForeignKey(l => l.ProcessId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.RoadmapItem).WithMany().HasForeignKey(l => l.RoadmapItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.TradingPartner).WithMany().HasForeignKey(l => l.TradingPartnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.DecisionId);
            e.HasIndex(l => l.ApplicationId);
            e.HasIndex(l => l.IntegrationId);
            e.HasIndex(l => l.VendorId);
            e.HasIndex(l => l.ContractId);
            e.HasIndex(l => l.ProcessId);
            e.HasIndex(l => l.RoadmapItemId);
            e.HasIndex(l => l.TradingPartnerId);
        });

        b.Entity<PortfolioDocument>(e =>
        {
            // Attached to at most one record. (The product it belongs to is separate.)
            e.ToTable("Documents", t => t.HasCheckConstraint("CK_Documents_OneTarget",
                "(\"ApplicationId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContractId\" IS NOT NULL) + (\"RoadmapItemId\" IS NOT NULL) <= 1"));
            e.Property(d => d.Title).IsRequired().HasMaxLength(300);
            e.Property(d => d.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.FileName).HasMaxLength(DocumentLibrary.MaxFileNameLength + 10);
            e.Property(d => d.LocalPath).HasMaxLength(1000);
            e.Property(d => d.Url).HasMaxLength(ExternalLinks.MaxUrlLength + 300);
            e.Property(d => d.Notes).HasMaxLength(2000);
            // The file stays in SharePoint when what it's attached to goes; the document just becomes unattached.
            e.HasOne(d => d.Product).WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Application).WithMany().HasForeignKey(d => d.ApplicationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Vendor).WithMany().HasForeignKey(d => d.VendorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Contract).WithMany().HasForeignKey(d => d.ContractId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.RoadmapItem).WithMany().HasForeignKey(d => d.RoadmapItemId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(d => d.ProductId);
            e.HasIndex(d => d.ApplicationId);
            e.HasIndex(d => d.VendorId);
            e.HasIndex(d => d.ContractId);
            e.HasIndex(d => d.RoadmapItemId);
            e.HasIndex(d => d.Category);
        });

        b.Entity<DocumentFolder>(e =>
        {
            e.ToTable("DocumentFolders");
            // One folder per type per product, and one shared folder per type.
            e.HasOne<Product>().WithMany().HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => new { f.ProductId, f.Category }).IsUnique();
            e.HasIndex(f => f.Category).IsUnique().HasFilter("\"ProductId\" IS NULL");
            e.Property(f => f.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(f => f.LocalPath).HasMaxLength(1000);
            e.Property(f => f.SharePointUrl).HasMaxLength(ExternalLinks.MaxUrlLength);
        });

        b.Entity<CapabilityFeature>(e =>
        {
            e.ToTable("CapabilityFeatures");
            e.Property(f => f.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(f => f.Name).IsRequired().HasMaxLength(Name).UseCollation("NOCASE");
            e.Property(f => f.Description).HasMaxLength(1000);
            // One feature of a name per category.
            e.HasIndex(f => new { f.Category, f.Name }).IsUnique();
        });

        b.Entity<ComparisonMember>(e =>
        {
            e.ToTable("ComparisonMembers");
            e.HasKey(m => new { m.Category, m.ApplicationId });
            e.Property(m => m.Category).HasConversion<string>().HasMaxLength(30);
            e.HasOne(m => m.Application).WithMany().HasForeignKey(m => m.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.ApplicationId);
        });

        b.Entity<FeatureAssessment>(e =>
        {
            e.ToTable("FeatureAssessments");
            e.HasKey(a => new { a.FeatureId, a.ApplicationId });
            e.Property(a => a.Availability).HasConversion<string>().HasMaxLength(10);
            e.Property(a => a.Strength).HasConversion<string>().HasMaxLength(10);
            e.Property(a => a.Note).HasMaxLength(CapabilityRules.MaxNoteLength);
            e.HasOne(a => a.Feature).WithMany(f => f.Assessments).HasForeignKey(a => a.FeatureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Application).WithMany().HasForeignKey(a => a.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.ApplicationId);
        });

        b.Entity<PricingLine>(e =>
        {
            e.ToTable("PricingLines");
            e.Property(l => l.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(l => l.Label).IsRequired().HasMaxLength(Name);
            e.Property(l => l.Basis).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Unit).HasMaxLength(40);
            e.Property(l => l.Frequency).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Note).HasMaxLength(CapabilityRules.MaxNoteLength);
            e.HasOne(l => l.Application).WithMany().HasForeignKey(l => l.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.Category, l.ApplicationId });
            e.HasIndex(l => l.ApplicationId);
        });

        b.Entity<SiteProfile>(e =>
        {
            e.ToTable("SiteProfiles");
            e.Property(p => p.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(p => p.Name).IsRequired().HasMaxLength(Name).UseCollation("NOCASE");
            e.Property(p => p.Notes).HasMaxLength(1000);
            e.HasIndex(p => new { p.Category, p.Name }).IsUnique();
        });

        b.Entity<PortfolioSettings>(e =>
        {
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.JiraBaseUrl).HasMaxLength(ExternalLinks.MaxUrlLength);
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
