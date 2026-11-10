using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>An application with its health (null when retired).</summary>
public record AppHealthRow(Application Application, AppHealth? Health);

/// <summary>A roadmap item the portfolio cares about: linked to an application, or carrying benefits.</summary>
public record StrategicItem(int ItemId, int ProductId, string ProductName, ItemType ItemType, string Title, ItemStatus Status, int ProgressPct,
    DateOnly? EndDate, IReadOnlyList<string> Applications, int Benefits, bool Overdue);

public record HighRiskRow(Integration Integration, RiskLevel Risk, bool ReviewOverdue);

/// <summary>Expected and realized dollars per year for one benefit category.</summary>
public record CategoryValue(BenefitCategory Category, decimal ExpectedUsd, decimal RealizedUsd);

/// <summary>The executive dashboard: everything on <c>/portfolio</c> in one read.</summary>
public record PortfolioDashboard(
    DateOnly AsOf,
    IReadOnlyList<AppHealthRow> Applications,
    decimal AnnualRunCost,
    IReadOnlyDictionary<TimeQuadrant, int> Time,
    int TimeUnrated,
    IReadOnlyList<RenewalRow> Renewals,
    int RenewalWarningDays,
    IReadOnlyList<HighRiskRow> RiskyIntegrations,
    int LiveIntegrations,
    int KeyPartners,
    IReadOnlyList<StrategicItem> Initiatives,
    BenefitTotals Benefits,
    IReadOnlyList<CategoryValue> ValueByCategory,
    decimal? LaborRate,
    int OpenPainPoints,
    int HighPainPoints,
    IReadOnlyList<Decision> RecentDecisions)
{
    public int Count(HealthRating rating) => Applications.Count(a => a.Health?.Rating == rating);
}

public partial class PortfolioService
{
    public async Task<PortfolioDashboard> GetDashboardAsync()
    {
        var today = Today;
        var settings = await GetSettingsAsync();
        var apps = (await GetApplicationsAsync()).Where(PortfolioRules.CountsInPortfolio).ToList();
        var integrations = await GetIntegrationsAsync();
        var live = integrations.Where(i => i.Status != LifecycleStatus.Retired).ToList();

        var health = apps
            .Select(a => new AppHealthRow(a, PortfolioHealth.Assess(a, PortfolioHealth.IntegrationsOf(a.Id, live), today, settings.RenewalWarningDays)))
            .OrderBy(r => r.Health?.Score ?? 101).ThenBy(r => r.Application.Criticality).ThenBy(r => r.Application.Name)
            .ToList();

        var time = Enum.GetValues<TimeQuadrant>().ToDictionary(q => q, q => apps.Count(a => a.TimeQuadrant == q));

        var risky = live
            .Select(i => new HighRiskRow(i, PortfolioRules.RiskLevel(i, today), PortfolioRules.ReviewOverdue(i, today)))
            .Where(r => r.Risk != RiskLevel.Low)
            .OrderByDescending(r => r.Risk).ThenBy(r => r.Integration.Criticality).ThenByDescending(r => PortfolioRules.TopRiskScore(r.Integration))
            .ThenBy(r => r.Integration.Name)
            .ToList();

        var benefits = await GetBenefitsAsync();
        var byCategory = benefits.GroupBy(b => b.Category)
            .Select(g => new CategoryValue(g.Key,
                g.Sum(b => BenefitMath.ExpectedUsd(b, settings.LaborRate) ?? 0),
                g.Sum(b => BenefitMath.RealizedUsd(b, settings.LaborRate) ?? 0)))
            .Where(c => c.ExpectedUsd > 0 || c.RealizedUsd > 0)
            .OrderByDescending(c => c.ExpectedUsd)
            .ToList();

        List<StrategicItem> initiatives;
        int openPain, highPain;
        List<Decision> recent;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var links = await db.ApplicationInitiatives.AsNoTracking()
                .Select(l => new { l.RoadmapItemId, App = l.Application!.Name }).ToListAsync();
            var benefitCounts = benefits.GroupBy(b => b.RoadmapItemId).ToDictionary(g => g.Key, g => g.Count());
            var ids = links.Select(l => l.RoadmapItemId).Concat(benefitCounts.Keys).Distinct().ToList();
            var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id) && i.Product!.ArchivedAt == null)
                .Select(i => new { i.Id, i.ProductId, Product = i.Product!.Name, i.ItemType, i.Title, i.Status, i.ProgressPct, i.EndDate })
                .ToListAsync();
            initiatives = items
                .Where(i => i.Status != ItemStatus.Cancelled)
                .Select(i => new StrategicItem(i.Id, i.ProductId, i.Product, i.ItemType, i.Title, i.Status, i.ProgressPct, i.EndDate,
                    links.Where(l => l.RoadmapItemId == i.Id).Select(l => l.App).Distinct().Order().ToList(),
                    benefitCounts.GetValueOrDefault(i.Id),
                    i.Status != ItemStatus.Done && i.EndDate is { } end && end < today))
                .OrderBy(i => i.Status switch { ItemStatus.InProgress => 0, ItemStatus.Planned => 1, ItemStatus.Idea => 2, _ => 3 })
                .ThenBy(i => i.ProductName).ThenBy(i => i.ItemType).ThenBy(i => i.Title)
                .ToList();

            var pains = await db.ProcessPainPoints.AsNoTracking().Where(p => p.Status == PainPointStatus.Open).Select(p => p.Severity).ToListAsync();
            openPain = pains.Count;
            highPain = pains.Count(s => s >= PainSeverity.High);
            recent = (await db.Decisions.AsNoTracking().ToListAsync())
                .OrderByDescending(d => d.DecidedOn).ThenByDescending(d => d.Id).Take(5).ToList();
        }

        return new PortfolioDashboard(today, health, apps.Sum(a => a.AnnualCost ?? 0), time, apps.Count(a => a.TimeQuadrant is null),
            await GetRenewalsAsync(settings.RenewalWarningDays), settings.RenewalWarningDays, risky, live.Count,
            (await GetPartnersAsync()).Count(p => p.Importance == PartnerImportance.Key && p.Status != PartnerStatus.Inactive),
            initiatives, BenefitMath.Totals(benefits, settings.LaborRate, today), byCategory, settings.LaborRate, openPain, highPain, recent);
    }

    /// <summary>Health for every application that isn't retired, keyed by id.</summary>
    public async Task<Dictionary<int, AppHealth>> GetHealthAsync()
    {
        var settings = await GetSettingsAsync();
        var live = (await GetIntegrationsAsync()).Where(i => i.Status != LifecycleStatus.Retired).ToList();
        var result = new Dictionary<int, AppHealth>();
        foreach (var a in await GetApplicationsAsync())
            if (PortfolioHealth.Assess(a, PortfolioHealth.IntegrationsOf(a.Id, live), Today, settings.RenewalWarningDays) is { } h)
                result[a.Id] = h;
        return result;
    }
}
