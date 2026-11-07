using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;

namespace Roadmapper.Data;

/// <summary>
/// The WSJF scoring rubric: criteria that turn answers into factor scores. Calculated factors are written
/// into the item's ordinary WSJF fields, so ranking, the matrix, snapshots and exports need no changes.
/// See <see cref="RubricCalculator"/> for how a factor is worked out.
/// </summary>
public partial class RoadmapService
{
    public async Task<ScoringRubric> GetRubricAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var scale = await db.Products.Where(p => p.Id == productId).Select(p => p.WsjfScale).FirstOrDefaultAsync();
        return await LoadRubricAsync(db, productId, scale);
    }

    /// <summary>Adds a criterion (Id 0) or replaces one, including its bands or choices.</summary>
    public async Task<int> SaveCriterionAsync(ScoringCriterion draft)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var product = await db.Products.FindAsync(draft.ProductId) ?? throw NotFound("Product");

        Normalize(draft);
        if (RubricRules.Validate(draft, RubricScale.Parse(product.WsjfScale)) is { } error) throw new InvalidOperationException(error);

        ScoringCriterion criterion;
        if (draft.Id == 0)
        {
            criterion = new ScoringCriterion
            {
                ProductId = draft.ProductId,
                SortOrder = (await db.ScoringCriteria.Where(c => c.ProductId == draft.ProductId).MaxAsync(c => (int?)c.SortOrder) ?? -1) + 1,
            };
            db.ScoringCriteria.Add(criterion);
        }
        else
        {
            criterion = await db.ScoringCriteria.Include(c => c.Options).FirstOrDefaultAsync(c => c.Id == draft.Id && c.ProductId == draft.ProductId)
                ?? throw NotFound("Criterion");
            // Answers to a number can't be read as a choice (or the reverse), so they start over.
            if (criterion.Kind != draft.Kind)
                await db.ItemRubricAnswers.Where(a => a.CriterionId == criterion.Id).ExecuteDeleteAsync();
        }

        criterion.Factor = draft.Factor;
        criterion.Role = draft.Role;
        criterion.Kind = draft.Kind;
        criterion.Name = draft.Name;
        criterion.Help = draft.Help;

        // Keep options by id so answers that picked them survive; removing an option removes those answers.
        var keep = draft.Options.Where(o => o.Id != 0).Select(o => o.Id).ToHashSet();
        foreach (var gone in criterion.Options.Where(o => !keep.Contains(o.Id)).ToList()) criterion.Options.Remove(gone);
        for (var i = 0; i < draft.Options.Count; i++)
        {
            var d = draft.Options[i];
            var option = d.Id == 0 ? null : criterion.Options.FirstOrDefault(o => o.Id == d.Id);
            if (option is null)
            {
                option = new ScoringCriterionOption();
                criterion.Options.Add(option);
            }
            option.SortOrder = i;
            option.Label = d.Label;
            option.From = d.From;
            option.Value = d.Value;
        }

        await db.SaveChangesAsync();
        await RecalculateAsync(db, draft.ProductId);
        return criterion.Id;
    }

    /// <summary>Deletes a criterion and every answer to it. Factor values it calculated stay until something recalculates them.</summary>
    public async Task DeleteCriterionAsync(int criterionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ScoringCriteria.FindAsync(criterionId) is not { } criterion) return;
        db.ScoringCriteria.Remove(criterion);
        await db.SaveChangesAsync();
        await RecalculateAsync(db, criterion.ProductId);
    }

    /// <summary>Swaps the criterion with the previous (-1) or next (+1) one for the same factor.</summary>
    public async Task MoveCriterionAsync(int criterionId, int direction)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var criterion = await db.ScoringCriteria.FindAsync(criterionId) ?? throw NotFound("Criterion");
        var all = await db.ScoringCriteria.Where(c => c.ProductId == criterion.ProductId).ToListAsync();
        var ordered = all.OrderBy(c => c.Factor).ThenBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
        var siblings = ordered.Where(c => c.Factor == criterion.Factor).ToList();
        var target = siblings.IndexOf(criterion) + Math.Sign(direction);
        if (target < 0 || target >= siblings.Count) return;

        var a = ordered.IndexOf(criterion);
        var b = ordered.IndexOf(siblings[target]);
        (ordered[a], ordered[b]) = (ordered[b], ordered[a]);
        for (var i = 0; i < ordered.Count; i++) ordered[i].SortOrder = i;
        await db.SaveChangesAsync();
    }

    /// <summary>Sets the scale, e.g. "1, 2, 3, 5, 8, 13, 20". Blank resets it to the default.</summary>
    public async Task SetScaleAsync(int productId, string? text)
    {
        if (!RubricScale.TryParse(text, out var scale, out var error)) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var product = await db.Products.FindAsync(productId) ?? throw NotFound("Product");
        var criteria = await db.ScoringCriteria.Include(c => c.Options)
            .Where(c => c.ProductId == productId && c.Role == CriterionRole.Base).ToListAsync();
        var off = criteria
            .SelectMany(c => c.Options.Where(o => !RubricScale.Contains(scale, o.Value)).Select(o => $"{c.Name} ({RubricText.Number(o.Value)})"))
            .Distinct().ToList();
        if (off.Count > 0)
            throw new InvalidOperationException($"Change these scores first; they aren't on the new scale: {string.Join(", ", off)}.");

        product.WsjfScale = scale.SequenceEqual(RubricScale.Default) ? null : RubricScale.Format(scale);
        await db.SaveChangesAsync();
        await RecalculateAsync(db, productId);
    }

    /// <summary>Adds <see cref="StarterRubric"/> to a product that has no criteria yet.</summary>
    public async Task AddStarterRubricAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var product = await db.Products.FindAsync(productId) ?? throw NotFound("Product");
        if (await db.ScoringCriteria.AnyAsync(c => c.ProductId == productId))
            throw new InvalidOperationException("This product already has a scoring rubric.");

        var scale = RubricScale.Parse(product.WsjfScale);
        var starter = StarterRubric.Create(productId);
        // The starter's scores come from the default scale; snap them in case this product uses another one.
        foreach (var option in starter.SelectMany(c => c.Options)) option.Value = RubricScale.Snap(scale, option.Value);
        db.ScoringCriteria.AddRange(starter);
        await db.SaveChangesAsync();
        await RecalculateAsync(db, productId);
    }

    /// <summary>
    /// Re-scores every WSJF product with a rubric, so start-date criteria keep up with the calendar.
    /// Runs at startup; it isn't an edit, so it isn't recorded in the history and doesn't touch UpdatedAt.
    /// </summary>
    public async Task RecalculateAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = await db.Products
            .Where(p => p.ScoringModel == ScoringModel.Wsjf && db.ScoringCriteria.Any(c => c.ProductId == p.Id))
            .Select(p => p.Id).ToListAsync();
        foreach (var id in ids) await RecalculateAsync(db, id);
    }

    /// <summary>Recalculates one product's roll-ups and scores (the Scoring page's Recalculate button).</summary>
    public async Task RecalculateProductAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await RecalculateAsync(db, productId);
    }

    private static async Task<ScoringRubric> LoadRubricAsync(RoadmapDbContext db, int productId, string? scale)
    {
        var criteria = await db.ScoringCriteria.AsNoTracking()
            .Include(c => c.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
            .Where(c => c.ProductId == productId)
            .ToListAsync();
        return new ScoringRubric(RubricScale.Parse(scale), criteria);
    }

    /// <summary>
    /// Part of saving an item: stores its rubric answers (recording changes in the history) and, under WSJF,
    /// writes the calculated factors before the save so the history shows them as ordinary score changes.
    /// </summary>
    private async Task ApplyRubricAsync(RoadmapDbContext db, Product product, RoadmapItem item, ItemDraft draft)
    {
        var rubric = await LoadRubricAsync(db, product.Id, product.WsjfScale);
        var score = item.Score!;

        // A value the rubric can't calculate can only have been typed, so protect it if a criterion is added later.
        foreach (var factor in RubricCalculator.Factors)
            if (!rubric.Calculates(factor) && RubricCalculator.Get(score, factor) is not null)
                score.ManualFactors |= RubricCalculator.Flag(factor);

        foreach (var criterion in rubric.Criteria.Where(c => c.Kind != CriterionKind.StartDate))
        {
            var wanted = draft.Answers.GetValueOrDefault(criterion.Id) is { } a && IsValidAnswer(criterion, a) ? a : null;
            var existing = item.RubricAnswers.FirstOrDefault(x => x.CriterionId == criterion.Id);
            var oldText = existing is null ? null : RubricText.Answer(criterion, RubricAnswer.From(existing));
            var newText = RubricText.Answer(criterion, wanted);

            if (wanted is null)
            {
                if (existing is not null) item.RubricAnswers.Remove(existing);
            }
            else if (existing is null)
            {
                item.RubricAnswers.Add(new ItemRubricAnswer { CriterionId = criterion.Id, Number = wanted.Number, OptionId = wanted.OptionId });
            }
            else
            {
                existing.Number = wanted.Number;
                existing.OptionId = wanted.OptionId;
            }

            // Answers live in their own table, which the audit trail doesn't watch, so record them here.
            if (!draft.IsNew && oldText != newText)
                db.AuditEntries.Add(new AuditEntry
                {
                    ProductId = item.ProductId, ItemId = item.Id, ItemTitle = item.Title, Action = AuditAction.Updated,
                    Field = $"Rubric: {criterion.Name}", OldValue = oldText, NewValue = newText,
                });
        }

        if (product.ScoringModel == ScoringModel.Wsjf && !rubric.IsEmpty)
            RubricCalculator.Apply(score, RubricCalculator.Evaluate(rubric, RubricCalculator.AnswersOf(item), item.StartDate, Today));
    }

    private static bool IsValidAnswer(ScoringCriterion c, RubricAnswer a) => c.Kind switch
    {
        CriterionKind.Choice => a.OptionId is { } id && c.Options.Any(o => o.Id == id),
        CriterionKind.NumberBands => a.Number is { } n && double.IsFinite(n),
        _ => false,
    };

    private static void Normalize(ScoringCriterion c)
    {
        c.Name = c.Name?.Trim() ?? "";
        c.Help = Clean(c.Help);
        foreach (var o in c.Options)
        {
            o.Label = Clean(o.Label);
            if (c.Kind == CriterionKind.Choice) o.From = null;
        }
    }
}
