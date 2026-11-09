using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

public partial class PortfolioService
{
    /// <summary>Every application with its vendor, contract, owners, roadmap and initiative links.</summary>
    public async Task<List<Application>> GetApplicationsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Applications.AsNoTracking()
            .Include(a => a.Vendor)
            .Include(a => a.Contract)
            .Include(a => a.BusinessOwner)
            .Include(a => a.TechnicalOwner)
            .Include(a => a.Product)
            .Include(a => a.Initiatives)
            .OrderBy(a => a.Name)
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<Application?> GetApplicationAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Applications.AsNoTracking()
            .Include(a => a.Vendor)
            .Include(a => a.Contract)
            .Include(a => a.BusinessOwner)
            .Include(a => a.TechnicalOwner)
            .Include(a => a.Product)
            .Include(a => a.SupportContacts).ThenInclude(s => s.Contact)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<int> SaveApplicationAsync(ApplicationDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var name = draft.Name.Trim();
        if (await db.Applications.AnyAsync(a => a.Id != draft.Id && a.Name == name))
            throw new InvalidOperationException($"An application named “{name}” already exists.");
        if (draft.VendorId is { } vid && !await db.Vendors.AnyAsync(v => v.Id == vid)) throw NotFound("Vendor");
        if (draft.ContractId is { } cid && !await db.Contracts.AnyAsync(c => c.Id == cid && c.VendorId == draft.VendorId))
            throw new InvalidOperationException("That contract belongs to a different vendor. Choose one of this vendor's contracts.");
        if (draft.ProductId is { } pid)
        {
            if (!await db.Products.AnyAsync(p => p.Id == pid)) throw NotFound("Roadmap");
            if (await db.Applications.Where(a => a.Id != draft.Id && a.ProductId == pid).Select(a => a.Name).FirstOrDefaultAsync() is { } other)
                throw new InvalidOperationException($"That roadmap already belongs to “{other}”. Each roadmap can belong to one application.");
        }
        await EnsureContactAsync(db, draft.BusinessOwnerId, "business owner");
        await EnsureContactAsync(db, draft.TechnicalOwnerId, "technical owner");

        Application app;
        if (draft.IsNew)
        {
            app = new Application();
            db.Applications.Add(app);
        }
        else app = await db.Applications.FindAsync(draft.Id) ?? throw NotFound("Application");

        draft.ApplyTo(app);
        app.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return app.Id;
    }

