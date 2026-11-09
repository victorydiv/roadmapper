using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>
/// An application or trading partner up- or downstream of another, <see cref="Distance"/> integrations away, reached
/// through <see cref="Integrations"/>. Exactly one of <see cref="Application"/> and <see cref="Partner"/> is set.
/// </summary>
public record DependencyRow(NodeRef Node, Application? Application, TradingPartner? Partner, int Distance, IReadOnlyList<Integration> Integrations)
{
    public string Name => Application?.Name ?? Partner?.Name ?? "";
}

/// <summary>What feeds an application or partner and what it feeds, plus every integration it takes part in directly.</summary>
public record DependencyView(NodeRef Node, string Name, int Depth, IReadOnlyList<Integration> Direct,
    IReadOnlyList<DependencyRow> Upstream, IReadOnlyList<DependencyRow> Downstream);

public partial class PortfolioService
{
    public const int MaxDependencyDepth = 5;

    private static IQueryable<Integration> WithParts(IQueryable<Integration> q) => q
        .Include(i => i.Source)
        .Include(i => i.SourcePartner)
        .Include(i => i.Target)
        .Include(i => i.TargetPartner)
        .Include(i => i.Via)
        .Include(i => i.Owner)
        .Include(i => i.Risks)
        .AsSplitQuery();

