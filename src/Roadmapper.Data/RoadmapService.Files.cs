using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;

namespace Roadmapper.Data;

/// <summary>Roadmapper (.rmp) files: a whole product, for backups and sharing. See <see cref="RoadmapFile"/>.</summary>
public partial class RoadmapService
{
    /// <summary>Everything in the product: settings, themes, tags, items, dependencies, templates, snapshots and history.</summary>
    public async Task<RoadmapFile?> ExportFileAsync(int productId)
    {
        if (await GetRoadmapAsync(productId) is not { } roadmap) return null;
        var templates = await GetTemplatesAsync(productId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var snapshots = await db.Snapshots.AsNoTracking().Where(s => s.ProductId == productId).OrderBy(s => s.TakenAt).ToListAsync();
        var history = await db.AuditEntries.AsNoTracking().Where(a => a.ProductId == productId)
            .OrderBy(a => a.ChangedAt).ThenBy(a => a.Id).ToListAsync();

        var product = roadmap.Product;
        var itemIds = roadmap.Items.Select(i => i.Id).ToHashSet();
        return new RoadmapFile
        {
            Product = new FileProduct { Name = product.Name, Description = product.Description, ScoringModel = product.ScoringModel },
            Themes = product.Themes.Select(t => new FileTheme(t.Id, t.Name, t.Color)).ToList(),
            Tags = product.Tags.Select(t => new FileTag(t.Id, t.Name)).ToList(),
            Items = roadmap.Flatten().Select(r => r.Item).Select(i => new FileItem
            {
                Id = i.Id, ParentId = i.ParentId, Type = i.ItemType, Title = i.Title, Description = i.Description,
                Status = i.Status, ProgressPct = i.ProgressPct, StartDate = i.StartDate, EndDate = i.EndDate,
                Horizon = i.Horizon, Owner = i.Owner, ThemeId = i.ThemeId,
                TagIds = i.Tags.Count == 0 ? null : i.Tags.Select(t => t.Id).Order().ToList(),
                SortOrder = i.SortOrder, ManualRank = i.ManualRank, IsRollupOverridden = i.IsRollupOverridden,
                Score = i.Score is { } s && HasInputs(s)
                    ? new FileScore(s.Reach, s.Impact, s.Confidence, s.Effort, s.BusinessValue, s.TimeCriticality, s.RiskReduction, s.JobSize, s.Moscow)
                    : null,
                SourceTemplateName = i.SourceTemplateName, SourceTemplateAppliedAt = i.SourceTemplateAppliedAt,
                TemplateBatchId = i.TemplateBatchId, CreatedAt = i.CreatedAt, UpdatedAt = i.UpdatedAt,
            }).ToList(),
            Dependencies = roadmap.Dependencies.Select(d => new FileDependency(d.PredecessorId, d.SuccessorId)).ToList(),
            Templates = templates.Select(t => new FileTemplate
            {
                Name = t.Name,
                Description = t.Description,
                Lines = t.Lines.Select(l => new FileTemplateLine(l.Title, l.Description, l.Owner, l.StartOffsetDays, l.DurationDays,
                    l.Tags.Count == 0 ? null : l.Tags.Select(x => x.Id).Order().ToList())).ToList(),
            }).ToList(),
            Snapshots = snapshots.Select(s => new FileSnapshot
            {
                Name = s.Name, Note = s.Note, TakenAt = s.TakenAt,
                Items = JsonSerializer.Deserialize<List<SnapshotItem>>(s.PayloadJson, SnapshotJson) ?? [],
            }).ToList(),
            History = history.Select(a => new FileHistoryEntry(a.ChangedAt, a.Action,
                a.ItemId is { } id && itemIds.Contains(id) ? id : null, a.ItemTitle, a.Field, a.OldValue, a.NewValue)).ToList(),
        };
    }

    /// <summary>
    /// Creates a new product from a file, in one transaction. Ids are re-assigned and every reference
    /// (parents, dependencies, snapshots, history) is relinked. Nothing existing is changed. If the
    /// product name is taken, " (imported)" is added. Returns the new product.
    /// </summary>
    public async Task<Product> ImportFileAsync(RoadmapFile file)
    {
        if (file.Validate() is { Count: > 0 } errors) throw new InvalidOperationException(string.Join(" ", errors));

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        // The file carries its own history, so don't record the import as a pile of new edits.
        db.AuditEnabled = false;

        var taken = (await db.Products.Select(p => p.Name).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var product = new Product
        {
            Name = UniqueProductName(file.Product.Name.Trim(), taken),
            Description = Clean(file.Product.Description),
            ScoringModel = file.Product.ScoringModel,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var themes = file.Themes.ToDictionary(t => t.Id, t => new Theme { ProductId = product.Id, Name = t.Name.Trim(), Color = t.Color });
        var tags = file.Tags.ToDictionary(t => t.Id, t => new Tag { ProductId = product.Id, Name = t.Name.Trim() });
        db.Themes.AddRange(themes.Values);
        db.Tags.AddRange(tags.Values);
        await db.SaveChangesAsync();

        // Parents before children, so each level can point at the ids just created for the level above.
        var created = new Dictionary<int, RoadmapItem>();
        var batches = new Dictionary<string, string>();
        foreach (var level in Enum.GetValues<ItemType>())
        {
            foreach (var siblings in file.Items.Where(i => i.Type == level).GroupBy(i => i.ParentId))
            {
                var sort = 0;
                foreach (var f in siblings.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
                {
                    var item = new RoadmapItem
                    {
                        ProductId = product.Id, ParentId = f.ParentId is { } p ? created[p].Id : null,
                        ItemType = f.Type, Title = f.Title.Trim(), Description = Clean(f.Description),
                        Status = f.Status, ProgressPct = f.Status == ItemStatus.Done ? 100 : f.ProgressPct,
                        StartDate = f.StartDate, EndDate = f.EndDate, Horizon = f.Horizon, Owner = Clean(f.Owner),
                        ThemeId = f.ThemeId is { } th ? themes[th].Id : null,
                        SortOrder = sort++, ManualRank = f.ManualRank, IsRollupOverridden = f.IsRollupOverridden,
                        SourceTemplateName = Clean(f.SourceTemplateName), SourceTemplateAppliedAt = f.SourceTemplateAppliedAt,
                        // New batch ids (same grouping), since undo finds a batch by id and must not reach the original product.
                        TemplateBatchId = Clean(f.TemplateBatchId) is { } batch
                            ? batches.TryGetValue(batch, out var nb) ? nb : batches[batch] = Guid.NewGuid().ToString("N")
                            : null,
                        CreatedAt = f.CreatedAt == default ? DateTime.UtcNow : f.CreatedAt,
                        UpdatedAt = f.UpdatedAt == default ? DateTime.UtcNow : f.UpdatedAt,
                        Score = f.Score is { } s
                            ? new ItemScore
                            {
                                Reach = s.Reach, Impact = s.Impact, Confidence = s.Confidence, Effort = s.Effort,
                                BusinessValue = s.BusinessValue, TimeCriticality = s.TimeCriticality,
                                RiskReduction = s.RiskReduction, JobSize = s.JobSize, Moscow = s.Moscow,
                            }
                            : null,
                    };
                    item.Tags.AddRange((f.TagIds ?? []).Distinct().Select(t => tags[t]));
                    db.Items.Add(item);
                    created[f.Id] = item;
                }
            }
            await db.SaveChangesAsync();
        }

        var links = new HashSet<(int, int)>();
        foreach (var d in file.Dependencies)
            if (links.Add((created[d.PredecessorId].Id, created[d.SuccessorId].Id)))
                db.Dependencies.Add(new Dependency { PredecessorId = created[d.PredecessorId].Id, SuccessorId = created[d.SuccessorId].Id });

        foreach (var t in file.Templates)
        {
            var template = new TaskTemplate { ProductId = product.Id, Name = t.Name.Trim(), Description = Clean(t.Description) };
            for (var i = 0; i < t.Lines.Count; i++)
            {
                var l = t.Lines[i];
                var line = new TaskTemplateLine
                {
                    SortOrder = i, Title = l.Title.Trim(), Description = Clean(l.Description), Owner = Clean(l.Owner),
                    StartOffsetDays = l.GapDays, DurationDays = l.DurationDays,
                };
                line.Tags.AddRange((l.TagIds ?? []).Distinct().Select(x => tags[x]));
                template.Lines.Add(line);
            }
            db.TaskTemplates.Add(template);
        }

        // Snapshot rows for items deleted before the export get negative ids, so they still show as "removed"
        // and can never be mistaken for a new item.
        var gone = new Dictionary<int, int>();
        int Relink(int oldId) => created.TryGetValue(oldId, out var item) ? item.Id
            : gone.TryGetValue(oldId, out var g) ? g : gone[oldId] = -(gone.Count + 1);
        foreach (var s in file.Snapshots)
        {
            var items = (s.Items ?? []).Select(i => i with { Id = Relink(i.Id), ParentId = i.ParentId is { } p ? Relink(p) : null }).ToList();
            db.Snapshots.Add(new Snapshot
            {
                ProductId = product.Id, Name = s.Name.Trim(), Note = Clean(s.Note), TakenAt = s.TakenAt,
                ItemCount = items.Count, PayloadJson = JsonSerializer.Serialize(items, SnapshotJson),
            });
        }

        db.AuditEntries.AddRange(file.History.Select(h => new AuditEntry
        {
            ProductId = product.Id, ChangedAt = h.ChangedAt, Action = h.Action,
            ItemId = h.ItemId is { } id && created.TryGetValue(id, out var item) ? item.Id : null,
            ItemTitle = h.ItemTitle, Field = h.Field, OldValue = h.OldValue, NewValue = h.NewValue,
        }));

        await db.SaveChangesAsync();
        await RecalculateAsync(db, product.Id);
        await tx.CommitAsync();
        return product;
    }

    private static bool HasInputs(ItemScore s) =>
        s.Reach is not null || s.Impact is not null || s.Confidence is not null || s.Effort is not null ||
        s.BusinessValue is not null || s.TimeCriticality is not null || s.RiskReduction is not null ||
        s.JobSize is not null || s.Moscow is not null;

    /// <summary>"Name", or "Name (imported)", "Name (imported 2)", … when the name is taken.</summary>
    public static string UniqueProductName(string name, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(name)) return name;
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? $"{name} (imported)" : $"{name} (imported {n})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
