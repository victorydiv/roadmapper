namespace Roadmapper.Core.Portfolio;

/// <summary>One reason an application lost health points.</summary>
public sealed record HealthFactor(string Reason, int Points);

/// <summary>An application's health: 0–100, its rating, and what took points off.</summary>
public sealed record AppHealth(int Score, HealthRating Rating, IReadOnlyList<HealthFactor> Factors)
{
    public string Summary => Factors.Count == 0 ? "No issues" : string.Join("; ", Factors.Select(f => $"{f.Reason} (−{f.Points})"));
}

/// <summary>
/// Application health, from 100 down. Each problem takes points off; Tier 1 applications lose 50 % more and Tier 3
/// ones 25 % fewer, because the same problem matters more on a system operations can't run without.
/// <list type="bullet">
/// <item>Integration risk: the worst live integration it's part of is high risk −25, medium risk −10.</item>
/// <item>Overdue integration reviews: −5 each, at most −15.</item>
/// <item>Contract: notice deadline missed (it will renew as is) −20; due within the renewal warning window −10.</item>
/// <item>Retiring but still in live integrations −15.</item>
/// <item>TIME: Eliminate −20, Migrate −15, Tolerate −5.</item>
/// </list>
/// Green is 80 and up, Amber 60–79, Red below 60. Retired applications aren't rated.
/// </summary>
public static class PortfolioHealth
{
    public const int GreenFrom = 80, AmberFrom = 60;

    /// <param name="integrations">Integrations the application is the source, target or middle hop of; retired ones are ignored.</param>
    public static AppHealth? Assess(Application app, IEnumerable<Integration> integrations, DateOnly today, int renewalWarningDays)
    {
        if (app.Status == LifecycleStatus.Retired) return null;
        var live = integrations.Where(i => i.Status != LifecycleStatus.Retired).ToList();
        var factors = new List<(string Reason, int Points)>();

        // Overdue reviews are counted on their own below, so medium risk here means a risk scoring 6+.
        var high = live.Where(i => PortfolioRules.RiskLevel(i, today) == RiskLevel.High).OrderBy(i => i.Name).FirstOrDefault();
        var medium = live.Where(i => PortfolioRules.TopRiskScore(i) >= PortfolioRules.MediumRiskScore)
            .OrderByDescending(PortfolioRules.TopRiskScore).ThenBy(i => i.Name).FirstOrDefault();
        if (high is not null) factors.Add(($"High-risk integration: {high.Name}", 25));
        else if (medium is not null) factors.Add(($"Medium-risk integration: {medium.Name}", 10));

        var overdue = live.Count(i => PortfolioRules.ReviewOverdue(i, today));
        if (overdue > 0) factors.Add(($"{overdue} integration review{(overdue == 1 ? "" : "s")} overdue", Math.Min(15, 5 * overdue)));

        if (app.Contract is { } c)
        {
            if (PortfolioRules.NoticeMissed(c, today)) factors.Add(($"Notice deadline for {c.Name} has passed", 20));
            else if (PortfolioRules.NoticeDue(c, today, renewalWarningDays)) factors.Add(($"Notice deadline for {c.Name} is coming up", 10));
        }

        if (app.Status == LifecycleStatus.Retiring && live.Count > 0)
            factors.Add(($"Retiring but still in {live.Count} live integration{(live.Count == 1 ? "" : "s")}", 15));

        switch (app.TimeQuadrant)
        {
            case TimeQuadrant.Eliminate: factors.Add(("TIME: Eliminate (low business and technical fit)", 20)); break;
            case TimeQuadrant.Migrate: factors.Add(("TIME: Migrate (technically weak)", 15)); break;
            case TimeQuadrant.Tolerate: factors.Add(("TIME: Tolerate (low business value)", 5)); break;
        }

        var weight = app.Criticality switch { Criticality.Tier1 => 1.5m, Criticality.Tier3 => 0.75m, _ => 1m };
        var weighted = factors.Select(f => new HealthFactor(f.Reason, (int)Math.Round(f.Points * weight, MidpointRounding.AwayFromZero))).ToList();
        var score = Math.Max(0, 100 - weighted.Sum(f => f.Points));
        return new AppHealth(score, Rate(score), weighted);
    }

    public static HealthRating Rate(int score) => score >= GreenFrom ? HealthRating.Green : score >= AmberFrom ? HealthRating.Amber : HealthRating.Red;

    /// <summary>The integrations an application takes part in, from a list of all of them.</summary>
    public static IEnumerable<Integration> IntegrationsOf(int applicationId, IEnumerable<Integration> all) =>
        all.Where(i => i.SourceApplicationId == applicationId || i.TargetApplicationId == applicationId || i.ViaApplicationId == applicationId);
}