    /// <summary>
    /// Deletes the application and its links. The linked roadmap and its items are untouched. Refused while
    /// integrations still use it: delete or re-point those first, or set the application to Retired instead.
    /// </summary>
    public async Task DeleteApplicationAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Applications.FindAsync(id) is not { } app) return;
        var used = await db.Integrations
            .Where(i => i.SourceApplicationId == id || i.TargetApplicationId == id || i.ViaApplicationId == id)
            .OrderBy(i => i.Name).Select(i => i.Name).ToListAsync();
        if (used.Count > 0)
            throw new InvalidOperationException(
                $"{app.Name} is used by {used.Count} integration{(used.Count == 1 ? "" : "s")}: {string.Join(", ", used.Take(5))}{(used.Count > 5 ? ", …" : "")}. " +
                "Delete those integrations first, or set the application to Retired to keep its history.");
        db.Applications.Remove(app);
        await db.SaveChangesAsync();
    }

    // ---- Support contacts ----

    public async Task AddSupportContactAsync(int applicationId, int contactId, SupportRole role, string? note)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId)) throw NotFound("Application");
        await EnsureContactAsync(db, contactId, "contact");
        if (await db.ApplicationSupportContacts.AnyAsync(s => s.ApplicationId == applicationId && s.ContactId == contactId && s.Role == role))
            throw new InvalidOperationException("That person already has that role on this application.");
        db.ApplicationSupportContacts.Add(new ApplicationSupportContact { ApplicationId = applicationId, ContactId = contactId, Role = role, Note = Clean(note) });
        await db.SaveChangesAsync();
    }

    public async Task RemoveSupportContactAsync(int applicationId, int contactId, SupportRole role)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ApplicationSupportContacts.FindAsync(applicationId, contactId, role) is not { } row) return;
        db.ApplicationSupportContacts.Remove(row);
        await db.SaveChangesAsync();
    }

    // ---- Links to roadmap items ----

    /// <summary>Links (or re-links with a new impact) a roadmap item from any product to an application.</summary>
    public async Task LinkItemAsync(int applicationId, int itemId, InitiativeImpact impact)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId)) throw NotFound("Application");
        if (!await db.Items.AnyAsync(i => i.Id == itemId)) throw NotFound("Roadmap item");
        if (await db.ApplicationInitiatives.FindAsync(applicationId, itemId) is { } existing) existing.ImpactType = impact;
        else db.ApplicationInitiatives.Add(new ApplicationInitiative { ApplicationId = applicationId, RoadmapItemId = itemId, ImpactType = impact });
        await db.SaveChangesAsync();
    }

    public async Task UnlinkItemAsync(int applicationId, int itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ApplicationInitiatives.FindAsync(applicationId, itemId) is not { } link) return;
        db.ApplicationInitiatives.Remove(link);
        await db.SaveChangesAsync();
    }

    /// <summary>The roadmap items linked to an application, with live status and progress, grouped by product.</summary>
    public async Task<List<LinkedItem>> GetLinkedItemsAsync(int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.ApplicationInitiatives.AsNoTracking()
            .Where(l => l.ApplicationId == applicationId)
            .Select(l => new LinkedItem(l.RoadmapItemId, l.RoadmapItem!.ProductId, l.RoadmapItem.Product!.Name, l.RoadmapItem.ItemType,
                l.RoadmapItem.Title, l.RoadmapItem.Status, l.RoadmapItem.ProgressPct, l.RoadmapItem.StartDate, l.RoadmapItem.EndDate,
                l.RoadmapItem.Owner, l.ImpactType))
            .ToListAsync();
        // Sorted here: ItemType is stored as text, and its order is the hierarchy, not the alphabet.
        return rows.OrderBy(l => l.ProductName).ThenBy(l => l.ItemType).ThenBy(l => l.Title).ToList();
    }

    /// <summary>The applications a roadmap item is linked to (shown in the item editor).</summary>
    public async Task<List<ApplicationInitiative>> GetLinksForItemAsync(int itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ApplicationInitiatives.AsNoTracking().Include(l => l.Application)
            .Where(l => l.RoadmapItemId == itemId)
            .OrderBy(l => l.Application!.Name)
            .ToListAsync();
    }

    /// <summary>A product's Goals, Initiatives and Features for a picker, Initiatives first. Tasks are left out: link the work, not the to-dos.</summary>
    public async Task<List<ItemOption>> GetLinkableItemsAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var items = await db.Items.AsNoTracking().Where(i => i.ProductId == productId)
            .Select(i => new { i.Id, i.ParentId, i.ItemType, i.Title }).ToListAsync();
        var byId = items.ToDictionary(i => i.Id);

        string PathOf(int? parentId)
        {
            var parts = new List<string>();
            while (parentId is { } id && byId.TryGetValue(id, out var p)) { parts.Insert(0, p.Title); parentId = p.ParentId; }
            return string.Join(" › ", parts);
        }

        return items.Where(i => i.ItemType != ItemType.Task)
            .Select(i => new ItemOption(i.Id, i.ItemType, i.Title, PathOf(i.ParentId)))
            .OrderBy(i => i.ItemType == ItemType.Initiative ? 0 : 1).ThenBy(i => i.ItemType).ThenBy(i => i.Path).ThenBy(i => i.Title)
            .ToList();
    }

    /// <summary>Products for pickers: id and name, not archived.</summary>
    public async Task<List<(int Id, string Name)>> GetProductOptionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Products.AsNoTracking().Where(p => p.ArchivedAt == null).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name }).ToListAsync();
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    /// <summary>The application whose own roadmap is this product, if any.</summary>
    public async Task<Application?> GetApplicationForProductAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ProductId == productId);
    }
}