    /// <summary>Every integration with its applications, partners, owner and risks.</summary>
    public async Task<List<Integration>> GetIntegrationsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithParts(db.Integrations.AsNoTracking()).OrderBy(i => i.Name).ToListAsync();
    }

    public async Task<Integration?> GetIntegrationAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithParts(db.Integrations.AsNoTracking())
            .Include(i => i.Risks).ThenInclude(r => r.Owner)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    /// <summary>Integrations in which the application is the source, target or middle hop.</summary>
    public async Task<List<Integration>> GetIntegrationsForApplicationAsync(int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithParts(db.Integrations.AsNoTracking())
            .Where(i => i.SourceApplicationId == applicationId || i.TargetApplicationId == applicationId || i.ViaApplicationId == applicationId)
            .OrderBy(i => i.Name)
            .ToListAsync();
    }

    /// <summary>Integrations to or from the trading partner.</summary>
    public async Task<List<Integration>> GetIntegrationsForPartnerAsync(int partnerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithParts(db.Integrations.AsNoTracking())
            .Where(i => i.SourcePartnerId == partnerId || i.TargetPartnerId == partnerId)
            .OrderBy(i => i.Name)
            .ToListAsync();
    }

    /// <summary>Creates or updates an integration. A blank name becomes "Source → Target".</summary>
    public async Task<int> SaveIntegrationAsync(IntegrationDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var sourceName = await EndpointNameAsync(db, draft.Source!.Value) ?? throw NotFound(draft.Source.Value.IsPartner ? "Source partner" : "Source application");
        var targetName = await EndpointNameAsync(db, draft.Target!.Value) ?? throw NotFound(draft.Target.Value.IsPartner ? "Target partner" : "Target application");
        if (draft.ViaApplicationId is { } via && !await db.Applications.AnyAsync(a => a.Id == via)) throw NotFound("“Via” application");
        await EnsureContactAsync(db, draft.OwnerId, "owner");

        var name = string.IsNullOrWhiteSpace(draft.Name) ? $"{sourceName} → {targetName}" : draft.Name.Trim();
        if (name.Length > PortfolioRules.MaxNameLength) name = name[..PortfolioRules.MaxNameLength];
        if (await db.Integrations.AnyAsync(i => i.Id != draft.Id
                && i.SourceApplicationId == draft.SourceApplicationId && i.SourcePartnerId == draft.SourcePartnerId
                && i.TargetApplicationId == draft.TargetApplicationId && i.TargetPartnerId == draft.TargetPartnerId
                && i.Type == draft.Type && i.Name == name))
            throw new InvalidOperationException($"There's already a {PortfolioText.Label(draft.Type)} integration named “{name}” between these two.");

        Integration integration;
        if (draft.IsNew)
        {
            integration = new Integration();
            db.Integrations.Add(integration);
        }
        else integration = await db.Integrations.FindAsync(draft.Id) ?? throw NotFound("Integration");

        draft.ApplyTo(integration, name);
        integration.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return integration.Id;
    }

    private static Task<string?> EndpointNameAsync(RoadmapDbContext db, NodeRef node) => node.IsPartner
        ? db.TradingPartners.Where(p => p.Id == node.Id).Select(p => p.Name).FirstOrDefaultAsync()
        : db.Applications.Where(a => a.Id == node.Id).Select(a => a.Name).FirstOrDefaultAsync();

    /// <summary>Deletes the integration and its risks. Prefer setting it to Retired to keep it on record.</summary>
    public async Task DeleteIntegrationAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Integrations.Include(i => i.Risks).FirstOrDefaultAsync(i => i.Id == id) is not { } integration) return;
        db.Integrations.Remove(integration);
        await db.SaveChangesAsync();
    }

    /// <summary>Records that the integration was reviewed today, which restarts its review interval.</summary>
    public async Task MarkReviewedAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var integration = await db.Integrations.FindAsync(id) ?? throw NotFound("Integration");
        integration.LastReviewDate = Today;
        integration.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // ---- Risks ----

    public async Task<int> SaveRiskAsync(RiskDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Integrations.AnyAsync(i => i.Id == draft.IntegrationId)) throw NotFound("Integration");
        await EnsureContactAsync(db, draft.OwnerId, "risk owner");

        IntegrationRisk risk;
        if (draft.IsNew)
        {
            risk = new IntegrationRisk { IntegrationId = draft.IntegrationId };
            db.IntegrationRisks.Add(risk);
        }
        else
        {
            risk = await db.IntegrationRisks.FindAsync(draft.Id) ?? throw NotFound("Risk");
            if (risk.IntegrationId != draft.IntegrationId) throw NotFound("Risk");
        }
        draft.ApplyTo(risk);
        risk.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return risk.Id;
    }

    public async Task DeleteRiskAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.IntegrationRisks.FindAsync(id) is not { } risk) return;
        db.IntegrationRisks.Remove(risk);
        await db.SaveChangesAsync();
    }

    // ---- Dependencies ----

    public Task<DependencyView?> GetDependenciesAsync(int applicationId, int depth = 2) => GetDependenciesAsync(NodeRef.App(applicationId), depth);

    /// <summary>
    /// Applications and partners upstream (they send data to this one) and downstream (they receive data from it), up
    /// to <paramref name="depth"/> integrations away. Retired integrations are ignored: they no longer move data.
    /// </summary>
    public async Task<DependencyView?> GetDependenciesAsync(NodeRef node, int depth = 2)
    {
        depth = Math.Clamp(depth, 1, MaxDependencyDepth);
        await using var db = await dbFactory.CreateDbContextAsync();
        var apps = await db.Applications.AsNoTracking().Include(a => a.Vendor).ToDictionaryAsync(a => a.Id);
        var partners = await db.TradingPartners.AsNoTracking().ToDictionaryAsync(p => p.Id);
        string? NameOf(NodeRef n) => n.IsPartner ? partners.GetValueOrDefault(n.Id)?.Name : apps.GetValueOrDefault(n.Id)?.Name;
        if (NameOf(node) is not { } name) return null;

        var live = await WithParts(db.Integrations.AsNoTracking()).Where(i => i.Status != LifecycleStatus.Retired).ToListAsync();
        var byId = live.ToDictionary(i => i.Id);
        var graph = PortfolioGraph.From(live);

        List<DependencyRow> Rows(IEnumerable<Reach> reach) => reach
            .Select(r => new DependencyRow(r.Node, r.Node.IsPartner ? null : apps[r.Node.Id], r.Node.IsPartner ? partners[r.Node.Id] : null,
                r.Distance, r.IntegrationIds.Select(id => byId[id]).OrderBy(i => i.Name).ToList()))
            .OrderBy(r => r.Distance).ThenBy(r => r.Node.Kind).ThenBy(r => r.Application?.Criticality).ThenBy(r => r.Name)
            .ToList();

        var direct = graph.IntegrationsTouching(node).Select(id => byId[id]).OrderBy(i => i.Name).ToList();
        return new DependencyView(node, name, depth, direct, Rows(graph.Upstream(node, depth)), Rows(graph.Downstream(node, depth)));
    }
}
