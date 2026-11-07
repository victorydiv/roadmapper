using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;

namespace Roadmapper.Data;

public record ProductSummary(int Id, string Name, string? Description, ScoringModel ScoringModel, int ItemCount, int GoalCount, int DoneCount);

/// <summary>
/// All reads and writes go through here. Each call uses a short-lived DbContext (the recommended
/// pattern for Blazor Server), and every item mutation re-runs rollups and scoring for the product.
/// Business-rule violations surface as <see cref="InvalidOperationException"/> with a user-facing message.
/// </summary>
public partial class RoadmapService(IDbContextFactory<RoadmapDbContext> dbFactory, TimeProvider? clock = null)
{
    /// <summary>Today's date; the start-date criteria in a scoring rubric count months from it.</summary>
    private DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetLocalNow().DateTime);

    // ---- Products ----

    public async Task<List<ProductSummary>> GetProductsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Products
            .Where(p => p.ArchivedAt == null)
            .OrderBy(p => p.Name)
            .Select(p => new ProductSummary(p.Id, p.Name, p.Description, p.ScoringModel,
                p.Items.Count,
                p.Items.Count(i => i.ItemType == ItemType.Goal),
                p.Items.Count(i => i.Status == ItemStatus.Done)))
            .ToListAsync();
    }

    public async Task<Product> CreateProductAsync(string name, string? description, ScoringModel model)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product name is required.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var product = new Product { Name = name.Trim(), Description = Clean(description), ScoringModel = model };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.TaskTemplates.Add(StarterTemplates.EdiMigration(product.Id));
        await db.SaveChangesAsync();
        return product;
    }

    public async Task UpdateProductAsync(int id, string name, string? description, ScoringModel model)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product name is required.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var product = await db.Products.FindAsync(id) ?? throw NotFound("Product");
        var modelChanged = product.ScoringModel != model;
        product.Name = name.Trim();
        product.Description = Clean(description);
        product.ScoringModel = model;
        await db.SaveChangesAsync();
        if (modelChanged) await RecalculateAsync(db, id);
    }

    public async Task DeleteProductAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync();
    }

    // ---- Roadmap (read model) ----

    public async Task<ProductRoadmap?> GetRoadmapAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var product = await db.Products.AsNoTracking()
            .Include(p => p.Themes.OrderBy(t => t.Name))
            .Include(p => p.Tags.OrderBy(t => t.Name))
            .FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return null;

        var items = await db.Items.AsNoTracking()
            .Where(i => i.ProductId == productId)
            .Include(i => i.Score)
            .Include(i => i.Tags)
            .Include(i => i.Theme)
            .Include(i => i.RubricAnswers)
            .ToListAsync();
        var dependencies = await db.Dependencies.AsNoTracking()
            .Where(d => d.Predecessor!.ProductId == productId)
            .ToListAsync();

        return new ProductRoadmap(product, items, dependencies) { Rubric = await LoadRubricAsync(db, productId, product.WsjfScale) };
    }

    // ---- Items ----

    public async Task<int> SaveItemAsync(ItemDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == draft.ProductId) ?? throw NotFound("Product");

        ItemType? parentType = null;
        if (draft.ParentId is { } parentId)
        {
            var parent = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == parentId && i.ProductId == draft.ProductId)
                ?? throw NotFound("Parent item");
            parentType = parent.ItemType;
        }

        RoadmapItem item;
        if (draft.IsNew)
        {
            HierarchyRules.Validate(draft.ItemType, parentType);
            item = new RoadmapItem
            {
                ProductId = draft.ProductId,
                ItemType = draft.ItemType,
                SortOrder = await NextSortOrderAsync(db, draft.ProductId, draft.ParentId),
            };
            db.Items.Add(item);
        }
        else
        {
            item = await db.Items.Include(i => i.Score).Include(i => i.Tags).Include(i => i.RubricAnswers)
                .FirstOrDefaultAsync(i => i.Id == draft.Id) ?? throw NotFound("Item");
            // Type is fixed after creation; changing it would orphan the item's children.
            HierarchyRules.Validate(item.ItemType, parentType);
            if (item.ParentId != draft.ParentId)
                item.SortOrder = await NextSortOrderAsync(db, item.ProductId, draft.ParentId);
        }

        draft.ApplyTo(item);
        item.UpdatedAt = DateTime.UtcNow;
        await ApplyRubricAsync(db, product, item, draft);

        var oldTags = TagList(item.Tags);
        item.Tags.Clear();
        if (draft.TagIds.Count > 0)
        {
            var tagIds = draft.TagIds.ToList();
            item.Tags.AddRange(await db.Tags.Where(t => t.ProductId == draft.ProductId && tagIds.Contains(t.Id)).ToListAsync());
        }
        // Tag links live in a join table the audit trail doesn't see, so record them here.
        if (!draft.IsNew && TagList(item.Tags) is var newTags && newTags != oldTags)
            db.AuditEntries.Add(new AuditEntry
            {
                ProductId = item.ProductId, ItemId = item.Id, ItemTitle = item.Title, Action = AuditAction.Updated,
                Field = "Tags", OldValue = oldTags, NewValue = newTags,
            });

        await db.SaveChangesAsync();
        await RecalculateAsync(db, draft.ProductId);
        return item.Id;
    }

    /// <summary>Deletes the item and (via cascade) its whole subtree, scores, tags and dependencies.</summary>
    public async Task DeleteItemAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.Items.FindAsync(id);
        if (item is null) return;

        // Removing the tracked item records the deletion; the database cascades the subtree.
        db.Items.Remove(item);
        await db.SaveChangesAsync();
        await RecalculateAsync(db, item.ProductId);
    }

    /// <summary>Swaps the item with its previous (-1) or next (+1) sibling.</summary>
    public async Task MoveItemAsync(int id, int direction)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.Items.FindAsync(id) ?? throw NotFound("Item");
        var siblings = await db.Items
            .Where(i => i.ProductId == item.ProductId && i.ParentId == item.ParentId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .ToListAsync();

        var index = siblings.IndexOf(item);
        var target = index + Math.Sign(direction);
        if (target < 0 || target >= siblings.Count) return;

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++) siblings[i].SortOrder = i;
        await db.SaveChangesAsync();
    }

    public async Task SetHorizonAsync(int id, Horizon? horizon)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.Items.FindAsync(id) ?? throw NotFound("Item");
        item.Horizon = horizon;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Done also sets progress to 100%, matching the item editor.</summary>
    public async Task SetStatusAsync(int id, ItemStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.Items.FindAsync(id) ?? throw NotFound("Item");
        item.Status = status;
        if (status == ItemStatus.Done) item.ProgressPct = 100;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RecalculateAsync(db, item.ProductId);
    }

    // ---- History ----

    /// <summary>Newest first; for one item when <paramref name="itemId"/> is given, else the whole product.</summary>
    public async Task<List<AuditEntry>> GetActivityAsync(int productId, int? itemId = null, int take = 50)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.AuditEntries.AsNoTracking().Where(a => a.ProductId == productId);
        if (itemId is not null) query = query.Where(a => a.ItemId == itemId);
        return await query.OrderByDescending(a => a.ChangedAt).ThenByDescending(a => a.Id).Take(take).ToListAsync();
    }

    // ---- Dependencies ----

    public async Task AddDependencyAsync(int predecessorId, int successorId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var products = await db.Items.Where(i => i.Id == predecessorId || i.Id == successorId)
            .Select(i => i.ProductId).Distinct().ToListAsync();
        if (predecessorId == successorId) throw new InvalidOperationException("An item can't depend on itself.");
        if (products.Count != 1) throw new InvalidOperationException("Both items must exist in the same product.");

        var existing = await db.Dependencies
            .Where(d => d.Predecessor!.ProductId == products[0])
            .Select(d => new { d.PredecessorId, d.SuccessorId })
            .ToListAsync();
        if (existing.Any(d => d.PredecessorId == predecessorId && d.SuccessorId == successorId))
            throw new InvalidOperationException("That dependency already exists.");
        if (DependencyRules.WouldCreateCycle(existing.Select(d => (d.PredecessorId, d.SuccessorId)), predecessorId, successorId))
            throw new InvalidOperationException("That dependency would create a cycle.");

        db.Dependencies.Add(new Dependency { PredecessorId = predecessorId, SuccessorId = successorId });
        await db.SaveChangesAsync();
    }

    public async Task RemoveDependencyAsync(int dependencyId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Dependencies.FindAsync(dependencyId) is not { } dependency) return;
        db.Dependencies.Remove(dependency);
        await db.SaveChangesAsync();
    }

    // ---- Themes & tags ----

    public async Task AddThemeAsync(int productId, string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Theme name is required.");

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Themes.Add(new Theme { ProductId = productId, Name = name.Trim(), Color = color });
        await db.SaveChangesAsync();
    }

    public async Task DeleteThemeAsync(int themeId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Themes.Where(t => t.Id == themeId).ExecuteDeleteAsync();
    }

    public async Task AddTagAsync(int productId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Tag name is required.");

        await using var db = await dbFactory.CreateDbContextAsync();
        name = name.Trim();
        if (await db.Tags.AnyAsync(t => t.ProductId == productId && t.Name == name))
            throw new InvalidOperationException($"Tag \"{name}\" already exists.");
        db.Tags.Add(new Tag { ProductId = productId, Name = name });
        await db.SaveChangesAsync();
    }

    public async Task DeleteTagAsync(int tagId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync();
    }

    // ---- Snapshots ----

    private static readonly JsonSerializerOptions SnapshotJson = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Snapshots for a product, newest first, without their payloads.</summary>
    public async Task<List<Snapshot>> GetSnapshotsAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Snapshots.AsNoTracking()
            .Where(s => s.ProductId == productId)
            .OrderByDescending(s => s.TakenAt)
            .Select(s => new Snapshot { Id = s.Id, ProductId = s.ProductId, Name = s.Name, Note = s.Note, TakenAt = s.TakenAt, ItemCount = s.ItemCount })
            .ToListAsync();
    }

    public async Task<Snapshot> TakeSnapshotAsync(int productId, string name, string? note)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Snapshot name is required.");

        var roadmap = await GetRoadmapAsync(productId) ?? throw NotFound("Product");
        var items = roadmap.Items.Select(SnapshotItem.From).ToList();
        var snapshot = new Snapshot
        {
            ProductId = productId,
            Name = name.Trim(),
            Note = Clean(note),
            ItemCount = items.Count,
            PayloadJson = JsonSerializer.Serialize(items, SnapshotJson),
        };

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Snapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }

    public async Task<(Snapshot Snapshot, List<SnapshotItem> Items)?> GetSnapshotAsync(int snapshotId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var snapshot = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshotId);
        if (snapshot is null) return null;
        return (snapshot, JsonSerializer.Deserialize<List<SnapshotItem>>(snapshot.PayloadJson, SnapshotJson) ?? []);
    }

    public async Task DeleteSnapshotAsync(int snapshotId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Snapshots.Where(s => s.Id == snapshotId).ExecuteDeleteAsync();
    }

    // ---- Import ----

    // The reference categorical palette, in its fixed order, for themes created by import.
    private static readonly string[] ThemeColors = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];

    /// <summary>
    /// Creates every valid planned item in one transaction, parents before children, creating any
    /// missing themes and tags by name. Returns the number of items created.
    /// </summary>
    public async Task<int> ImportAsync(int productId, IReadOnlyList<PlannedItem> plans)
    {
        var valid = plans.Where(p => p.IsValid).ToList();
        if (valid.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await db.Products.AnyAsync(p => p.Id == productId)) throw NotFound("Product");

        var themes = (await db.Themes.Where(t => t.ProductId == productId).ToListAsync())
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in valid.Select(p => p.ThemeName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !themes.ContainsKey(n)))
        {
            var theme = new Theme { ProductId = productId, Name = name, Color = ThemeColors[themes.Count % ThemeColors.Length] };
            db.Themes.Add(theme);
            themes[name] = theme;
        }
        var tags = (await db.Tags.Where(t => t.ProductId == productId).ToListAsync())
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in valid.SelectMany(p => p.TagNames).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !tags.ContainsKey(n)))
        {
            var tag = new Tag { ProductId = productId, Name = name };
            db.Tags.Add(tag);
            tags[name] = tag;
        }
        await db.SaveChangesAsync();

        var created = new Dictionary<int, RoadmapItem>(); // by row number
        var nextSort = new Dictionary<int, int>();         // by parent id (0 = top level)
        foreach (var level in Enum.GetValues<ItemType>())
        {
            foreach (var plan in valid.Where(p => p.Draft.ItemType == level).OrderBy(p => p.RowNumber))
            {
                var draft = plan.Draft;
                if (plan.ParentRow is { } row) draft.ParentId = created[row].Id;
                draft.ThemeId = plan.ThemeName is { } tn ? themes[tn].Id : null;

                var parentKey = draft.ParentId ?? 0;
                if (!nextSort.TryGetValue(parentKey, out var sort))
                    sort = await NextSortOrderAsync(db, productId, draft.ParentId);
                nextSort[parentKey] = sort + 1;

                var item = new RoadmapItem { ProductId = productId, ItemType = draft.ItemType, SortOrder = sort };
                // WSJF values in a spreadsheet were typed by hand, so the scoring rubric leaves them alone.
                draft.Score.ManualFactors |= RubricCalculator.WithValues(draft.Score);
                draft.ApplyTo(item);
                item.Tags.AddRange(plan.TagNames.Select(n => tags[n]));
                db.Items.Add(item);
                created[plan.RowNumber] = item;
            }
            // Save per level so children can reference their parents' new ids.
            await db.SaveChangesAsync();
        }

        await RecalculateAsync(db, productId);
        await tx.CommitAsync();
        return created.Count;
    }

    // ---- Helpers ----

    /// <summary>Re-derives parent rollups and every item's computed score for one product.</summary>
    private async Task RecalculateAsync(RoadmapDbContext db, int productId)
    {
        db.ChangeTracker.Clear();
        var product = await db.Products.Where(p => p.Id == productId).Select(p => new { p.ScoringModel, p.WsjfScale }).FirstAsync();
        var items = await db.Items.Include(i => i.Score).Include(i => i.RubricAnswers).Where(i => i.ProductId == productId).ToListAsync();

        RollupService.Apply(items);
        // The rubric runs after roll-up, so a parent's start-date criterion uses its rolled-up start.
        var rubric = product.ScoringModel == ScoringModel.Wsjf
            ? await LoadRubricAsync(db, productId, product.WsjfScale)
            : ScoringRubric.Empty;
        var today = Today;
        var strategy = Scoring.For(product.ScoringModel);
        foreach (var item in items)
        {
            item.Score ??= new ItemScore();
            if (!rubric.IsEmpty)
                RubricCalculator.Apply(item.Score, RubricCalculator.Evaluate(rubric, RubricCalculator.AnswersOf(item), item.StartDate, today));
            item.Score.ComputedScore = strategy.Compute(item.Score);
        }

        // Derived values aren't user edits; keep them out of the history.
        db.AuditEnabled = false;
        try { await db.SaveChangesAsync(); }
        finally { db.AuditEnabled = true; }
    }

    private static string TagList(IEnumerable<Tag> tags) => string.Join(", ", tags.Select(t => t.Name).Order());

    private static async Task<int> NextSortOrderAsync(RoadmapDbContext db, int productId, int? parentId) =>
        (await db.Items.Where(i => i.ProductId == productId && i.ParentId == parentId)
            .MaxAsync(i => (int?)i.SortOrder) ?? -1) + 1;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static InvalidOperationException NotFound(string what) => new($"{what} not found. It may have been deleted.");
}
