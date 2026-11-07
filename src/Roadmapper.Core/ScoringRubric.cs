using System.Globalization;

namespace Roadmapper.Core;

/// <summary>An item's answer to one rubric criterion (a number for number bands, an option for a pick-list).</summary>
public sealed record RubricAnswer(double? Number, int? OptionId)
{
    public static RubricAnswer From(ItemRubricAnswer a) => new(a.Number, a.OptionId);
}

/// <summary>A product's WSJF scoring rubric: its scale and its criteria, ordered by factor then position.</summary>
public sealed class ScoringRubric
{
    public static readonly ScoringRubric Empty = new(RubricScale.Default, []);

    public ScoringRubric(IReadOnlyList<double> scale, IEnumerable<ScoringCriterion> criteria)
    {
        Scale = scale;
        Criteria = criteria.OrderBy(c => c.Factor).ThenBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
    }

    public IReadOnlyList<double> Scale { get; }
    public IReadOnlyList<ScoringCriterion> Criteria { get; }
    public bool IsEmpty => Criteria.Count == 0;

    public IEnumerable<ScoringCriterion> ForFactor(WsjfFactor factor) => Criteria.Where(c => c.Factor == factor);

    /// <summary>True when the rubric can calculate this factor (it has at least one base criterion for it).</summary>
    public bool Calculates(WsjfFactor factor) => Criteria.Any(c => c.Factor == factor && c.Role == CriterionRole.Base);
}

/// <summary>The scale factor scores live on, e.g. 1, 2, 3, 5, 8, 13, 20. Adjusters move a score along it.</summary>
public static class RubricScale
{
    public static readonly IReadOnlyList<double> Default = [1, 2, 3, 5, 8, 13, 20];
    public const int MaxValues = 15;

    /// <summary>The stored scale, or the default when it's blank or unreadable.</summary>
    public static IReadOnlyList<double> Parse(string? text) => TryParse(text, out var scale, out _) ? scale : Default;

    /// <summary>Reads "1, 2, 3, 5" (commas, spaces or semicolons). Blank means the default scale.</summary>
    public static bool TryParse(string? text, out IReadOnlyList<double> scale, out string? error)
    {
        scale = Default;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var values = new List<double>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                error = $"\"{part}\" isn't a number.";
                return false;
            }
            values.Add(v);
        }
        if (values.Count < 2) error = "The scale needs at least two values.";
        else if (values.Count > MaxValues) error = $"The scale can have at most {MaxValues} values.";
        else if (values.Any(v => v <= 0)) error = "Scale values must be greater than 0.";
        else if (values.Zip(values.Skip(1)).Any(p => p.Second <= p.First)) error = "Scale values must go up from left to right.";
        if (error is not null) return false;

        scale = values;
        return true;
    }

    public static string Format(IEnumerable<double> scale) => string.Join(", ", scale.Select(RubricText.Number));

    public static bool Contains(IReadOnlyList<double> scale, double value) => scale.Any(s => Math.Abs(s - value) < 1e-9);

    /// <summary>The nearest scale value; halfway goes up.</summary>
    public static double Snap(IReadOnlyList<double> scale, double value) => scale[IndexOf(scale, value)];

    /// <summary>Moves <paramref name="steps"/> places along the scale from the nearest value, stopping at either end.</summary>
    public static double Step(IReadOnlyList<double> scale, double value, int steps) =>
        scale[Math.Clamp(IndexOf(scale, value) + steps, 0, scale.Count - 1)];

    private static int IndexOf(IReadOnlyList<double> scale, double value)
    {
        var best = 0;
        for (var i = 1; i < scale.Count; i++)
            if (Math.Abs(scale[i] - value) <= Math.Abs(scale[best] - value)) best = i;
        return best;
    }
}

/// <summary>How one criterion was answered for one item. <see cref="Option"/> is null when it wasn't.</summary>
public sealed record CriterionResult(ScoringCriterion Criterion, ScoringCriterionOption? Option, string? Input)
{
    public bool Answered => Option is not null;
}

