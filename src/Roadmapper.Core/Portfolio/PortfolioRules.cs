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
}

/// <summary>Display names for portfolio enums, shared by the UI, exports and API.</summary>
public static class PortfolioText
{
    public static string Label(AppCategory c) => c switch
    {
        AppCategory.CustomerPortal => "Customer portal",
        AppCategory.IntegrationPlatform => "Integration platform",
        AppCategory.ExternalParty => "External party",
        _ => c.ToString(),
    };

    public static string Label(Criticality c) => $"Tier {(int)c}";

    public static string Label(LifecycleStatus s) => s.ToString();

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

    /// <summary>Any portfolio enum (or other value) as display text, for history and exports.</summary>
    public static string Format(object value) => value switch
    {
        AppCategory c => Label(c),
        Criticality c => Label(c),
        Hosting h => Label(h),
        SupportRole r => Label(r),
        bool b => b ? "Yes" : "No",
        DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
