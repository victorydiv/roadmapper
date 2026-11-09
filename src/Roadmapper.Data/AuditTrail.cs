using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Roadmapper.Core;

namespace Roadmapper.Data;

/// <summary>
/// Turns tracked changes into <see cref="AuditEntry"/> rows. Capture runs before SaveChanges (while
/// original values are still known); Resolve runs after it, once new items have ids, and swaps
/// foreign keys for readable names.
/// </summary>
internal static class AuditTrail
{
    internal sealed record Pending(object Entity, AuditAction Action, string? Property, object? Old, object? New);

    private static readonly Dictionary<string, string> ItemFields = new()
    {
        [nameof(RoadmapItem.Title)] = "Title",
        [nameof(RoadmapItem.Description)] = "Description",
        [nameof(RoadmapItem.Status)] = "Status",
        [nameof(RoadmapItem.ProgressPct)] = "Progress",
        [nameof(RoadmapItem.StartDate)] = "Start date",
        [nameof(RoadmapItem.EndDate)] = "End date",
        [nameof(RoadmapItem.Horizon)] = "Horizon",
        [nameof(RoadmapItem.Owner)] = "Owner",
        [nameof(RoadmapItem.JiraKey)] = "Jira key",
        [nameof(RoadmapItem.ExternalUrl)] = "Link",
        [nameof(RoadmapItem.ThemeId)] = "Theme",
        [nameof(RoadmapItem.ParentId)] = "Parent",
        [nameof(RoadmapItem.ManualRank)] = "Manual rank",
        [nameof(RoadmapItem.IsRollupOverridden)] = "Manual dates/progress",
    };

    private static readonly Dictionary<string, string> ScoreFields = new()
    {
        [nameof(ItemScore.Reach)] = "Reach",
        [nameof(ItemScore.Impact)] = "Impact",
        [nameof(ItemScore.Confidence)] = "Confidence",
        [nameof(ItemScore.Effort)] = "Effort",
        [nameof(ItemScore.BusinessValue)] = "Business value",
        [nameof(ItemScore.TimeCriticality)] = "Time criticality",
        [nameof(ItemScore.RiskReduction)] = "Risk reduction",
        [nameof(ItemScore.JobSize)] = "Job size",
        [nameof(ItemScore.Moscow)] = "MoSCoW",
    };

    private static readonly Dictionary<string, string> ProductFields = new()
    {
        [nameof(Product.Name)] = "Product name",
        [nameof(Product.Description)] = "Product description",
        [nameof(Product.ScoringModel)] = "Scoring model",
    };

    public static List<Pending> Capture(ChangeTracker tracker)
    {
        var pending = new List<Pending>();
        foreach (var entry in tracker.Entries())
        {
            switch (entry.Entity, entry.State)
            {
                case (RoadmapItem i, EntityState.Added):
                    pending.Add(new(i, AuditAction.Created, null, null, null));
                    break;
                case (RoadmapItem i, EntityState.Deleted):
                    pending.Add(new(i, AuditAction.Deleted, null, null, null));
                    break;
                case (RoadmapItem, EntityState.Modified):
                    pending.AddRange(Diff(entry, ItemFields));
                    break;
                case (ItemScore, EntityState.Modified):
                    pending.AddRange(Diff(entry, ScoreFields));
                    break;
                case (Product, EntityState.Modified):
                    pending.AddRange(Diff(entry, ProductFields));
                    break;
                case (Dependency d, EntityState.Added):
                    pending.Add(new(d, AuditAction.Created, "Blocked by", null, d.PredecessorId));
                    break;
                case (Dependency d, EntityState.Deleted):
                    pending.Add(new(d, AuditAction.Deleted, "Blocked by", d.PredecessorId, null));
                    break;
            }
        }
        return pending;
    }

    public static async Task<List<AuditEntry>> ResolveAsync(RoadmapDbContext db, List<Pending> pending, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entries = new List<AuditEntry>();
        foreach (var p in pending)
        {
            // Who the change is about: (product, item id, item title).
            (int ProductId, int? ItemId, string? Title)? subject = p.Entity switch
            {
                RoadmapItem i => (i.ProductId, i.Id, i.Title),
                ItemScore s => await ItemSubjectAsync(db, s.ItemId, ct),
                Dependency d => await ItemSubjectAsync(db, d.SuccessorId, ct),
                Product pr => (pr.Id, null, null),
                _ => null,
            };
            if (subject is not { } subj) continue;

            entries.Add(new AuditEntry
            {
                ProductId = subj.ProductId,
                ItemId = subj.ItemId,
                ItemTitle = subj.Title,
                Action = p.Action,
                Field = p.Property switch
                {
                    null => null,
                    _ when p.Entity is RoadmapItem => ItemFields[p.Property],
                    _ when p.Entity is ItemScore => ScoreFields[p.Property],
                    _ when p.Entity is Product => ProductFields[p.Property],
                    _ => p.Property,
                },
                OldValue = await FormatAsync(db, p, p.Old, ct),
                NewValue = await FormatAsync(db, p, p.New, ct),
                ChangedAt = now,
            });
        }
        return entries;
    }

    private static IEnumerable<Pending> Diff(EntityEntry entry, Dictionary<string, string> fields) =>
        entry.Properties
            .Where(prop => prop.IsModified && fields.ContainsKey(prop.Metadata.Name) && !Equals(prop.OriginalValue, prop.CurrentValue))
            .Select(prop => new Pending(entry.Entity, AuditAction.Updated, prop.Metadata.Name, prop.OriginalValue, prop.CurrentValue));

    private static async Task<(int, int?, string?)?> ItemSubjectAsync(RoadmapDbContext db, int itemId, CancellationToken ct)
    {
        var local = db.Items.Local.FirstOrDefault(i => i.Id == itemId);
        if (local is not null) return (local.ProductId, local.Id, local.Title);
        var row = await db.Items.AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => new { i.ProductId, i.Title }).FirstOrDefaultAsync(ct);
        return row is null ? null : (row.ProductId, itemId, row.Title);
    }

    private static async Task<string?> FormatAsync(RoadmapDbContext db, Pending p, object? value, CancellationToken ct)
    {
        if (value is null) return null;

        // Foreign keys are stored as names so history stays readable after renames/deletes.
        if (p.Entity is Dependency || p.Property == nameof(RoadmapItem.ParentId))
            return await ItemSubjectAsync(db, (int)value, ct) is { } subject ? subject.Item3 : $"#{value}";
        if (p.Property == nameof(RoadmapItem.ThemeId))
            return await db.Themes.AsNoTracking().Where(t => t.Id == (int)value).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? $"#{value}";

        var text = value switch
        {
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ItemStatus s => s == ItemStatus.InProgress ? "In progress" : s.ToString(),
            bool b => b ? "Yes" : "No",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        return text is { Length: > 300 } ? text[..297] + "…" : text;
    }
}
