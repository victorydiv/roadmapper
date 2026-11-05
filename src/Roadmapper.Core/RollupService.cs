namespace Roadmapper.Core;

public static class RollupService
{
    /// <summary>
    /// Recomputes every parent's dates and progress from its non-cancelled children, bottom-up so
    /// changes cascade Task → Feature → Initiative → Goal. Leaves, overridden parents, and parents
    /// whose children are all cancelled keep their own values. Dates roll up only when at least one
    /// child has them.
    /// </summary>
    public static void Apply(IReadOnlyCollection<RoadmapItem> items)
    {
        var childrenByParent = items.Where(i => i.ParentId != null).ToLookup(i => i.ParentId!.Value);

        foreach (var item in items.OrderByDescending(i => i.ItemType))
        {
            if (item.IsRollupOverridden) continue;

            var children = childrenByParent[item.Id].Where(c => c.Status != ItemStatus.Cancelled).ToList();
            if (children.Count == 0) continue;

            var start = children.Min(c => c.StartDate);
            var end = children.Max(c => c.EndDate);
            if (start != null) item.StartDate = start;
            if (end != null) item.EndDate = end;

            item.ProgressPct = (int)Math.Round(children.Average(c => c.ProgressPct), MidpointRounding.AwayFromZero);
        }
    }
}
