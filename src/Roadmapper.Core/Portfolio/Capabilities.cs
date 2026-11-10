namespace Roadmapper.Core.Portfolio;

/// <summary>
/// A capability applications of one category are compared on, e.g. "Dock scheduling" for YMS. Applications are rated
/// on it with a <see cref="FeatureAssessment"/>.
/// </summary>
public class CapabilityFeature
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<FeatureAssessment> Assessments { get; set; } = new();
}

/// <summary>
/// An application from another category added to a category's comparison (e.g. a WMS with yard features in the YMS
/// comparison). Applications of the category itself are always included and need no row.
/// </summary>
public class ComparisonMember
{
    public AppCategory Category { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
}

/// <summary>
/// How one application does on one feature: whether it has it (<see cref="Availability"/>, for the comparison matrix)
/// and how strong it is (<see cref="Strength"/>, for the heat map). Either may be unknown.
/// </summary>
public class FeatureAssessment
{
    public int FeatureId { get; set; }
    public CapabilityFeature? Feature { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    public FeatureAvailability? Availability { get; set; }
    public FeatureStrength? Strength { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class FeatureDraft
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public bool IsNew => Id == 0;

    public static FeatureDraft From(CapabilityFeature f) => new() { Id = f.Id, Category = f.Category, Name = f.Name, Description = f.Description };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Give the feature a name.";
        if (Name.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (Description?.Trim().Length > 1000) return "Description can be at most 1000 characters.";
        return null;
    }

    public void ApplyTo(CapabilityFeature f)
    {
        f.Category = Category;
        f.Name = Name.Trim();
        f.Description = DraftText.Clean(Description);
    }
}

/// <summary>Rules for feature ratings.</summary>
public static class CapabilityRules
{
    public const int MaxNoteLength = 1000;

    /// <summary>
    /// The rating as it will be stored: No means no strength, so a missing strength becomes None and any other is
    /// refused. Returns an error message, or null when it's fine.
    /// </summary>
    public static string? Normalize(ref FeatureAvailability? availability, ref FeatureStrength? strength, ref string? note)
    {
        note = DraftText.Clean(note);
        if (note?.Length > MaxNoteLength) return $"A note can be at most {MaxNoteLength} characters.";
        if (availability == FeatureAvailability.No)
        {
            if (strength is { } s && s != FeatureStrength.None) return "An application without the feature can't be rated stronger than None.";
            strength = FeatureStrength.None;
        }
        return null;
    }

    /// <summary>True when there's nothing to keep: no availability, no strength, no note.</summary>
    public static bool IsEmpty(FeatureAvailability? availability, FeatureStrength? strength, string? note) =>
        availability is null && strength is null && string.IsNullOrWhiteSpace(note);
}

/// <summary>One application's column in a comparison. <see cref="Added"/>: it's from another category.</summary>
public sealed record MatrixApp(Application Application, bool Added)
{
    public int Id => Application.Id;
    public string Name => Application.Name;
}

/// <summary>Coverage of one application across the features.</summary>
public sealed record AppCoverage(int Yes, int Partial, int No, int Unassessed)
{
    public int Assessed => Yes + Partial + No;
    public int Total => Assessed + Unassessed;
    public int AssessedPct => Total == 0 ? 0 : (int)Math.Round(100.0 * Assessed / Total, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Coverage of one feature across the applications. <see cref="IsGap"/>: no application has it fully.
/// <see cref="IsOverlap"/>: two or more have it fully. <see cref="Strongest"/>: the applications with the highest
/// strength above None.
/// </summary>
public sealed record FeatureCoverage(int Yes, int Partial, int No, int Unassessed, IReadOnlyList<int> Strongest)
{
    public bool IsGap => Yes == 0;
    public bool IsOverlap => Yes >= 2;
}

/// <summary>
/// A category's comparison: features (rows, in order) × applications (columns: the category's own, then those added,
/// each by name), with each cell's rating and coverage both ways.
/// </summary>
public sealed class CapabilityMatrix
{
    public AppCategory Category { get; init; }
    public IReadOnlyList<CapabilityFeature> Features { get; init; } = [];
    public IReadOnlyList<MatrixApp> Apps { get; init; } = [];
    public IReadOnlyDictionary<int, AppCoverage> ByApp { get; init; } = new Dictionary<int, AppCoverage>();
    public IReadOnlyDictionary<int, FeatureCoverage> ByFeature { get; init; } = new Dictionary<int, FeatureCoverage>();
    private IReadOnlyDictionary<(int Feature, int App), FeatureAssessment> Cells { get; init; } = new Dictionary<(int, int), FeatureAssessment>();

    public FeatureAssessment? Cell(int featureId, int appId) => Cells.GetValueOrDefault((featureId, appId));

    /// <summary>The site profile the comparison is costed for.</summary>
    public SiteProfile Site { get; init; } = new();
    /// <summary>What each application would cost <see cref="Site"/> (<see cref="CostEstimate.NotPriced"/> when it has no pricing).</summary>
    public IReadOnlyDictionary<int, CostEstimate> Costs { get; init; } = new Dictionary<int, CostEstimate>();

    public CostEstimate Cost(int appId) => Costs.GetValueOrDefault(appId) ?? CostEstimate.NotPriced;
    public bool AnyPriced => Costs.Values.Any(c => c.IsPriced);

    /// <summary>The applications with the lowest monthly cost, among those fully costed (empty when none is).</summary>
    public IReadOnlyList<int> Cheapest
    {
        get
        {
            var complete = Apps.Where(a => Cost(a.Id).IsComplete).ToList();
            if (complete.Count == 0) return [];
            var min = complete.Min(a => Cost(a.Id).Monthly);
            return complete.Where(a => Cost(a.Id).Monthly == min).Select(a => a.Id).ToList();
        }
    }

    /// <summary>How many applications are in the comparison before narrowing it to <see cref="Apps"/>.</summary>
    public int AllApps { get; init; }
    /// <summary>True when the comparison is narrowed to some of its applications.</summary>
    public bool Narrowed => Apps.Count < AllApps;

    public int Gaps => Features.Count(f => ByFeature[f.Id].IsGap);

    /// <summary>Application ids from a comma-separated list such as "3,7,12" (null when there are none).</summary>
    public static IReadOnlyList<int>? ParseIds(string? ids)
    {
        var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();
        return list.Count == 0 ? null : list;
    }
    public int Overlaps => Features.Count(f => ByFeature[f.Id].IsOverlap);

    /// <summary>
    /// Builds the comparison. <paramref name="apps"/> may be every application: those of <paramref name="category"/>
    /// and those in <paramref name="addedIds"/> are kept. Retired applications are left out unless
    /// <paramref name="includeRetired"/>. <paramref name="pricing"/> is costed for <paramref name="site"/> (one site
    /// with unknown sizes when not given). <paramref name="only"/> narrows the comparison to those applications (ignored
    /// when none of them is in it); gaps, overlaps, strongest and cheapest are then worked out among them alone.
    /// </summary>
    public static CapabilityMatrix Build(AppCategory category, IEnumerable<CapabilityFeature> features, IEnumerable<Application> apps,
        IEnumerable<int> addedIds, IEnumerable<FeatureAssessment> assessments, bool includeRetired = false,
        IEnumerable<PricingLine>? pricing = null, SiteProfile? site = null, IEnumerable<int>? only = null)
    {
        site ??= SiteProfile.OneSite(category);
        var lines = (pricing ?? []).Where(l => l.Category == category).ToLookup(l => l.ApplicationId);
        var added = addedIds.ToHashSet();
        var columns = apps
            .Where(a => a.Category == category || added.Contains(a.Id))
            .Where(a => includeRetired || a.Status != LifecycleStatus.Retired)
            .DistinctBy(a => a.Id)
            .Select(a => new MatrixApp(a, a.Category != category))
            .OrderBy(m => m.Added).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Id)
            .ToList();
        var all = columns.Count;
        if (only?.ToHashSet() is { Count: > 0 } keep && columns.Any(c => keep.Contains(c.Id)))
            columns = columns.Where(c => keep.Contains(c.Id)).ToList();
        var rows = features.Where(f => f.Category == category).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList();
        var appIds = columns.Select(c => c.Id).ToHashSet();
        var featureIds = rows.Select(f => f.Id).ToHashSet();
        var cells = assessments.Where(a => appIds.Contains(a.ApplicationId) && featureIds.Contains(a.FeatureId))
            .GroupBy(a => (a.FeatureId, a.ApplicationId)).ToDictionary(g => g.Key, g => g.First());

        AppCoverage CoverApp(int app)
        {
            var ratings = rows.Select(f => cells.GetValueOrDefault((f.Id, app))?.Availability).ToList();
            return new(ratings.Count(r => r == FeatureAvailability.Yes), ratings.Count(r => r == FeatureAvailability.Partial),
                ratings.Count(r => r == FeatureAvailability.No), ratings.Count(r => r is null));
        }
        FeatureCoverage CoverFeature(int feature)
        {
            var row = columns.Select(c => (c.Id, Cell: cells.GetValueOrDefault((feature, c.Id)))).ToList();
            var top = row.Select(r => r.Cell?.Strength).Where(s => s is > FeatureStrength.None).DefaultIfEmpty(null).Max();
            return new(row.Count(r => r.Cell?.Availability == FeatureAvailability.Yes), row.Count(r => r.Cell?.Availability == FeatureAvailability.Partial),
                row.Count(r => r.Cell?.Availability == FeatureAvailability.No), row.Count(r => r.Cell?.Availability is null),
                top is null ? [] : row.Where(r => r.Cell?.Strength == top).Select(r => r.Id).ToList());
        }

        return new CapabilityMatrix
        {
            Category = category,
            Features = rows,
            Apps = columns,
            AllApps = all,
            Cells = cells,
            ByApp = columns.ToDictionary(c => c.Id, c => CoverApp(c.Id)),
            ByFeature = rows.ToDictionary(f => f.Id, f => CoverFeature(f.Id)),
            Site = site,
            Costs = columns.ToDictionary(c => c.Id, c => lines[c.Id].Any() ? CostEstimate.For(lines[c.Id], site) : CostEstimate.NotPriced),
        };
    }
}
