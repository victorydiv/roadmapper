using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>A choice in one of a development project's pickers. <see cref="Detail"/>: e.g. a partner's type or an item's type.</summary>
public record DevOption(int Id, string Name, string? Detail = null);

/// <summary>
/// A product's development pipeline: its projects (with cost lines), the cost categories, and each project's documents
/// (SOWs first, then newest).
/// </summary>
public record DevPipeline(int ProductId, string ProductName, List<DevProject> Projects, List<DevCostCategory> Categories,
    Dictionary<int, List<PortfolioDocument>> Documents)
{
    public DevPipelineSummary Summary => DevRules.Summarize(Projects);
    public List<PortfolioDocument> DocumentsOf(int projectId) => Documents.GetValueOrDefault(projectId) ?? [];
}

/// <summary>What a product's development projects can be linked to.</summary>
public record DevProjectOptions(List<DevOption> Customers, List<DevOption> Applications, List<DevOption> Vendors, List<DevOption> Items);

public partial class PortfolioService
{
    private static IQueryable<DevProject> WithDetails(IQueryable<DevProject> q) => q
        .Include(p => p.CostLines).ThenInclude(l => l.Category)
        .Include(p => p.Customer).Include(p => p.Application).Include(p => p.Vendor).Include(p => p.RoadmapItem);

