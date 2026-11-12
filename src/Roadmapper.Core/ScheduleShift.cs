namespace Roadmapper.Core;

/// <summary>
/// Moving an item together with its sub-items: every descendant's dates move by the same number of days as the
/// item, so their spacing relative to each other is kept.
/// </summary>
public static class ScheduleShift
{
    /// <summary>
    /// How many days an item moved: the change in its start when it had a start before and after and that changed,
    /// otherwise the change in its end. 0 when neither can be measured.
    /// </summary>
    public static int DaysMoved(DateOnly? oldStart, DateOnly? oldEnd, DateOnly? newStart, DateOnly? newEnd)
    {
        if (oldStart is { } os && newStart is { } ns && ns != os) return ns.DayNumber - os.DayNumber;
        if (oldEnd is { } oe && newEnd is { } ne) return ne.DayNumber - oe.DayNumber;
        return 0;
    }

    /// <summary>Moves each item's start and end (whichever it has) by <paramref name="days"/>. Returns how many items moved.</summary>
    public static int Apply(IEnumerable<RoadmapItem> items, int days)
    {
        if (days == 0) return 0;
        var moved = 0;
        foreach (var i in items)
        {
            if (i.StartDate is null && i.EndDate is null) continue;
            i.StartDate = i.StartDate?.AddDays(days);
            i.EndDate = i.EndDate?.AddDays(days);
            moved++;
        }
        return moved;
    }

    /// <summary>Every item below <paramref name="rootId"/>, at any depth.</summary>
    public static List<RoadmapItem> DescendantsOf(IEnumerable<RoadmapItem> items, int rootId)
    {
        var byParent = items.Where(i => i.ParentId != null).ToLookup(i => i.ParentId!.Value);
        var result = new List<RoadmapItem>();
        var stack = new Stack<int>([rootId]);
        while (stack.TryPop(out var id))
            foreach (var c in byParent[id])
            {
                result.Add(c);
                stack.Push(c.Id);
            }
        return result;
    }

    /// <summary>"+14 days", "−3 days", "+1 day".</summary>
    public static string Describe(int days) =>
        $"{(days < 0 ? "−" : "+")}{Math.Abs(days)} day{(Math.Abs(days) == 1 ? "" : "s")}";
}
