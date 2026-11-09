namespace Roadmapper.Core.Portfolio;

/// <summary>Benefit totals in dollars per year. <see cref="UnvaluedHours"/> counts labor-hour benefits left out because no labor rate is set.</summary>
public sealed record BenefitTotals(decimal ExpectedUsd, decimal RealizedUsd, int Count, int Realized, int AtRisk, int NotMeasured, int UnvaluedHours)
{
    /// <summary>Realized as a share of expected, 0–100+; null when nothing has a dollar value.</summary>
    public int? RealizationPct => ExpectedUsd > 0 ? (int)Math.Round(RealizedUsd / ExpectedUsd * 100m, MidpointRounding.AwayFromZero) : null;
}

/// <summary>
/// How much of a benefit has been realized. Pure functions, so pages, exports and the API agree.
/// <list type="bullet">
/// <item><b>Actual</b> is the latest measurement: the annual value (or level) the benefit is running at. With no
/// measurements, a linked process KPI's current value stands in.</item>
/// <item><b>Gain</b> is how far actual has moved from the baseline toward the expected value. With no baseline
/// (savings counted from zero) it's the actual itself. A baseline above the expected value means lower is better
/// (error rate, dock-to-stock hours), and a drop counts as gain.</item>
/// <item><b>Realization %</b> = gain ÷ expected gain.</item>
/// <item><b>Dollars</b>: USD as is; Hours × the portfolio labor rate; Percent and Count have no dollar value.</item>
/// <item><b>At risk</b>: the expected-by date has passed and less than <see cref="AtRiskBelowPct"/>% is realized.</item>
/// </list>
/// </summary>
public static class BenefitMath
{
    public const int AtRiskBelowPct = 50;

    public static BenefitMeasurement? Latest(Benefit b) =>
        b.Measurements.OrderByDescending(m => m.MeasuredOn).ThenByDescending(m => m.Id).FirstOrDefault();

    /// <summary>The latest measurement, or the linked KPI's current value when there are none.</summary>
    public static decimal? Actual(Benefit b) => Latest(b)?.Value ?? b.Kpi?.Current;

    /// <summary>When <see cref="Actual"/> was measured.</summary>
    public static DateOnly? ActualOn(Benefit b) => Latest(b)?.MeasuredOn ?? b.Kpi?.MeasuredOn;

    /// <summary>The benefit's own baseline, or its KPI's when it has none.</summary>
    public static decimal? Baseline(Benefit b) => b.Baseline ?? b.Kpi?.Baseline;

    /// <summary>The improvement the benefit should bring: expected less baseline, always positive.</summary>
    public static decimal? ExpectedGain(Benefit b) => (b.ExpectedValue, Baseline(b)) switch
    {
        ({ } e, null) => e,
        ({ } e, { } base0) => Math.Abs(e - base0),
        _ => null,
    };

    /// <summary>The improvement so far, in the direction of the expected value (negative when it went the wrong way).</summary>
    public static decimal? Gain(Benefit b)
    {
        if (Actual(b) is not { } actual) return null;
        if (Baseline(b) is not { } base0) return actual;
        var expected = b.ExpectedValue ?? base0;
        return expected >= base0 ? actual - base0 : base0 - actual;
    }

    /// <summary>Gain ÷ expected gain in percent; null when either is unknown or nothing is expected.</summary>
    public static int? RealizationPct(Benefit b) =>
        (Gain(b), ExpectedGain(b)) is ({ } g, { } e) && e != 0
            ? (int)Math.Round(g / e * 100m, MidpointRounding.AwayFromZero)
            : null;

    public static BenefitStatus Status(Benefit b, DateOnly today)
    {
        var due = b.ExpectedBy is { } by && by < today;
        if (Actual(b) is null) return due ? BenefitStatus.AtRisk : BenefitStatus.NotMeasured;
        var pct = RealizationPct(b);
        if (pct >= 100) return BenefitStatus.Realized;
        if (due && (pct ?? 0) < AtRiskBelowPct) return BenefitStatus.AtRisk;
        return BenefitStatus.Realizing;
    }

    /// <summary>A value in dollars: USD as is, hours at the labor rate; null for percentages and counts, or hours with no rate.</summary>
    public static decimal? Dollars(decimal? value, BenefitUnit unit, decimal? laborRate) => (value, unit) switch
    {
        ({ } v, BenefitUnit.USD) => v,
        ({ } v, BenefitUnit.Hours) when laborRate is { } rate => v * rate,
        _ => null,
    };

    public static decimal? ExpectedUsd(Benefit b, decimal? laborRate) => Dollars(ExpectedGain(b), b.Unit, laborRate);

    /// <summary>Dollars realized so far; never negative, so a setback doesn't cancel other benefits in totals.</summary>
    public static decimal? RealizedUsd(Benefit b, decimal? laborRate) =>
        Dollars(Gain(b) is { } g ? Math.Max(0, g) : null, b.Unit, laborRate) ?? (ExpectedUsd(b, laborRate) is null ? null : 0);

    public static BenefitTotals Totals(IEnumerable<Benefit> benefits, decimal? laborRate, DateOnly today)
    {
        decimal expected = 0, realized = 0;
        int count = 0, done = 0, risk = 0, unmeasured = 0, unvalued = 0;
        foreach (var b in benefits)
        {
            count++;
            switch (Status(b, today))
            {
                case BenefitStatus.Realized: done++; break;
                case BenefitStatus.AtRisk: risk++; break;
                case BenefitStatus.NotMeasured: unmeasured++; break;
            }
            if (b.Unit == BenefitUnit.Hours && laborRate is null) { unvalued++; continue; }
            expected += ExpectedUsd(b, laborRate) ?? 0;
            realized += RealizedUsd(b, laborRate) ?? 0;
        }
        return new BenefitTotals(expected, realized, count, done, risk, unmeasured, unvalued);
    }
}
