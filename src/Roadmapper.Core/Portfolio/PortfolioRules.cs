namespace Roadmapper.Core.Portfolio;

/// <summary>Derived portfolio values. Pure functions, so pages, exports and the API all agree.</summary>
public static class PortfolioRules
{
    public const int MaxNameLength = 200;
    public const int MaxNotesLength = 4000;
    /// <summary>Fit scores at or above this count as "high" for the TIME model.</summary>
    public const int HighFit = 3;

    public static DateOnly? NoticeDeadline(DateOnly? renewal, int? noticeDays) =>
        renewal is { } r ? r.AddDays(-(noticeDays ?? 0)) : null;

    /// <summary>Invest = good business and technical fit; Migrate = valued but technically weak; Tolerate = sound but low value; Eliminate = neither.</summary>
    public static TimeQuadrant? Quadrant(int? businessFit, int? technicalFit) => (businessFit, technicalFit) switch
    {
        ({ } b, { } t) when b >= HighFit && t >= HighFit => TimeQuadrant.Invest,
        ({ } b, { }) when b >= HighFit => TimeQuadrant.Migrate,
        ({ }, { } t) when t >= HighFit => TimeQuadrant.Tolerate,
        ({ }, { }) => TimeQuadrant.Eliminate,
        _ => null,
    };

    /// <summary>The earliest start of a vendor's contracts.</summary>
    public static DateOnly? ContractStart(IEnumerable<Contract> contracts) => contracts.Min(c => c.StartDate);

    /// <summary>The latest end of a vendor's contracts.</summary>
    public static DateOnly? ContractEnd(IEnumerable<Contract> contracts) => contracts.Max(c => c.EndDate);

    /// <summary>The soonest contract that hasn't renewed yet as of <paramref name="today"/>.</summary>
    public static Contract? NextRenewal(IEnumerable<Contract> contracts, DateOnly today) =>
        contracts.Where(c => c.RenewalDate >= today).OrderBy(c => c.RenewalDate).FirstOrDefault();

    /// <summary>True when the notice deadline is today or later but within <paramref name="warningDays"/>.</summary>
    public static bool NoticeDue(Contract c, DateOnly today, int warningDays) =>
        c.NoticeDeadline is { } d && d >= today && d <= today.AddDays(warningDays);

    /// <summary>The notice deadline has passed but the renewal date hasn't: the contract will renew as-is.</summary>
    public static bool NoticeMissed(Contract c, DateOnly today) =>
        c.NoticeDeadline is { } d && d < today && c.RenewalDate >= today;

    // ---- Applications ----

    /// <summary>
    /// Whether an application counts in portfolio totals, health and the integration map: not retired, and not a
    /// candidate still being evaluated.
    /// </summary>
    public static bool CountsInPortfolio(Application a) => a.Status is not (LifecycleStatus.Retired or LifecycleStatus.Evaluating);

    // ---- Integrations ----

    /// <summary>Risk scores (likelihood × impact) at or above this make an integration high risk.</summary>
    public const int HighRiskScore = 15;
    /// <summary>On a Tier 1 integration, a risk this high is already enough for high risk.</summary>
    public const int HighRiskScoreTier1 = 8;
    /// <summary>Risk scores at or above this make an integration at least medium risk.</summary>
    public const int MediumRiskScore = 6;

    /// <summary>The highest score among risks that are still open or accepted; 0 when there are none.</summary>
    public static int TopRiskScore(Integration i) => i.Risks.Where(r => r.IsActive).Select(r => r.Score).DefaultIfEmpty(0).Max();

    /// <summary>A live integration that has never been reviewed, or whose review interval has passed.</summary>
    public static bool ReviewOverdue(Integration i, DateOnly today) =>
        i.Status is LifecycleStatus.Active or LifecycleStatus.Retiring && (i.NextReviewDate is not { } next || next < today);

