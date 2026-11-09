using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>Something a decision can be linked to, for the link picker.</summary>
public record DecisionTargetOption(DecisionTarget Target, string Name, string? Detail = null);

public partial class PortfolioService
{
    private static IQueryable<Decision> WithParts(IQueryable<Decision> q) => q
        .Include(d => d.Alternatives)
        .Include(d => d.SupersededBy)
        .Include(d => d.Links).ThenInclude(l => l.Application)
        .Include(d => d.Links).ThenInclude(l => l.Integration)
        .Include(d => d.Links).ThenInclude(l => l.Vendor)
        .Include(d => d.Links).ThenInclude(l => l.Contract)
        .Include(d => d.Links).ThenInclude(l => l.Process)
        .Include(d => d.Links).ThenInclude(l => l.RoadmapItem)
        .Include(d => d.Links).ThenInclude(l => l.TradingPartner)
        .AsSplitQuery();

    /// <summary>Decisions, newest first; optionally only those linked to <paramref name="target"/>.</summary>
    public async Task<List<Decision>> GetDecisionsAsync(DecisionTarget? target = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = WithParts(db.Decisions.AsNoTracking());
        if (target is { } t)
        {
            var link = t.ToLink(0);
            q = t.Kind switch
            {
                DecisionTargetKind.Application => q.Where(d => d.Links.Any(l => l.ApplicationId == link.ApplicationId)),
                DecisionTargetKind.Integration => q.Where(d => d.Links.Any(l => l.IntegrationId == link.IntegrationId)),
                DecisionTargetKind.Vendor => q.Where(d => d.Links.Any(l => l.VendorId == link.VendorId)),
                DecisionTargetKind.Contract => q.Where(d => d.Links.Any(l => l.ContractId == link.ContractId)),
                DecisionTargetKind.Process => q.Where(d => d.Links.Any(l => l.ProcessId == link.ProcessId)),
                DecisionTargetKind.RoadmapItem => q.Where(d => d.Links.Any(l => l.RoadmapItemId == link.RoadmapItemId)),
                _ => q.Where(d => d.Links.Any(l => l.TradingPartnerId == link.TradingPartnerId)),
            };
        }
        return (await q.ToListAsync()).OrderByDescending(d => d.DecidedOn).ThenByDescending(d => d.Id).ToList();
    }