    /// <summary>A product's development projects in pipeline order, then by target date and name, with their cost lines.</summary>
    public async Task<List<DevProject>> GetDevProjectsAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await WithDetails(db.DevProjects.AsNoTracking()).AsSplitQuery().Where(p => p.ProductId == productId).ToListAsync())
            .OrderBy(p => p.Status).ThenBy(p => p.TargetDate ?? DateOnly.MaxValue).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A product's whole pipeline (null when there's no such product).</summary>
    public async Task<DevPipeline?> GetDevPipelineAsync(int productId)
    {
        string? name;
        await using (var db = await dbFactory.CreateDbContextAsync())
            name = await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Name).FirstOrDefaultAsync();
        if (name is null) return null;
        var documents = (await GetDocumentsAsync(productId: productId))
            .Where(d => d.DevProjectId is not null)
            .GroupBy(d => d.DevProjectId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.Category != DocumentCategory.Sow)
                .ThenByDescending(d => d.DocumentDate ?? DateOnly.FromDateTime(d.CreatedAt)).ToList());
        return new DevPipeline(productId, name, await GetDevProjectsAsync(productId), await GetDevCostCategoriesAsync(), documents);
    }

    public async Task<DevProject?> GetDevProjectAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithDetails(db.DevProjects.AsNoTracking()).AsSplitQuery().FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>Customers (trading partners, customers first), applications, vendors and the product's goals, initiatives and features.</summary>
    public async Task<DevProjectOptions> GetDevProjectOptionsAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var partners = (await db.TradingPartners.AsNoTracking().Select(x => new { x.Id, x.Name, x.Type }).ToListAsync())
            .OrderBy(x => x.Type != PartnerType.Customer).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DevOption(x.Id, x.Name, x.Type == PartnerType.Customer ? null : PortfolioText.Label(x.Type))).ToList();
        var apps = (await db.Applications.AsNoTracking().Select(x => new DevOption(x.Id, x.Name, null)).ToListAsync())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var vendors = (await db.Vendors.AsNoTracking().Select(x => new DevOption(x.Id, x.Name, null)).ToListAsync())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var items = (await db.Items.AsNoTracking()
                .Where(i => i.ProductId == productId && (i.ItemType == ItemType.Goal || i.ItemType == ItemType.Initiative || i.ItemType == ItemType.Feature))
                .Select(i => new { i.Id, i.Title, i.ItemType }).ToListAsync())
            .OrderBy(x => x.ItemType).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DevOption(x.Id, x.Title, x.ItemType.ToString())).ToList();
        return new(partners, apps, vendors, items);
    }

    /// <summary>
    /// Creates or updates a development project and its cost lines: lines are matched by id, so unchanged lines keep
    /// their history; lines left out are removed. Blank lines are ignored. Returns the project's id.
    /// </summary>
    public async Task<int> SaveDevProjectAsync(DevProjectDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Products.AnyAsync(p => p.Id == draft.ProductId)) throw NotFound("Product");
        if (draft.CustomerId is > 0 and var cid && !await db.TradingPartners.AnyAsync(x => x.Id == cid)) throw NotFound("Customer");
        if (draft.ApplicationId is > 0 and var aid && !await db.Applications.AnyAsync(x => x.Id == aid)) throw NotFound("Application");
        if (draft.VendorId is > 0 and var vid && !await db.Vendors.AnyAsync(x => x.Id == vid)) throw NotFound("Vendor");
        if (draft.RoadmapItemId is > 0 and var iid && !await db.Items.AnyAsync(x => x.Id == iid && x.ProductId == draft.ProductId))
            throw new InvalidOperationException("The roadmap item chosen isn't in this product's roadmap.");
        var lines = draft.Lines.Where(l => !l.IsBlank).ToList();
        var categoryIds = await db.DevCostCategories.Select(c => c.Id).ToListAsync();
        if (lines.Any(l => !categoryIds.Contains(l.CategoryId))) throw NotFound("Cost category");

        DevProject project;
        if (draft.IsNew)
        {
            project = new DevProject();
            db.DevProjects.Add(project);
        }
        else
        {
            project = await db.DevProjects.Include(p => p.CostLines).FirstOrDefaultAsync(p => p.Id == draft.Id) ?? throw NotFound("Development project");
            if (project.ProductId != draft.ProductId) throw new InvalidOperationException("A development project can't be moved to another product.");
        }
        draft.ApplyTo(project, Today);
        project.UpdatedAt = DateTime.UtcNow;

        var keep = lines.Where(l => l.Id != 0).Select(l => l.Id).ToHashSet();
        foreach (var gone in project.CostLines.Where(l => !keep.Contains(l.Id)).ToList())
        {
            project.CostLines.Remove(gone);
            db.DevCostLines.Remove(gone);
        }
        for (var n = 0; n < lines.Count; n++)
        {
            var l = lines[n];
            var row = l.Id == 0 ? null : project.CostLines.FirstOrDefault(x => x.Id == l.Id);
            if (row is null)
            {
                row = new DevCostLine();
                project.CostLines.Add(row);
            }
            l.ApplyTo(row);
            row.SortOrder = n;
        }
        await db.SaveChangesAsync();
        return project.Id;
    }

    /// <summary>Moves a project to another stage (e.g. from the board). Marking it Complete records today as its completion date.</summary>
    public async Task SetDevProjectStatusAsync(int id, DevStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var project = await db.DevProjects.FirstOrDefaultAsync(p => p.Id == id) ?? throw NotFound("Development project");
        if (project.Status == status) return;
        project.Status = status;
        project.CompletedOn = status == DevStatus.Complete ? project.CompletedOn ?? Today : null;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Deletes a project and its cost lines. Its documents stay, no longer attached to it.</summary>
    public async Task DeleteDevProjectAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.DevProjects.Include(p => p.CostLines).FirstOrDefaultAsync(p => p.Id == id) is not { } project) return;
        foreach (var doc in await db.Documents.Where(d => d.DevProjectId == id).ToListAsync()) doc.DevProjectId = null;
        db.DevProjects.Remove(project);
        await db.SaveChangesAsync();
    }

    // ---- Cost categories ----

    /// <summary>The cost categories (DEV, QA, EDI, PM…) in their order, shared by every product.</summary>
    public async Task<List<DevCostCategory>> GetDevCostCategoriesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.DevCostCategories.AsNoTracking().ToListAsync()).OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
    }

    /// <summary>How many cost lines use each category.</summary>
    public async Task<Dictionary<int, int>> GetDevCostCategoryUsageAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.DevCostLines.GroupBy(l => l.CategoryId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    /// <summary>Adds (id 0) or renames a cost category and sets its default hourly rate. Names are unique, ignoring case.</summary>
    public async Task<int> SaveDevCostCategoryAsync(int id, string name, decimal? defaultRate)
    {
        var clean = Clean(name) ?? throw new InvalidOperationException("Give the category a name, e.g. DEV or QA.");
        if (clean.Length > DevRules.MaxCategoryLength) throw new InvalidOperationException($"A category name can be at most {DevRules.MaxCategoryLength} characters.");
        if (defaultRate is < 0) throw new InvalidOperationException("A rate can't be negative.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.DevCostCategories.ToListAsync();
        if (all.Any(c => c.Id != id && string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"There's already a cost category called “{clean}”.");
        var row = id == 0 ? null : all.FirstOrDefault(c => c.Id == id) ?? throw NotFound("Cost category");
        if (row is null)
        {
            row = new DevCostCategory { SortOrder = all.Select(c => c.SortOrder + 1).DefaultIfEmpty(0).Max() };
            db.DevCostCategories.Add(row);
        }
        row.Name = clean;
        row.DefaultRate = defaultRate;
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task MoveDevCostCategoryAsync(int id, int delta)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var order = (await db.DevCostCategories.ToListAsync()).OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
        var at = order.FindIndex(c => c.Id == id);
        var to = at + Math.Sign(delta);
        if (at < 0 || to < 0 || to >= order.Count) return;
        (order[at], order[to]) = (order[to], order[at]);
        for (var n = 0; n < order.Count; n++) order[n].SortOrder = n;
        await db.SaveChangesAsync();
    }

    /// <summary>Deletes a cost category that no cost line uses.</summary>
    public async Task DeleteDevCostCategoryAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.DevCostCategories.FindAsync(id) is not { } row) return;
        var used = await db.DevCostLines.CountAsync(l => l.CategoryId == id);
        if (used > 0)
            throw new InvalidOperationException($"“{row.Name}” is used by {used} cost line{(used == 1 ? "" : "s")}. Change those lines to another category first.");
        db.DevCostCategories.Remove(row);
        await db.SaveChangesAsync();
    }
}
