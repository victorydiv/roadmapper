namespace Roadmapper.Core;

public interface IScoringStrategy
{
    ScoringModel Model { get; }
    string DisplayName { get; }
    /// <summary>Returns null when required inputs are missing or invalid.</summary>
    double? Compute(ItemScore score);

    string ValueLabel { get; }
    string EffortLabel { get; }
    /// <summary>The (value, effort) pair plotted on the prioritization matrix; null when inputs are missing.</summary>
    (double Value, double Effort)? MatrixPoint(ItemScore score);
}

/// <summary>(Reach × Impact × Confidence%) / Effort.</summary>
public sealed class RiceStrategy : IScoringStrategy
{
    public ScoringModel Model => ScoringModel.Rice;
    public string DisplayName => "RICE";
    public string ValueLabel => "Value (reach × impact × confidence)";
    public string EffortLabel => "Effort (person-months)";

    public double? Compute(ItemScore s) => MatrixPoint(s) is { } p ? p.Value / p.Effort : null;

    public (double Value, double Effort)? MatrixPoint(ItemScore s) =>
        s is { Reach: { } r, Impact: { } i, Confidence: { } c, Effort: { } e } && e > 0
            ? (r * i * (c / 100.0), e)
            : null;
}

/// <summary>Cost of delay (business value + time criticality + risk reduction) / job size.</summary>
public sealed class WsjfStrategy : IScoringStrategy
{
    public ScoringModel Model => ScoringModel.Wsjf;
    public string DisplayName => "WSJF";
    public string ValueLabel => "Cost of delay";
    public string EffortLabel => "Job size";

    public double? Compute(ItemScore s) => MatrixPoint(s) is { } p ? p.Value / p.Effort : null;

    public (double Value, double Effort)? MatrixPoint(ItemScore s) =>
        s is { BusinessValue: { } bv, TimeCriticality: { } tc, RiskReduction: { } rr, JobSize: { } js } && js > 0
            ? (bv + tc + rr, js)
            : null;
}

/// <summary>Must = 4, Should = 3, Could = 2, Won't = 1. MoSCoW has no effort term, so the matrix uses the Effort field.</summary>
public sealed class MoscowStrategy : IScoringStrategy
{
    public ScoringModel Model => ScoringModel.Moscow;
    public string DisplayName => "MoSCoW";
    public string ValueLabel => "MoSCoW priority";
    public string EffortLabel => "Effort";

    public double? Compute(ItemScore s) => s.Moscow is { } m ? (int)m : null;

    public (double Value, double Effort)? MatrixPoint(ItemScore s) =>
        s is { Moscow: { } m, Effort: { } e } && e > 0 ? ((int)m, e) : null;
}

public static class Scoring
{
    private static readonly IScoringStrategy[] Strategies = [new RiceStrategy(), new WsjfStrategy(), new MoscowStrategy()];

    public static IScoringStrategy For(ScoringModel model) => Strategies.Single(s => s.Model == model);

    /// <summary>Manually ranked items first (by rank), then by computed score descending, unscored last.</summary>
    public static IEnumerable<RoadmapItem> InRankOrder(this IEnumerable<RoadmapItem> items) =>
        items.OrderBy(i => i.ManualRank is null)
             .ThenBy(i => i.ManualRank)
             .ThenByDescending(i => i.Score?.ComputedScore ?? double.MinValue)
             .ThenBy(i => i.SortOrder);
}
