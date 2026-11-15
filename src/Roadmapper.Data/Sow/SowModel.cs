using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Sows;

/// <summary>EF mapping for statements of work, their cost lines, the rate card and the SOW settings.</summary>
internal static class SowModel
{
    public static void ConfigureSows(this ModelBuilder b)
    {
        b.Entity<Sow>(e =>
        {
            e.ToTable("Sows");
            e.Property(s => s.Number).HasMaxLength(SowRules.MaxNumberLength);
            e.Property(s => s.ProjectName).IsRequired().HasMaxLength(PortfolioRules.MaxNameLength);
            e.Property(s => s.CustomerName).IsRequired().HasMaxLength(PortfolioRules.MaxNameLength);
            e.Property(s => s.Site).HasMaxLength(SowRules.MaxSiteLength);
            e.Property(s => s.Manager).HasMaxLength(PortfolioRules.MaxNameLength);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            foreach (var p in new[] { nameof(Sow.Overview), nameof(Sow.Scope), nameof(Sow.DetailedDescription), nameof(Sow.Assumptions), nameof(Sow.Schedule) })
                e.Property(p).HasMaxLength(SowRules.MaxTextLength);
            e.Ignore(s => s.Totals);
            // Deleting the trading partner keeps the SOW; it still prints the customer's name.
            e.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => s.CustomerId);
            e.HasIndex(s => s.Status);
        });