/// <summary>A factor worked out from the rubric. <see cref="Value"/> is null when no base criterion is answered.</summary>
public sealed record FactorResult(WsjfFactor Factor, double? Value, double? Average, int Steps, IReadOnlyList<CriterionResult> Criteria)
{
    public bool Calculated => Value is not null;

    /// <summary>E.g. "Integrations 3 → 5 · Named users 116 → +1 step · = 8".</summary>
    public string Explanation
    {
        get
        {
            var parts = new List<string>();
            foreach (var r in Criteria.Where(r => r.Answered))
            {
                var input = r.Input is { Length: > 0 } i ? $" {i}" : "";
                parts.Add(r.Criterion.Role == CriterionRole.Base
                    ? $"{r.Criterion.Name}{input} → {RubricText.Number(r.Option!.Value)}"
                    : $"{r.Criterion.Name}{input} → {RubricText.Steps(r.Option!.Value)}");
            }
            var bases = Criteria.Count(r => r.Answered && r.Criterion.Role == CriterionRole.Base);
            if (bases > 1 && Average is { } avg) parts.Add($"average {RubricText.Number(Math.Round(avg, 2))}");
            if (Value is { } v) parts.Add($"= {RubricText.Number(v)}");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Works out WSJF factors from a rubric and an item's answers. A factor is the average of its answered
/// base criteria, snapped to the scale, then moved by the answered adjusters' steps (clamped to the scale).
/// </summary>
public static class RubricCalculator
{
    public static readonly IReadOnlyList<WsjfFactor> Factors = Enum.GetValues<WsjfFactor>();

    public static WsjfFactors Flag(WsjfFactor factor) => (WsjfFactors)(1 << (int)factor);

    /// <summary>Whole months from today's month to the start month; negative once it has started.</summary>
    public static int MonthsUntil(DateOnly today, DateOnly start) => (start.Year - today.Year) * 12 + start.Month - today.Month;

    public static CriterionResult Match(ScoringCriterion criterion, RubricAnswer? answer, DateOnly? startDate, DateOnly today)
    {
        switch (criterion.Kind)
        {
            case CriterionKind.Choice:
                var option = answer?.OptionId is { } id ? criterion.Options.FirstOrDefault(o => o.Id == id) : null;
                return new(criterion, option, option?.Label);
            case CriterionKind.NumberBands:
                return answer?.Number is { } n ? new(criterion, Band(criterion, n), RubricText.Number(n)) : new(criterion, null, null);
            case CriterionKind.StartDate:
                if (startDate is not { } start) return new(criterion, null, null);
                var months = MonthsUntil(today, start);
                return new(criterion, Band(criterion, months), RubricText.Start(start, months));
            default:
                return new(criterion, null, null);
        }
    }

    /// <summary>The band whose lower bound is the highest one at or below the value; below every band, the lowest band.</summary>
    public static ScoringCriterionOption? Band(ScoringCriterion criterion, double value)
    {
        var bands = criterion.Options.Where(o => o.From is not null).OrderBy(o => o.From).ToList();
        return bands.Count == 0 ? null : bands.LastOrDefault(o => o.From <= value) ?? bands[0];
    }

    public static FactorResult EvaluateFactor(ScoringRubric rubric, WsjfFactor factor,
        IReadOnlyDictionary<int, RubricAnswer> answers, DateOnly? startDate, DateOnly today)
    {
        var results = rubric.ForFactor(factor)
            .Select(c => Match(c, answers.GetValueOrDefault(c.Id), startDate, today))
            .ToList();
        var bases = results.Where(r => r.Answered && r.Criterion.Role == CriterionRole.Base).Select(r => r.Option!.Value).ToList();
        if (bases.Count == 0) return new(factor, null, null, 0, results);

        var average = bases.Average();
        var steps = (int)results.Where(r => r.Answered && r.Criterion.Role == CriterionRole.Adjuster).Sum(r => Math.Round(r.Option!.Value));
        return new(factor, RubricScale.Step(rubric.Scale, average, steps), average, steps, results);
    }

    public static IReadOnlyList<FactorResult> Evaluate(ScoringRubric rubric, IReadOnlyDictionary<int, RubricAnswer> answers,
        DateOnly? startDate, DateOnly today) =>
        Factors.Select(f => EvaluateFactor(rubric, f, answers, startDate, today)).ToList();

    /// <summary>The item's answers keyed by criterion.</summary>
    public static Dictionary<int, RubricAnswer> AnswersOf(RoadmapItem item) =>
        item.RubricAnswers.ToDictionary(a => a.CriterionId, RubricAnswer.From);

    /// <summary>Writes each calculated factor that isn't marked as entered by hand. Returns true if anything changed.</summary>
    public static bool Apply(ItemScore score, IEnumerable<FactorResult> results)
    {
        var changed = false;
        foreach (var r in results)
        {
            if (r.Value is not { } value || score.ManualFactors.HasFlag(Flag(r.Factor)) || Get(score, r.Factor) == value) continue;
            Set(score, r.Factor, value);
            changed = true;
        }
        return changed;
    }

    public static double? Get(ItemScore s, WsjfFactor factor) => factor switch
    {
        WsjfFactor.BusinessValue => s.BusinessValue,
        WsjfFactor.TimeCriticality => s.TimeCriticality,
        WsjfFactor.RiskReduction => s.RiskReduction,
        _ => s.JobSize,
    };

    public static void Set(ItemScore s, WsjfFactor factor, double? value)
    {
        switch (factor)
        {
            case WsjfFactor.BusinessValue: s.BusinessValue = value; break;
            case WsjfFactor.TimeCriticality: s.TimeCriticality = value; break;
            case WsjfFactor.RiskReduction: s.RiskReduction = value; break;
            default: s.JobSize = value; break;
        }
    }

    /// <summary>The factors that have a value. Used to mark existing and imported values as entered by hand.</summary>
    public static WsjfFactors WithValues(ItemScore s) =>
        Factors.Where(f => Get(s, f) is not null).Aggregate(WsjfFactors.None, (all, f) => all | Flag(f));
}

/// <summary>The rules a rubric must follow. Violations come back as messages for the user.</summary>
public static class RubricRules
{
    public const int MaxNameLength = 100;
    public const int MaxHelpLength = 500;
    public const int MaxOptions = 20;
    public const int MaxSteps = 6;

    public static string? Validate(ScoringCriterion c, IReadOnlyList<double> scale)
    {
        var name = c.Name?.Trim() ?? "";
        if (name.Length == 0) return "The criterion needs a name.";
        if (name.Length > MaxNameLength) return $"The criterion name can be at most {MaxNameLength} characters.";
        if (c.Help?.Length > MaxHelpLength) return $"The help text can be at most {MaxHelpLength} characters.";
        if (c.Options.Count == 0) return $"“{name}” needs at least one {(c.Kind == CriterionKind.Choice ? "choice" : "band")}.";
        if (c.Options.Count > MaxOptions) return $"“{name}” can have at most {MaxOptions} {(c.Kind == CriterionKind.Choice ? "choices" : "bands")}.";

        if (c.Kind == CriterionKind.Choice)
        {
            if (c.Options.Any(o => string.IsNullOrWhiteSpace(o.Label))) return $"Every choice in “{name}” needs a label.";
            if (c.Options.GroupBy(o => o.Label!.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } dupe)
                return $"“{name}” has the choice “{dupe.Key}” more than once.";
        }
        else
        {
            if (c.Options.Any(o => o.From is null)) return $"Every band in “{name}” needs a “from” value.";
            if (c.Options.GroupBy(o => o.From).FirstOrDefault(g => g.Count() > 1) is { } dupe)
                return $"“{name}” has two bands starting at {RubricText.Number(dupe.Key!.Value)}.";
        }

        foreach (var o in c.Options)
        {
            if (c.Role == CriterionRole.Base && !RubricScale.Contains(scale, o.Value))
                return $"“{name}”: {RubricText.Number(o.Value)} isn't on the scale ({RubricScale.Format(scale)}).";
            if (c.Role == CriterionRole.Adjuster && (o.Value != Math.Round(o.Value) || Math.Abs(o.Value) > MaxSteps))
                return $"“{name}”: adjusters move a whole number of steps, from −{MaxSteps} to +{MaxSteps}.";
        }
        return null;
    }

    /// <summary>A detached copy, for editing.</summary>
    public static ScoringCriterion Copy(ScoringCriterion c) => new()
    {
        Id = c.Id, ProductId = c.ProductId, Factor = c.Factor, Role = c.Role, Kind = c.Kind, Name = c.Name, Help = c.Help,
        SortOrder = c.SortOrder,
        Options = c.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .Select(o => new ScoringCriterionOption { Id = o.Id, CriterionId = o.CriterionId, SortOrder = o.SortOrder, Label = o.Label, From = o.From, Value = o.Value })
            .ToList(),
    };
}

/// <summary>Readable text for rubric values, shared by the UI, the change history and the explanations.</summary>
public static class RubricText
{
    public static string Factor(WsjfFactor f) => f switch
    {
        WsjfFactor.BusinessValue => "Business value",
        WsjfFactor.TimeCriticality => "Time criticality",
        WsjfFactor.RiskReduction => "Risk reduction",
        _ => "Job size",
    };

    public static string Kind(CriterionKind k) => k switch
    {
        CriterionKind.NumberBands => "Number with bands",
        CriterionKind.Choice => "Pick-list",
        _ => "From the start date",
    };

    public static string Number(double v) =>
        v == Math.Round(v) ? v.ToString("#,0", CultureInfo.InvariantCulture) : v.ToString("#,0.##", CultureInfo.InvariantCulture);

    public static string Steps(double v) => v switch
    {
        0 => "no change",
        1 => "+1 step",
        -1 => "−1 step",
        > 0 => $"+{Number(v)} steps",
        _ => $"−{Number(-v)} steps",
    };

    public static string Start(DateOnly start, int months) =>
        $"{(months < 0 ? "started" : "starts")} {start.ToString("MMM yyyy", CultureInfo.InvariantCulture)}" +
        (months > 0 ? $" (in {months} month{(months == 1 ? "" : "s")})" : months == 0 ? " (this month)" : "");

    /// <summary>An answer as the history shows it: the choice's label, or the number.</summary>
    public static string? Answer(ScoringCriterion c, RubricAnswer? a) => a is null ? null : c.Kind switch
    {
        CriterionKind.Choice => c.Options.FirstOrDefault(o => o.Id == a.OptionId)?.Label,
        CriterionKind.NumberBands => a.Number is { } n ? Number(n) : null,
        _ => null,
    };
}

/// <summary>A small, generic rubric to start from. Offered by Settings; never added automatically.</summary>
public static class StarterRubric
{
    public static List<ScoringCriterion> Create(int productId)
    {
        var sort = 0;
        ScoringCriterion Criterion(WsjfFactor factor, CriterionKind kind, string name, string help, params (string? Label, double? From, double Value)[] options) => new()
        {
            ProductId = productId, Factor = factor, Role = CriterionRole.Base, Kind = kind, Name = name, Help = help, SortOrder = sort++,
            Options = options.Select((o, i) => new ScoringCriterionOption { SortOrder = i, Label = o.Label, From = o.From, Value = o.Value }).ToList(),
        };

        return
        [
            Criterion(WsjfFactor.BusinessValue, CriterionKind.Choice, "Business value", "How much the business gains when this is done.",
                ("Low", null, 1), ("Medium", null, 3), ("High", null, 8), ("Critical", null, 13)),
            Criterion(WsjfFactor.TimeCriticality, CriterionKind.StartDate, "Start date", "The sooner the planned start, the more urgent. Updates itself as time passes.",
                ("Started or within 3 months", 0, 13), ("4–9 months away", 4, 8), ("10–15 months away", 10, 5), ("16+ months away", 16, 3)),
            Criterion(WsjfFactor.RiskReduction, CriterionKind.Choice, "Risk if we wait", "Risk reduced or opportunity enabled by doing this now.",
                ("None", null, 1), ("Some", null, 3), ("Significant", null, 8), ("Severe", null, 13)),
            Criterion(WsjfFactor.JobSize, CriterionKind.Choice, "Size", "How big the job is.",
                ("Small", null, 2), ("Medium", null, 3), ("Large", null, 5), ("Extra large", null, 8)),
        ];
    }
}
