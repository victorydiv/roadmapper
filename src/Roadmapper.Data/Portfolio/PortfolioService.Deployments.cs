using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>An application that can be assigned to customers in a comparison. <see cref="Added"/>: it's from another category.</summary>
public record DeploymentChoice(AppCategory Category, int ApplicationId, string Name, bool Added)
{
    public string Key => $"{Category}:{ApplicationId}";
}

public partial class PortfolioService
{
    /// <summary>A comparison's deployments, with their customer and application.</summary>
    public async Task<List<AppDeployment>> GetDeploymentsAsync(AppCategory category)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Deployments.AsNoTracking().Include(d => d.TradingPartner).Include(d => d.Application)
            .Where(d => d.Category == category).ToListAsync();
    }

    /// <summary>A customer's deployments, with their application, by application then site.</summary>
    public async Task<List<AppDeployment>> GetDeploymentsForPartnerAsync(int partnerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.Deployments.AsNoTracking().Include(d => d.Application).Where(d => d.TradingPartnerId == partnerId).ToListAsync())
            .OrderBy(d => d.Application!.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Category)
            .ThenBy(d => d.Site is not null).ThenBy(d => d.Site, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>An application's deployments in every comparison, with their customer, by customer then site.</summary>
    public async Task<List<AppDeployment>> GetDeploymentsForApplicationAsync(int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.Deployments.AsNoTracking().Include(d => d.TradingPartner).Where(d => d.ApplicationId == applicationId).ToListAsync())
            .OrderBy(d => d.Category).ThenBy(d => d.TradingPartner!.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Site is not null).ThenBy(d => d.Site, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Every application that can be assigned to a customer, per comparison: those of each category, then those added
    /// to it from other categories. Retired applications are left out.
    /// </summary>
    public async Task<List<DeploymentChoice>> GetDeploymentChoicesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var apps = await db.Applications.AsNoTracking().Where(a => a.Status != LifecycleStatus.Retired).ToListAsync();
        var members = await db.ComparisonMembers.AsNoTracking().ToListAsync();
        var byId = apps.ToDictionary(a => a.Id);
        return apps.Select(a => new DeploymentChoice(a.Category, a.Id, a.Name, false))
            .Concat(members.Where(m => byId.ContainsKey(m.ApplicationId)).Select(m => new DeploymentChoice(m.Category, m.ApplicationId, byId[m.ApplicationId].Name, true)))
            .OrderBy(c => c.Category).ThenBy(c => c.Added).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Adds a deployment for each site of a new draft, or changes one. The application must be in the category's
    /// comparison, and a customer can have it only once per site. A new customer's name adds them as a trading partner
    /// (type Customer), or uses the one of that name. Returns the ids saved.
    /// </summary>
    public async Task<List<int>> SaveDeploymentAsync(DeploymentDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.ApplicationId) ?? throw NotFound("Application");
        if (app.Category != draft.Category && !await db.ComparisonMembers.AnyAsync(m => m.Category == draft.Category && m.ApplicationId == app.Id))
            throw new InvalidOperationException($"{app.Name} isn't in the {PortfolioText.Label(draft.Category)} comparison. Add it there first.");

        TradingPartner partner;
        if (draft.TradingPartnerId > 0)
            partner = await db.TradingPartners.FindAsync(draft.TradingPartnerId) ?? throw NotFound("Customer");
        else
        {
            var name = draft.NewCustomerName!.Trim();
            partner = (await db.TradingPartners.ToListAsync()).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? db.TradingPartners.Add(new TradingPartner { Name = name, Type = PartnerType.Customer }).Entity;
        }

        var existing = partner.Id == 0 ? [] : await db.Deployments
            .Where(d => d.Category == draft.Category && d.ApplicationId == app.Id && d.TradingPartnerId == partner.Id && d.Id != draft.Id).ToListAsync();
        var sites = draft.SiteList;
        var taken = sites.Where(s => existing.Any(d => DeploymentRules.SameSite(d.Site, s))).ToList();
        if (taken.Count > 0)
            throw new InvalidOperationException(
                $"{app.Name} is already recorded for {string.Join(", ", taken.Select(s => DeploymentRules.Where(partner.Name, s)))}. Edit that entry instead.");

        var saved = new List<AppDeployment>();
        if (draft.IsNew)
        {
            foreach (var site in sites)
            {
                var d = new AppDeployment { TradingPartner = partner };
                draft.ApplyTo(d, site);
                db.Deployments.Add(d);
                saved.Add(d);
            }
        }
        else
        {
            var d = await db.Deployments.FirstOrDefaultAsync(x => x.Id == draft.Id) ?? throw NotFound("Deployment");
            draft.ApplyTo(d, sites[0]);
            d.TradingPartner = partner;
            d.UpdatedAt = DateTime.UtcNow;
            saved.Add(d);
        }
        await db.SaveChangesAsync();
        return saved.Select(d => d.Id).ToList();
    }

    public async Task DeleteDeploymentAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Deployments.FindAsync(id) is not { } d) return;
        db.Deployments.Remove(d);
        await db.SaveChangesAsync();
    }
}
