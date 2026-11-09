using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>An application up- or downstream of another, <see cref="Distance"/> integrations away, reached through <see cref="Integrations"/>.</summary>
public record DependencyRow(Application Application, int Distance, IReadOnlyList<Integration> Integrations);

/// <summary>What feeds an application and what it feeds, plus every integration it takes part in directly.</summary>
public record DependencyView(Application Application, int Depth, IReadOnlyList<Integration> Direct,
    IReadOnlyList<DependencyRow> Upstream, IReadOnlyList<DependencyRow> Downstream);

public partial class PortfolioService
{
    public const int MaxDependencyDepth = 5;

    private static IQueryable<Integration> WithParts(IQueryable<Integration> q) => q
        .Include(i => i.Source)
        .Include(i => i.Target)
        .Include(i => i.Via)
        .Include(i => i.Owner)
        .Include(i => i.Risks)
        .AsSplitQuery();

    /// <summary>Every integration with its applications, owner and risks.</summary>
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

    /// <summary>Creates or updates an integration. A blank name becomes "Source → Target".</summary>
    public async Task<int> SaveIntegrationAsync(IntegrationDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = new[] { draft.SourceApplicationId, draft.TargetApplicationId, draft.ViaApplicationId }.OfType<int>().ToList();
        var names = await db.Applications.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name);
        if (!names.ContainsKey(draft.SourceApplicationId!.Value)) throw NotFound("Source application");
        if (!names.ContainsKey(draft.TargetApplicationId!.Value)) throw NotFound("Target application");
        if (draft.ViaApplicationId is { } via && !names.ContainsKey(via)) throw NotFound("“Via” application");
        await EnsureContactAsync(db, draft.OwnerId, "owner");

        var name = string.IsNullOrWhiteSpace(draft.Name)
            ? $"{names[draft.SourceApplicationId.Value]} → {names[draft.TargetApplicationId.Value]}"
            : draft.Name.Trim();
        if (name.Length > PortfolioRules.MaxNameLength) name = name[..PortfolioRules.MaxNameLength];
        if (await db.Integrations.AnyAsync(i => i.Id != draft.Id && i.SourceApplicationId == draft.SourceApplicationId
                && i.TargetApplicationId == draft.TargetApplicationId && i.Type == draft.Type && i.Name == name))
            throw new InvalidOperationException($"There's already a {PortfolioText.Label(draft.Type)} integration named “{name}” between these applications.");

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

    /// <summary>
    /// Applications upstream (they send data to this one) and downstream (they receive data from it), up to
    /// <paramref name="depth"/> integrations away. Retired integrations are ignored: they no longer move data.
    /// </summary>
    public async Task<DependencyView?> GetDependenciesAsync(int applicationId, int depth = 2)
    {
        depth = Math.Clamp(depth, 1, MaxDependencyDepth);
        await using var db = await dbFactory.CreateDbContextAsync();
        var apps = await db.Applications.AsNoTracking().Include(a => a.Vendor).ToDictionaryAsync(a => a.Id);
        if (!apps.TryGetValue(applicationId, out var app)) return null;

        var live = await WithParts(db.Integrations.AsNoTracking()).Where(i => i.Status != LifecycleStatus.Retired).ToListAsync();
        var byId = live.ToDictionary(i => i.Id);
        var graph = PortfolioGraph.From(live);

        List<DependencyRow> Rows(IEnumerable<Reach> reach) => reach
            .Select(r => new DependencyRow(apps[r.ApplicationId], r.Distance, r.IntegrationIds.Select(id => byId[id]).OrderBy(i => i.Name).ToList()))
            .OrderBy(r => r.Distance).ThenBy(r => r.Application.Criticality).ThenBy(r => r.Application.Name)
            .ToList();

        var direct = graph.IntegrationsTouching(applicationId).Select(id => byId[id]).OrderBy(i => i.Name).ToList();
        return new DependencyView(app, depth, direct, Rows(graph.Upstream(applicationId, depth)), Rows(graph.Downstream(applicationId, depth)));
    }
}
