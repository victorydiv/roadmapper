using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;

namespace Roadmapper.Data;

/// <summary>Result of applying a template: the batch to undo, and how many tasks went to how many Features.</summary>
public record TemplateBatch(string BatchId, string TemplateName, int TaskCount, int FeatureCount);

/// <summary>Task templates: managing them, applying them to Features, and undoing an application.</summary>
public partial class RoadmapService
{
    /// <summary>The product's templates by name, each with its lines in order and their tags.</summary>
    public async Task<List<TaskTemplate>> GetTemplatesAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var templates = await db.TaskTemplates.AsNoTracking()
            .Where(t => t.ProductId == productId)
            .Include(t => t.Lines).ThenInclude(l => l.Tags)
            .ToListAsync();
        foreach (var t in templates) t.Lines = t.Lines.OrderBy(l => l.SortOrder).ToList();
        return templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Creates or updates a template, replacing its lines with the draft's, in the draft's order.</summary>
    public async Task<int> SaveTemplateAsync(TemplateDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        var name = draft.Name.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Products.AnyAsync(p => p.Id == draft.ProductId)) throw NotFound("Product");
        await EnsureUniqueNameAsync(db, draft.ProductId, name, draft.Id);

        TaskTemplate template;
        if (draft.IsNew)
        {
            template = new TaskTemplate { ProductId = draft.ProductId };
            db.TaskTemplates.Add(template);
        }
        else
        {
            template = await db.TaskTemplates.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == draft.Id) ?? throw NotFound("Template");
            db.TaskTemplateLines.RemoveRange(template.Lines);
            template.Lines.Clear();
            template.UpdatedAt = DateTime.UtcNow;
        }
        template.Name = name;
        template.Description = Clean(draft.Description);

        var tagIds = draft.Lines.SelectMany(l => l.TagIds).Distinct().ToList();
        var tags = await db.Tags.Where(t => t.ProductId == draft.ProductId && tagIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var l = draft.Lines[i];
            template.Lines.Add(new TaskTemplateLine
            {
                SortOrder = i,
                Title = l.Title.Trim(),
                Description = Clean(l.Description),
                Owner = Clean(l.Owner),
                StartOffsetDays = l.StartOffsetDays,
                DurationDays = l.DurationDays,
                Tags = l.TagIds.Where(tags.ContainsKey).Select(id => tags[id]).ToList(),
            });
        }
        await db.SaveChangesAsync();
        return template.Id;
    }

    public async Task<int> DuplicateTemplateAsync(int templateId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var source = await db.TaskTemplates.AsNoTracking().Include(t => t.Lines).ThenInclude(l => l.Tags)
            .FirstOrDefaultAsync(t => t.Id == templateId) ?? throw NotFound("Template");
        var taken = await db.TaskTemplates.Where(t => t.ProductId == source.ProductId).Select(t => t.Name).ToListAsync();

        var copy = TemplateDraft.From(source);
        copy.Id = 0;
        copy.Name = TemplateRules.CopyName(source.Name, taken);
        return await SaveTemplateAsync(copy);
    }

    /// <summary>Deletes only the template. Tasks created from it keep their copied fields and reference.</summary>
    public async Task DeleteTemplateAsync(int templateId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.TaskTemplates.Where(t => t.Id == templateId).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Creates the checked tasks under their Features in one save, in the order given, each tagged with the
    /// template's name, the time applied and a shared batch id for undo. Status is Idea and progress 0.
    /// </summary>
    public async Task<TemplateBatch> ApplyTemplateAsync(int templateId, IReadOnlyList<PlannedTask> tasks)
    {
        var chosen = tasks.Where(t => t.Include).ToList();
        if (chosen.Count == 0) throw new InvalidOperationException("Choose at least one task to add.");
        foreach (var t in chosen)
        {
            if (string.IsNullOrWhiteSpace(t.Title)) throw new InvalidOperationException("Every task needs a title.");
            if (t.StartDate is { } s && t.EndDate is { } e && e < s)
                throw new InvalidOperationException($"\"{t.Title}\": the end date can't be before the start date.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var template = await db.TaskTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId) ?? throw NotFound("Template");
        var featureIds = chosen.Select(t => t.FeatureId).Distinct().ToList();
        var features = await db.Items.Where(i => featureIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        foreach (var id in featureIds)
        {
            if (!features.TryGetValue(id, out var f) || f.ProductId != template.ProductId) throw NotFound("Feature");
            if (f.ItemType != ItemType.Feature) throw new InvalidOperationException($"\"{f.Title}\" isn't a Feature; templates add tasks to Features.");
        }

        var tagIds = chosen.SelectMany(t => t.TagIds).Distinct().ToList();
        var tags = await db.Tags.Where(t => t.ProductId == template.ProductId && tagIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var batchId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var nextSort = new Dictionary<int, int>();
        foreach (var t in chosen)
        {
            if (!nextSort.TryGetValue(t.FeatureId, out var sort)) sort = await NextSortOrderAsync(db, template.ProductId, t.FeatureId);
            nextSort[t.FeatureId] = sort + 1;
            db.Items.Add(new RoadmapItem
            {
                ProductId = template.ProductId,
                ParentId = t.FeatureId,
                ItemType = ItemType.Task,
                SortOrder = sort,
                Title = t.Title.Trim(),
                Description = Clean(t.Description),
                Owner = Clean(t.Owner),
                Status = ItemStatus.Idea,
                ProgressPct = 0,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                Tags = t.TagIds.Where(tags.ContainsKey).Select(id => tags[id]).ToList(),
                SourceTemplateName = template.Name,
                SourceTemplateAppliedAt = now,
                TemplateBatchId = batchId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(); // one save: all tasks or none
        await RecalculateAsync(db, template.ProductId);
        return new TemplateBatch(batchId, template.Name, chosen.Count, featureIds.Count);
    }

    /// <summary>Deletes every task still left from one template application (and their dependencies). Returns how many.</summary>
    public async Task<int> UndoTemplateBatchAsync(string batchId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var items = await db.Items.Where(i => i.TemplateBatchId == batchId).ToListAsync();
        if (items.Count == 0) return 0;
        db.Items.RemoveRange(items);
        await db.SaveChangesAsync();
        await RecalculateAsync(db, items[0].ProductId);
        return items.Count;
    }

    /// <summary>
    /// Adds templates from an export, creating missing tags by name. Templates whose name already exists
    /// in the product are skipped (never overwritten); their names are returned.
    /// </summary>
    public async Task<(int Created, List<string> Skipped)> ImportTemplatesAsync(int productId, IReadOnlyList<ImportedTemplate> templates)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Products.AnyAsync(p => p.Id == productId)) throw NotFound("Product");
        var taken = (await db.TaskTemplates.Where(t => t.ProductId == productId).Select(t => t.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tags = (await db.Tags.Where(t => t.ProductId == productId).ToListAsync())
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var skipped = new List<string>();
        var created = 0;
        foreach (var t in templates)
        {
            var name = t.Name.Trim();
            if (name.Length == 0 || t.Lines.Count == 0) continue;
            if (!taken.Add(name)) { skipped.Add(name); continue; }

            var template = new TaskTemplate { ProductId = productId, Name = name[..Math.Min(name.Length, TemplateRules.MaxNameLength)], Description = Clean(t.Description) };
            for (var i = 0; i < t.Lines.Count; i++)
            {
                var l = t.Lines[i];
                var line = new TaskTemplateLine
                {
                    SortOrder = i, Title = l.Title, Description = Clean(l.Description), Owner = Clean(l.Owner),
                    StartOffsetDays = l.StartOffsetDays, DurationDays = l.DurationDays,
                };
                foreach (var tagName in l.TagNames)
                {
                    if (!tags.TryGetValue(tagName, out var tag))
                    {
                        tag = new Tag { ProductId = productId, Name = tagName };
                        db.Tags.Add(tag);
                        tags[tagName] = tag;
                    }
                    line.Tags.Add(tag);
                }
                template.Lines.Add(line);
            }
            db.TaskTemplates.Add(template);
            created++;
        }
        await db.SaveChangesAsync();
        return (created, skipped);
    }

    private static async Task EnsureUniqueNameAsync(RoadmapDbContext db, int productId, string name, int exceptId)
    {
        // The column is NOCASE, so this comparison ignores case like the unique index does.
        if (await db.TaskTemplates.AnyAsync(t => t.ProductId == productId && t.Id != exceptId && t.Name == name))
            throw new InvalidOperationException($"A template named \"{name}\" already exists.");
    }
}