        b.Entity<SowLine>(e =>
        {
            e.ToTable("SowLines");
            e.Property(l => l.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(l => l.Frequency).HasConversion<string>().HasMaxLength(10);
            e.Property(l => l.Description).IsRequired().HasMaxLength(SowRules.MaxLineLength);
            e.Property(l => l.Unit).HasMaxLength(SowRules.MaxUnitLength);
            e.Property(l => l.Note).HasMaxLength(SowRules.MaxNoteLength);
            e.Ignore(l => l.Amount);
            e.HasOne(l => l.Sow).WithMany(s => s.Lines).HasForeignKey(l => l.SowId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.SowId);
        });

        // The rate card starts with the rates from the SOW estimation template (v220815).
        b.Entity<SowRate>(e =>
        {
            e.ToTable("SowRates");
            e.Property(r => r.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.Frequency).HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.Name).IsRequired().HasMaxLength(SowRules.MaxLineLength);
            e.Property(r => r.Unit).HasMaxLength(SowRules.MaxUnitLength);
            e.Property(r => r.Note).HasMaxLength(SowRules.MaxNoteLength);
            e.HasData(
            new SowRate { Id = 1, Category = SowCostCategory.LaborItPm, Frequency = SowFrequency.OneTime, Name = "Project management", Rate = 150m, Unit = "hour", Note = "Project management and oversight", SortOrder = 0 },
            new SowRate { Id = 2, Category = SowCostCategory.LaborItPm, Frequency = SowFrequency.OneTime, Name = "Product management", Rate = 150m, Unit = "hour", Note = "Design review and as-needed support", SortOrder = 1 },
            new SowRate { Id = 3, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "Design", Rate = 150m, Unit = "hour", SortOrder = 0 },
            new SowRate { Id = 4, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "Development", Rate = 215m, Unit = "hour", SortOrder = 1 },
            new SowRate { Id = 5, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "Configuration", Rate = 150m, Unit = "hour", SortOrder = 2 },
            new SowRate { Id = 6, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "Testing", Rate = 150m, Unit = "hour", SortOrder = 3 },
            new SowRate { Id = 7, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "Support", Rate = 150m, Unit = "hour", SortOrder = 4 },
            new SowRate { Id = 8, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "BSA labor (non-bill)", Rate = 0m, Unit = "hour", Note = "BSA labor for oversight, testing, and support.", SortOrder = 5 },
            new SowRate { Id = 9, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "Vendor labor", Rate = 150m, Unit = "hour", Note = "Don't change unless the vendor's rate is higher.", SortOrder = 6 },
            new SowRate { Id = 10, Category = SowCostCategory.LaborWms, Frequency = SowFrequency.OneTime, Name = "Contingency", Rate = 150m, Unit = "hour", Note = "Usually about 10% of the WMS hours, rounded up.", SortOrder = 7 },
            new SowRate { Id = 11, Category = SowCostCategory.LaborEdi, Frequency = SowFrequency.OneTime, Name = "Design", Rate = 80m, Unit = "hour", Note = "Kenco development hours", SortOrder = 0 },
            new SowRate { Id = 12, Category = SowCostCategory.LaborEdi, Frequency = SowFrequency.OneTime, Name = "Development", Rate = 175m, Unit = "hour", Note = "Cleo hours", SortOrder = 1 },
            new SowRate { Id = 13, Category = SowCostCategory.LaborEdi, Frequency = SowFrequency.OneTime, Name = "Configuration", Rate = 150m, Unit = "hour", SortOrder = 2 },
            new SowRate { Id = 14, Category = SowCostCategory.LaborEdi, Frequency = SowFrequency.OneTime, Name = "Testing", Rate = 150m, Unit = "hour", SortOrder = 3 },
            new SowRate { Id = 15, Category = SowCostCategory.LaborEdi, Frequency = SowFrequency.OneTime, Name = "Support", Rate = 150m, Unit = "hour", SortOrder = 4 },
            new SowRate { Id = 16, Category = SowCostCategory.LaborTms, Frequency = SowFrequency.OneTime, Name = "Design", Rate = 150m, Unit = "hour", SortOrder = 0 },
            new SowRate { Id = 17, Category = SowCostCategory.LaborTms, Frequency = SowFrequency.OneTime, Name = "Development", Rate = 150m, Unit = "hour", SortOrder = 1 },
            new SowRate { Id = 18, Category = SowCostCategory.LaborTms, Frequency = SowFrequency.OneTime, Name = "Configuration", Rate = 150m, Unit = "hour", SortOrder = 2 },
            new SowRate { Id = 19, Category = SowCostCategory.LaborTms, Frequency = SowFrequency.OneTime, Name = "Testing", Rate = 150m, Unit = "hour", SortOrder = 3 },
            new SowRate { Id = 20, Category = SowCostCategory.LaborTms, Frequency = SowFrequency.OneTime, Name = "Support", Rate = 150m, Unit = "hour", SortOrder = 4 },
            new SowRate { Id = 21, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Application readiness", Rate = 150m, Unit = "hour", SortOrder = 0 },
            new SowRate { Id = 22, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Application functional", Rate = 150m, Unit = "hour", SortOrder = 1 },
            new SowRate { Id = 23, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Application regression", Rate = 150m, Unit = "hour", SortOrder = 2 },
            new SowRate { Id = 24, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Application performance", Rate = 150m, Unit = "hour", SortOrder = 3 },
            new SowRate { Id = 25, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Solution end-to-end", Rate = 150m, Unit = "hour", SortOrder = 4 },
            new SowRate { Id = 26, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Solution integration", Rate = 150m, Unit = "hour", SortOrder = 5 },
            new SowRate { Id = 27, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Solution regression", Rate = 150m, Unit = "hour", SortOrder = 6 },
            new SowRate { Id = 28, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Interface failure", Rate = 150m, Unit = "hour", SortOrder = 7 },
            new SowRate { Id = 29, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Interface performance", Rate = 150m, Unit = "hour", SortOrder = 8 },
            new SowRate { Id = 30, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Installation qualification (IQ)", Rate = 150m, Unit = "hour", SortOrder = 9 },
            new SowRate { Id = 31, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Operational qualification (OQ)", Rate = 150m, Unit = "hour", SortOrder = 10 },
            new SowRate { Id = 32, Category = SowCostCategory.LaborTesting, Frequency = SowFrequency.OneTime, Name = "Performance qualification (PQ)", Rate = 150m, Unit = "hour", SortOrder = 11 },
            new SowRate { Id = 33, Category = SowCostCategory.Software, Frequency = SowFrequency.Annual, Name = "WMS license", Rate = 1200m, Unit = "each", SortOrder = 0 },
            new SowRate { Id = 34, Category = SowCostCategory.Software, Frequency = SowFrequency.Annual, Name = "LMS license", Rate = 300m, Unit = "each", SortOrder = 1 },
            new SowRate { Id = 35, Category = SowCostCategory.Software, Frequency = SowFrequency.Annual, Name = "AS2 license maintenance", Rate = 500m, Unit = "each", SortOrder = 2 },
            new SowRate { Id = 36, Category = SowCostCategory.Software, Frequency = SowFrequency.Annual, Name = "EDI annual support", Rate = 1m, Unit = "each", Note = "Enter the support amount as the quantity.", SortOrder = 3 },
            new SowRate { Id = 37, Category = SowCostCategory.Software, Frequency = SowFrequency.Annual, Name = "MercuryGate license", Rate = 0m, Unit = "each", SortOrder = 4 },
            new SowRate { Id = 38, Category = SowCostCategory.Software, Frequency = SowFrequency.OneTime, Name = "AS2 license", Rate = 2500m, Unit = "each", SortOrder = 5 },
            new SowRate { Id = 39, Category = SowCostCategory.Software, Frequency = SowFrequency.OneTime, Name = "MercuryGate license", Rate = 0m, Unit = "each", SortOrder = 6 },
            new SowRate { Id = 40, Category = SowCostCategory.Software, Frequency = SowFrequency.OneTime, Name = "Software license", Rate = 0m, Unit = "each", Note = "For a Blue Yonder software license, add 9.25% sales tax to the cost.", SortOrder = 7 },
            new SowRate { Id = 41, Category = SowCostCategory.Travel, Frequency = SowFrequency.OneTime, Name = "Travel", Rate = 1800m, Unit = "trip", Note = "Per trip: airfare, hotel, car and meals.", SortOrder = 0 }            );
        });

        b.Entity<SowSettings>(e =>
        {
            e.ToTable("SowSettings");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.DefaultManager).HasMaxLength(PortfolioRules.MaxNameLength);
            e.Property(s => s.Signatories).IsRequired().HasMaxLength(SowRules.MaxTextLength);
            foreach (var p in new[] { nameof(SowSettings.CostFootnote), nameof(SowSettings.Agreement), nameof(SowSettings.DefaultScope), nameof(SowSettings.DefaultAssumptions), nameof(SowSettings.DefaultSchedule) })
                e.Property(p).HasMaxLength(SowRules.MaxTextLength);
            e.HasData(new SowSettings());
        });
    }
}