    /// <summary>
    /// High: an open or accepted risk scores 15+, or 8+ on a Tier 1 integration.
    /// Medium: a risk scores 6+, or the review is overdue. Low: otherwise. Retired integrations are always low.
    /// </summary>
    public static RiskLevel RiskLevel(Integration i, DateOnly today)
    {
        if (i.Status == LifecycleStatus.Retired) return Portfolio.RiskLevel.Low;
        var top = TopRiskScore(i);
        if (top >= HighRiskScore || (i.Criticality == Criticality.Tier1 && top >= HighRiskScoreTier1)) return Portfolio.RiskLevel.High;
        if (top >= MediumRiskScore || ReviewOverdue(i, today)) return Portfolio.RiskLevel.Medium;
        return Portfolio.RiskLevel.Low;
    }

    /// <summary>Severity band of a single risk score, for the colored score chip: 1–5 low, 6–14 medium, 15–25 high.</summary>
    public static RiskLevel ScoreLevel(int score) =>
        score >= HighRiskScore ? Portfolio.RiskLevel.High : score >= MediumRiskScore ? Portfolio.RiskLevel.Medium : Portfolio.RiskLevel.Low;

    // ---- Business processes ----

    /// <summary>
    /// The error to show when <paramref name="processId"/> can't sit under <paramref name="newParentId"/>: itself, or
    /// one of its own sub-processes (that would make a loop). Null when the move is fine. <paramref name="parentOf"/>
    /// maps each process to its parent.
    /// </summary>
    public static string? ParentError(int processId, int? newParentId, IReadOnlyDictionary<int, int?> parentOf)
    {
        if (newParentId is not { } parent) return null;
        if (parent == processId) return "A process can't be its own parent.";
        var seen = new HashSet<int>();
        for (int? at = parent; at is { } id && seen.Add(id); at = parentOf.GetValueOrDefault(id))
            if (id == processId) return "A process can't sit under one of its own sub-processes.";
        return null;
    }

    /// <summary>The process and everything above it, nearest first.</summary>
    public static IEnumerable<int> SelfAndAncestors(int processId, IReadOnlyDictionary<int, int?> parentOf)
    {
        var seen = new HashSet<int>();
        for (int? at = processId; at is { } id && seen.Add(id); at = parentOf.GetValueOrDefault(id)) yield return id;
    }

