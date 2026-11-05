namespace Roadmapper.Core;

/// <summary>Date math shared by the on-screen timeline and the SVG/PNG export.</summary>
public static class TimelineLayout
{
    public const double MonthPxPerDay = 6, QuarterPxPerDay = 2.2;

    /// <summary>A drawable span; a single date is drawn as a one-day bar.</summary>
    public static (DateOnly Start, DateOnly End)? Span(DateOnly? start, DateOnly? end) =>
        (start ?? end, end ?? start) is ({ } s, { } e) ? (s, e) : null;

    public static (DateOnly Start, DateOnly End)? Span(RoadmapItem i) => Span(i.StartDate, i.EndDate);

    /// <summary>Whole months covering every span and today, plus padding at the end.</summary>
    public static (DateOnly Start, DateOnly End) Range(IEnumerable<(DateOnly Start, DateOnly End)> spans, DateOnly today)
    {
        var list = spans.ToList();
        var min = list.Count == 0 ? today : list.Min(s => s.Start);
        var max = list.Count == 0 ? today.AddMonths(3) : list.Max(s => s.End);
        if (today < min) min = today;
        if (today > max) max = today;
        return (new DateOnly(min.Year, min.Month, 1), new DateOnly(max.Year, max.Month, 1).AddMonths(2));
    }

    /// <summary>Header text under a month name: the year at January / the first month, else the quarter (quarter zoom only).</summary>
    public static string MonthCaption(DateOnly month, DateOnly rangeStart, bool quarterScale) =>
        month.Month == 1 || month == rangeStart ? month.Year.ToString()
        : quarterScale && month.Month % 3 == 1 ? $"Q{(month.Month + 2) / 3}"
        : "";
}
