namespace Roadmapper.Core;

public record MatrixPoint(RoadmapItem Item, double Value, double Effort, Quadrant Quadrant);

public record PriorityMatrixResult(IReadOnlyList<MatrixPoint> Points, IReadOnlyList<RoadmapItem> Unplotted, double ValueSplit, double EffortSplit);

public static class PriorityMatrix
{
    /// <summary>
    /// Plots items by (value, effort) and splits the quadrants at the medians, so the split adapts
    /// to each product's own scale. Values on a split line count as high value / low effort.
    /// </summary>
    public static PriorityMatrixResult Build(IEnumerable<RoadmapItem> items, IScoringStrategy strategy)
    {
        var raw = new List<(RoadmapItem Item, double Value, double Effort)>();
        var unplotted = new List<RoadmapItem>();
        foreach (var item in items)
        {
            if (item.Score is { } s && strategy.MatrixPoint(s) is { } p) raw.Add((item, p.Value, p.Effort));
            else unplotted.Add(item);
        }

        var valueSplit = Median(raw.Select(r => r.Value));
        var effortSplit = Median(raw.Select(r => r.Effort));
        var points = raw.Select(r => new MatrixPoint(r.Item, r.Value, r.Effort, Classify(r.Value, r.Effort, valueSplit, effortSplit))).ToList();
        return new PriorityMatrixResult(points, unplotted, valueSplit, effortSplit);
    }

    public static Quadrant Classify(double value, double effort, double valueSplit, double effortSplit) =>
        (value >= valueSplit, effort <= effortSplit) switch
        {
            (true, true) => Quadrant.QuickWin,
            (true, false) => Quadrant.BigBet,
            (false, true) => Quadrant.FillIn,
            (false, false) => Quadrant.TimeSink,
        };

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