    public async Task<Decision?> GetDecisionAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithParts(db.Decisions.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id);
    }

    /// <summary>The decisions this one replaced (they name it as "superseded by").</summary>
    public async Task<List<Decision>> GetSupersededByAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Decisions.AsNoTracking().Where(d => d.SupersededById == id).OrderByDescending(d => d.DecidedOn).ToListAsync();
    }

    /// <summary>Creates or updates a decision and replaces its alternatives. <paramref name="linkTo"/> links a new decision on creation.</summary>
    public async Task<int> SaveDecisionAsync(DecisionDraft draft, DecisionTarget? linkTo = null)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (draft.SupersededById is { } sid)
        {
            if (!await db.Decisions.AnyAsync(d => d.Id == sid)) throw NotFound("Replacing decision");
            // Walk the chain: no loops (A replaced by B replaced by A).
            var seen = new HashSet<int> { draft.Id };
            for (int? at = sid; at is { } x; at = await db.Decisions.Where(d => d.Id == x).Select(d => d.SupersededById).FirstOrDefaultAsync())
                if (!seen.Add(x)) throw new InvalidOperationException("That would make a loop: the replacing decision is itself replaced by this one.");
        }

        Decision decision;
        if (draft.IsNew)
        {
            decision = new Decision();
            db.Decisions.Add(decision);
        }
        else decision = await db.Decisions.Include(d => d.Alternatives).FirstOrDefaultAsync(d => d.Id == draft.Id) ?? throw NotFound("Decision");

        draft.ApplyTo(decision);
        decision.UpdatedAt = DateTime.UtcNow;
        db.DecisionAlternatives.RemoveRange(decision.Alternatives);
        decision.Alternatives = draft.Alternatives.Where(a => !a.IsBlank)
            .Select((a, n) => new DecisionAlternative
            {
                Option = a.Option.Trim(), Pros = Clean(a.Pros), Cons = Clean(a.Cons), WhyRejected = Clean(a.WhyRejected), SortOrder = n,
            }).ToList();
        await db.SaveChangesAsync();

        if (draft.IsNew && linkTo is { } target) await LinkDecisionAsync(decision.Id, target);
        return decision.Id;
    }

    /// <summary>Deletes the decision with its alternatives and links. Decisions it replaced lose their "superseded by".</summary>
    public async Task DeleteDecisionAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Decisions.FindAsync(id) is not { } decision) return;
        db.Decisions.Remove(decision);
        await db.SaveChangesAsync();
    }

    /// <summary>Links the decision to a record. Linking twice is fine.</summary>
    public async Task LinkDecisionAsync(int decisionId, DecisionTarget target)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Decisions.AnyAsync(d => d.Id == decisionId)) throw NotFound("Decision");
        var exists = target.Kind switch
        {
            DecisionTargetKind.Application => await db.Applications.AnyAsync(x => x.Id == target.Id),
            DecisionTargetKind.Integration => await db.Integrations.AnyAsync(x => x.Id == target.Id),
            DecisionTargetKind.Vendor => await db.Vendors.AnyAsync(x => x.Id == target.Id),
            DecisionTargetKind.Contract => await db.Contracts.AnyAsync(x => x.Id == target.Id),
            DecisionTargetKind.Process => await db.BusinessProcesses.AnyAsync(x => x.Id == target.Id),
            DecisionTargetKind.RoadmapItem => await db.Items.AnyAsync(x => x.Id == target.Id),
            _ => await db.TradingPartners.AnyAsync(x => x.Id == target.Id),
        };
        if (!exists) throw NotFound(PortfolioText.Label(target.Kind));
        var links = await db.DecisionLinks.Where(l => l.DecisionId == decisionId).ToListAsync();
        if (links.Any(l => l.Target == target)) return;
        db.DecisionLinks.Add(target.ToLink(decisionId));
        await db.SaveChangesAsync();
    }

    public async Task UnlinkDecisionAsync(int linkId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.DecisionLinks.FindAsync(linkId) is not { } link) return;
        db.DecisionLinks.Remove(link);
        await db.SaveChangesAsync();
    }

    /// <summary>Everything a decision can be linked to, grouped by kind. Roadmap items: goals and initiatives only.</summary>
    public async Task<List<DecisionTargetOption>> GetDecisionTargetOptionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var options = new List<DecisionTargetOption>();
        static DecisionTarget T(DecisionTargetKind k, int id) => new(k, id);
        options.AddRange((await db.Applications.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync())
            .OrderBy(x => x.Name).Select(x => new DecisionTargetOption(T(DecisionTargetKind.Application, x.Id), x.Name)));
        options.AddRange((await db.TradingPartners.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync())
            .OrderBy(x => x.Name).Select(x => new DecisionTargetOption(T(DecisionTargetKind.TradingPartner, x.Id), x.Name)));
        options.AddRange((await db.Integrations.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync())
            .OrderBy(x => x.Name).Select(x => new DecisionTargetOption(T(DecisionTargetKind.Integration, x.Id), x.Name)));
        options.AddRange((await db.Vendors.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync())
            .OrderBy(x => x.Name).Select(x => new DecisionTargetOption(T(DecisionTargetKind.Vendor, x.Id), x.Name)));
        options.AddRange((await db.Contracts.AsNoTracking().Select(a => new { a.Id, a.Name, Vendor = a.Vendor!.Name }).ToListAsync())
            .OrderBy(x => x.Vendor).ThenBy(x => x.Name).Select(x => new DecisionTargetOption(T(DecisionTargetKind.Contract, x.Id), x.Name, x.Vendor)));
        options.AddRange((await db.BusinessProcesses.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync())
            .OrderBy(x => x.Name).Select(x => new DecisionTargetOption(T(DecisionTargetKind.Process, x.Id), x.Name)));
        options.AddRange((await db.Items.AsNoTracking()
                .Where(i => i.ItemType == ItemType.Goal || i.ItemType == ItemType.Initiative)
                .Select(i => new { i.Id, i.Title, Product = i.Product!.Name, i.ItemType }).ToListAsync())
            .OrderBy(x => x.Product).ThenBy(x => x.ItemType).ThenBy(x => x.Title)
            .Select(x => new DecisionTargetOption(T(DecisionTargetKind.RoadmapItem, x.Id), x.Title, x.Product)));
        return options;
    }
}