    /// <summary>
    /// How far a KPI has moved from baseline toward target, in percent: 0 at baseline, 100 at target, and it can go
    /// past either end. Null when baseline, target or current is missing, or baseline equals target.
    /// </summary>
    public static int? KpiProgress(ProcessKpi k)
    {
        if (k is not { Baseline: { } b, Target: { } t, Current: { } c } || b == t) return null;
        return (int)Math.Round((c - b) / (t - b) * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>Current is at or beyond target in the KPI's direction. Null when either is missing.</summary>
    public static bool? KpiOnTarget(ProcessKpi k) => (k.Target, k.Current) switch
    {
        ({ } t, { } c) => k.Direction == KpiDirection.HigherIsBetter ? c >= t : c <= t,
        _ => null,
    };
}

/// <summary>Display names for portfolio enums, shared by the UI, exports and API.</summary>
public static class PortfolioText
{
    public static string Label(AppCategory c) => c switch
    {
        AppCategory.CustomerPortal => "Customer portal",
        AppCategory.IntegrationPlatform => "Integration platform",
        _ => c.ToString(),
    };

    public static string Label(Criticality c) => $"Tier {(int)c}";

    public static string Label(LifecycleStatus s) => s.ToString();

    public static string Label(FeatureAvailability a) => a.ToString();

    public static string Label(FeatureStrength s) => s.ToString();

    public static string Label(Hosting h) => h switch { Hosting.OnPrem => "On-premises", _ => h.ToString() };

    public static string Label(SupportRole r) => r switch
    {
        SupportRole.L1Support => "L1 support",
        SupportRole.L2Support => "L2 support",
        SupportRole.VendorSupport => "Vendor support",
        SupportRole.SME => "Subject-matter expert",
        SupportRole.KeyUser => "Key user",
        _ => "Other",
    };

    public static string Label(InitiativeImpact i) => i.ToString();

    public static string Label(TimeQuadrant q) => q.ToString();

    public static string Describe(TimeQuadrant q) => q switch
    {
        TimeQuadrant.Invest => "High business and technical fit: keep investing.",
        TimeQuadrant.Migrate => "Valuable but technically weak: plan a move to a better platform.",
        TimeQuadrant.Tolerate => "Technically sound but low business value: keep running, don't invest.",
        _ => "Low business and technical fit: candidate to retire.",
    };

    public static string Label(IntegrationType t) => t switch { IntegrationType.Batch => "Batch file", _ => t.ToString() };

    public static string Label(FlowDirection d) => d switch { FlowDirection.OneWay => "One-way", _ => "Bidirectional" };

    public static string Label(IntegrationFrequency f) => f switch
    {
        IntegrationFrequency.RealTime => "Real-time",
        IntegrationFrequency.NearRealTime => "Near real-time",
        IntegrationFrequency.OnDemand => "On demand",
        _ => f.ToString(),
    };

    public static string Label(RiskStatus s) => s.ToString();

    public static string Label(PartnerType t) => t.ToString();

    public static string Label(PartnerStatus s) => s.ToString();

    public static string Label(PartnerImportance i) => i == PartnerImportance.Key ? "Key partner" : "Standard";

    public static string Label(RiskLevel l) => $"{l} risk";

    public static string Label(ProcessDomain d) => d == ProcessDomain.CustomerService ? "Customer service" : d.ToString();

    public static string Label(PainSeverity s) => s.ToString();

    public static string Label(PainPointStatus s) => s == PainPointStatus.WontFix ? "Won't fix" : s.ToString();

    public static string Label(KpiDirection d) => d == KpiDirection.HigherIsBetter ? "Higher is better" : "Lower is better";

    public static string Label(ImpactSeverity s) => $"{s} impact";

    public static string Label(HealthRating h) => h.ToString();

    public static string Label(DecisionStatus s) => s.ToString();

    public static string Label(DecisionTargetKind k) => k switch
    {
        DecisionTargetKind.RoadmapItem => "Roadmap item",
        DecisionTargetKind.TradingPartner => "Trading partner",
        _ => k.ToString(),
    };

    public static string Label(DocumentCategory c) => c switch
    {
        DocumentCategory.Sow => "SOW",
        DocumentCategory.Brd => "BRD",
        _ => c.ToString(),
    };

    public static string Label(DocumentTargetKind k) => k switch
    {
        DocumentTargetKind.RoadmapItem => "Roadmap item",
        _ => k.ToString(),
    };

    public static string Label(BenefitCategory c) => c switch
    {
        BenefitCategory.CostSavings => "Cost savings",
        BenefitCategory.LaborSavings => "Labor savings",
        BenefitCategory.RevenueImpact => "Revenue impact",
        BenefitCategory.RiskReduction => "Risk reduction",
        _ => c.ToString(),
    };

    public static string Label(BenefitUnit u) => u switch { BenefitUnit.USD => "US dollars", _ => u.ToString() };

    public static string Label(BenefitStatus s) => s switch
    {
        BenefitStatus.NotMeasured => "Not measured",
        BenefitStatus.AtRisk => "At risk",
        _ => s.ToString(),
    };

    public static string Label(ProcessImpactKind k) => k switch
    {
        ProcessImpactKind.UsesApplication => "Uses it",
        ProcessImpactKind.DependsOnIntegration => "Relies on an affected integration",
        _ => "A sub-process is affected",
    };

    /// <summary>Any portfolio enum (or other value) as display text, for history and exports.</summary>
    public static string Format(object value) => value switch
    {
        AppCategory c => Label(c),
        Criticality c => Label(c),
        Hosting h => Label(h),
        SupportRole r => Label(r),
        IntegrationType t => Label(t),
        FlowDirection d => Label(d),
        IntegrationFrequency f => Label(f),
        PartnerImportance i => Label(i),
        ProcessDomain d => Label(d),
        PainPointStatus s => Label(s),
        KpiDirection d => Label(d),
        BenefitCategory c => Label(c),
        DecisionStatus s => Label(s),
        BenefitUnit u => Label(u),
        DocumentCategory c => Label(c),
        FeatureAvailability a => Label(a),
        FeatureStrength f => Label(f),
        bool b => b ? "Yes" : "No",
        DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
