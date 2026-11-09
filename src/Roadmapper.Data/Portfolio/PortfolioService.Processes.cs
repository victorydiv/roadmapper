using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>A process that uses an application, or relies on one of its integrations.</summary>
public record ProcessUse(BusinessProcess Process, ProcessApplication? Use, IReadOnlyList<Integration> Integrations);

public partial class PortfolioService
{
    private static IQueryable<BusinessProcess> WithParts(IQueryable<BusinessProcess> q) => q
        .Include(p => p.ProcessOwner)
        .Include(p => p.ParentProcess)
        .Include(p => p.Applications).ThenInclude(a => a.Application)
        .Include(p => p.Integrations).ThenInclude(i => i.Integration)
        .Include(p => p.PainPoints)
        .Include(p => p.Kpis)
        .AsSplitQuery();

    /// <summary>Every process with its owner, parent, applications, integrations, pain points and KPIs, in tree order.</summary>
    public async Task<List<BusinessProcess>> GetProcessesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await WithParts(db.BusinessProcesses.AsNoTracking()).ToListAsync();
        return TreeOrder(all);
    }

    /// <summary>Parents before their children; siblings by sort order, then name.</summary>
    public static List<BusinessProcess> TreeOrder(IReadOnlyCollection<BusinessProcess> all)
    {
        var ids = all.Select(p => p.Id).ToHashSet();
        var children = all.ToLookup(p => p.ParentProcessId is { } pid && ids.Contains(pid) ? pid : (int?)null);
        var ordered = new List<BusinessProcess>();
        var seen = new HashSet<int>();
        void Visit(int? parent)
        {
            foreach (var p in children[parent].OrderBy(p => p.SortOrder).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                if (seen.Add(p.Id)) { ordered.Add(p); Visit(p.Id); }
        }
        Visit(null);
        return ordered;
    }

    public async Task<BusinessProcess?> GetProcessAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.BusinessProcesses.AsNoTracking()
            .Include(p => p.ProcessOwner)
            .Include(p => p.ParentProcess)
            .Include(p => p.Children)
            .Include(p => p.Applications).ThenInclude(a => a.Application)
            .Include(p => p.Integrations).ThenInclude(i => i.Integration).ThenInclude(i => i!.Source)
            .Include(p => p.Integrations).ThenInclude(i => i.Integration).ThenInclude(i => i!.SourcePartner)
            .Include(p => p.Integrations).ThenInclude(i => i.Integration).ThenInclude(i => i!.Target)
            .Include(p => p.Integrations).ThenInclude(i => i.Integration).ThenInclude(i => i!.TargetPartner)
            .Include(p => p.Integrations).ThenInclude(i => i.Integration).ThenInclude(i => i!.Via)
            .Include(p => p.Integrations).ThenInclude(i => i.Integration).ThenInclude(i => i!.Risks)
            .Include(p => p.PainPoints).ThenInclude(pp => pp.Application)
            .Include(p => p.PainPoints).ThenInclude(pp => pp.AddressedBy).ThenInclude(i => i!.Product)
            .Include(p => p.Kpis)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<int> SaveProcessAsync(ProcessDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var name = draft.Name.Trim();
        if (await db.BusinessProcesses.AnyAsync(p => p.Id != draft.Id && p.Name == name))
            throw new InvalidOperationException($"A process named “{name}” already exists.");
        await EnsureContactAsync(db, draft.ProcessOwnerId, "process owner");
        if (draft.ParentProcessId is { } parentId)
        {
            var parentOf = await db.BusinessProcesses.ToDictionaryAsync(p => p.Id, p => p.ParentProcessId);
            if (!parentOf.ContainsKey(parentId)) throw NotFound("Parent process");
            if (!draft.IsNew && PortfolioRules.ParentError(draft.Id, parentId, parentOf) is { } loop) throw new InvalidOperationException(loop);
        }

        BusinessProcess process;
        if (draft.IsNew)
        {
            process = new BusinessProcess();
            db.BusinessProcesses.Add(process);
        }
        else process = await db.BusinessProcesses.FindAsync(draft.Id) ?? throw NotFound("Process");

        if (draft.IsNew || process.ParentProcessId != draft.ParentProcessId)
            process.SortOrder = (await db.BusinessProcesses.Where(p => p.ParentProcessId == draft.ParentProcessId && p.Id != draft.Id)
                .MaxAsync(p => (int?)p.SortOrder) ?? 0) + 1;
        draft.ApplyTo(process);
        process.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return process.Id;
    }

    /// <summary>Deletes the process with its links, pain points and KPIs. Refused while it has sub-processes.</summary>
    public async Task DeleteProcessAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.BusinessProcesses.FindAsync(id) is not { } process) return;
        var children = await db.BusinessProcesses.Where(p => p.ParentProcessId == id).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync();
        if (children.Count > 0)
            throw new InvalidOperationException(
                $"{process.Name} has {children.Count} sub-process{(children.Count == 1 ? "" : "es")}: {string.Join(", ", children.Take(5))}{(children.Count > 5 ? ", …" : "")}. " +
                "Move or delete those first.");
        db.BusinessProcesses.Remove(process);
        await db.SaveChangesAsync();
    }

    // ---- Applications and integrations a process relies on ----

    /// <summary>Adds the application to the process, or updates how it's used.</summary>
    public async Task SetProcessApplicationAsync(int processId, int applicationId, string? usage, bool isPrimary)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == processId)) throw NotFound("Process");
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId)) throw NotFound("Application");
        if (usage?.Trim().Length > 500) throw new InvalidOperationException("Usage can be at most 500 characters.");
        if (await db.ProcessApplications.FindAsync(processId, applicationId) is { } row)
        {
            row.Usage = Clean(usage);
            row.IsPrimary = isPrimary;
        }
        else db.ProcessApplications.Add(new ProcessApplication { ProcessId = processId, ApplicationId = applicationId, Usage = Clean(usage), IsPrimary = isPrimary });
        await db.SaveChangesAsync();
    }

    public async Task RemoveProcessApplicationAsync(int processId, int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ProcessApplications.FindAsync(processId, applicationId) is not { } row) return;
        db.ProcessApplications.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task AddProcessIntegrationAsync(int processId, int integrationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == processId)) throw NotFound("Process");
        if (!await db.Integrations.AnyAsync(i => i.Id == integrationId)) throw NotFound("Integration");
        if (await db.ProcessIntegrations.FindAsync(processId, integrationId) is not null) return;
        db.ProcessIntegrations.Add(new ProcessIntegration { ProcessId = processId, IntegrationId = integrationId });
        await db.SaveChangesAsync();
    }

    public async Task RemoveProcessIntegrationAsync(int processId, int integrationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ProcessIntegrations.FindAsync(processId, integrationId) is not { } row) return;
        db.ProcessIntegrations.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>Processes that use the application, or rely on an integration it's part of (as source, target or via).</summary>
    public async Task<List<ProcessUse>> GetProcessesForApplicationAsync(int applicationId)
    {
        var all = await GetProcessesAsync();
        return all
            .Select(p => new ProcessUse(p, p.Applications.FirstOrDefault(a => a.ApplicationId == applicationId),
                p.Integrations.Select(i => i.Integration!)
                    .Where(i => i.SourceApplicationId == applicationId || i.TargetApplicationId == applicationId || i.ViaApplicationId == applicationId)
                    .OrderBy(i => i.Name).ToList()))
            .Where(u => u.Use is not null || u.Integrations.Count > 0)
            .ToList();
    }

    /// <summary>Processes that rely on the integration.</summary>
    public async Task<List<BusinessProcess>> GetProcessesForIntegrationAsync(int integrationId) =>
        (await GetProcessesAsync()).Where(p => p.Integrations.Any(i => i.IntegrationId == integrationId)).ToList();

    // ---- Pain points ----

    public async Task<int> SavePainPointAsync(PainPointDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == draft.ProcessId)) throw NotFound("Process");
        if (draft.ApplicationId is { } appId && !await db.Applications.AnyAsync(a => a.Id == appId)) throw NotFound("Application");
        if (draft.AddressedByItemId is { } itemId && !await db.Items.AnyAsync(i => i.Id == itemId)) throw NotFound("Roadmap item");

        ProcessPainPoint pain;
        if (draft.IsNew)
        {
            pain = new ProcessPainPoint { ProcessId = draft.ProcessId };
            db.ProcessPainPoints.Add(pain);
        }
        else
        {
            pain = await db.ProcessPainPoints.FindAsync(draft.Id) ?? throw NotFound("Pain point");
            if (pain.ProcessId != draft.ProcessId) throw NotFound("Pain point");
        }
        draft.ApplyTo(pain);
        pain.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return pain.Id;
    }

    public async Task DeletePainPointAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ProcessPainPoints.FindAsync(id) is not { } pain) return;
        db.ProcessPainPoints.Remove(pain);
        await db.SaveChangesAsync();
    }

    /// <summary>The Goals of a product, for placing a new Initiative (an Initiative must sit under a Goal).</summary>
    public async Task<List<ItemOption>> GetGoalsAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Items.AsNoTracking().Where(i => i.ProductId == productId && i.ItemType == ItemType.Goal)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Title)
            .Select(i => new { i.Id, i.Title }).ToListAsync();
        return rows.Select(r => new ItemOption(r.Id, ItemType.Goal, r.Title, "")).ToList();
    }

    // ---- KPIs ----

    public async Task<int> SaveKpiAsync(KpiDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == draft.ProcessId)) throw NotFound("Process");

        ProcessKpi kpi;
        if (draft.IsNew)
        {
            kpi = new ProcessKpi { ProcessId = draft.ProcessId };
            db.ProcessKpis.Add(kpi);
        }
        else
        {
            kpi = await db.ProcessKpis.FindAsync(draft.Id) ?? throw NotFound("KPI");
            if (kpi.ProcessId != draft.ProcessId) throw NotFound("KPI");
        }
        if (draft.Current is not null && draft.MeasuredOn is null) draft.MeasuredOn = Today;
        draft.ApplyTo(kpi);
        kpi.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return kpi.Id;
    }

    public async Task DeleteKpiAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ProcessKpis.FindAsync(id) is not { } kpi) return;
        db.ProcessKpis.Remove(kpi);
        await db.SaveChangesAsync();
    }

    // ---- Change impact ----

    /// <summary>
    /// What a change to the application or trading partner touches: integrations, applications and partners,
    /// business processes, roadmap items and vendors. Null when it doesn't exist. See <see cref="ImpactAnalyzer"/>.
    /// </summary>
    public async Task<ImpactReport?> GetImpactAsync(NodeRef subject, int depth = ImpactAnalyzer.DefaultDepth)
    {
        depth = Math.Clamp(depth, 1, MaxDependencyDepth);
        await using var db = await dbFactory.CreateDbContextAsync();
        var exists = subject.IsPartner
            ? await db.TradingPartners.AnyAsync(p => p.Id == subject.Id)
            : await db.Applications.AnyAsync(a => a.Id == subject.Id);
        if (!exists) return null;

        var input = new ImpactInput
        {
            Applications = await db.Applications.AsNoTracking()
                .Include(a => a.Vendor).ThenInclude(v => v!.Contracts)
                .Include(a => a.Vendor).ThenInclude(v => v!.AccountManager)
                .Include(a => a.Vendor).ThenInclude(v => v!.EscalationContacts).ThenInclude(e => e.Contact)
                .Include(a => a.BusinessOwner)
                .Include(a => a.TechnicalOwner)
                .AsSplitQuery().ToListAsync(),
            Partners = await db.TradingPartners.AsNoTracking().Include(p => p.RelationshipOwner).ToListAsync(),
            Integrations = await WithParts(db.Integrations.AsNoTracking()).ToListAsync(),
            Processes = await WithParts(db.BusinessProcesses.AsNoTracking()).ToListAsync(),
            Initiatives = await db.ApplicationInitiatives.AsNoTracking()
                .Select(l => new ImpactInitiative(l.ApplicationId, l.RoadmapItemId, l.RoadmapItem!.ProductId, l.RoadmapItem.Product!.Name,
                    l.RoadmapItem.ItemType, l.RoadmapItem.Title, l.RoadmapItem.Status, l.RoadmapItem.ProgressPct,
                    l.RoadmapItem.StartDate, l.RoadmapItem.EndDate, l.RoadmapItem.Owner, l.ImpactType))
                .ToListAsync(),
        };
        return ImpactAnalyzer.Analyze(input, subject, depth, Today);
    }
}
