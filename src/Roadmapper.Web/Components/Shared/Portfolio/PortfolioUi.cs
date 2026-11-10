using System.Globalization;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Web.Components.Shared.Portfolio;

/// <summary>Display helpers for the portfolio pages.</summary>
public static class PortfolioUi
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>"$480,000", or "–" when unknown.</summary>
    public static string Money(decimal? value) => value is { } v ? v.ToString(v == decimal.Truncate(v) ? "C0" : "C2", Us) : "–";

    /// <summary>"$2.4M" / "$480K" for tiles.</summary>
    public static string MoneyShort(decimal value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("$0.#M", Us),
        >= 10_000 => (value / 1_000).ToString("$0K", Us),
        _ => value.ToString("C0", Us),
    };

    public static string Date(DateOnly? d) => d is { } x ? x.ToString("MMM d, yyyy", Us) : "–";

    /// <summary>"in 45 days", "today", "12 days ago".</summary>
    public static string Relative(DateOnly d, DateOnly today) => (d.DayNumber - today.DayNumber) switch
    {
        0 => "today",
        1 => "tomorrow",
        -1 => "yesterday",
        > 0 and var n => $"in {n} days",
        var n => $"{-n} days ago",
    };

    /// <summary>CSS class for a contract's notice deadline: missed, due soon, or nothing.</summary>
    public static string NoticeClass(Contract? c, DateOnly today, int warningDays) =>
        c is null ? "" : PortfolioRules.NoticeMissed(c, today) ? "overdue" : PortfolioRules.NoticeDue(c, today, warningDays) ? "due-soon" : "";

    /// <summary>A KPI value with its unit: "96.5%", "18 hrs", "$1,200"; "–" when unknown.</summary>
    public static string Number(decimal? value, string? unit)
    {
        if (value is not { } v) return "–";
        var n = v.ToString("#,0.##", Us);
        return unit switch
        {
            null or "" => n,
            "%" => n + "%",
            "USD" or "$" => "$" + n,
            _ => $"{n} {unit}",
        };
    }

        /// <summary>A benefit amount in its unit: "$60,000", "1,040 hrs", "34%", "120"; "–" when unknown.</summary>
    public static string Amount(decimal? value, BenefitUnit unit) => value is not { } v ? "–" : unit switch
    {
        BenefitUnit.USD => Money(v),
        BenefitUnit.Hours => $"{v.ToString("#,0.##", Us)} hrs",
        BenefitUnit.Percent => $"{v.ToString("#,0.##", Us)}%",
        _ => v.ToString("#,0.##", Us),
    };

        public static string Fit(int? score) => score is { } s ? $"{s}/5" : "–";

    /// <summary>The detail page of an application or trading partner.</summary>
    public static string Href(NodeRef node) => node.IsPartner ? $"/portfolio/partners/{node.Id}" : $"/portfolio/applications/{node.Id}";

    /// <summary>"1.2 MB", "340 KB"; empty when unknown.</summary>
    public static string FileSize(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };

    /// <summary>Where a document opens: SharePoint when its address is known, else the copy on this PC.</summary>
    public static string DocumentHref(PortfolioDocument d) => d.Url ?? $"/documents/{d.Id}/file";

    /// <summary>The page of the record a document is attached to.</summary>
    public static string? TargetHref(PortfolioDocument d) => d.Target is not { } t ? null : t.Kind switch
    {
        DocumentTargetKind.Application => $"/portfolio/applications/{t.Id}",
        DocumentTargetKind.Vendor => $"/portfolio/vendors/{t.Id}",
        DocumentTargetKind.Contract => d.Contract is { } c ? $"/portfolio/vendors/{c.VendorId}" : null,
        _ => d.RoadmapItem is { } i ? $"/products/{i.ProductId}" : null,
    };

    /// <summary>✓ Yes, ◐ Partial, ✗ No; empty when not rated.</summary>
    public static string Symbol(FeatureAvailability? a) => a switch
    {
        FeatureAvailability.Yes => "✓",
        FeatureAvailability.Partial => "◐",
        FeatureAvailability.No => "✗",
        _ => "",
    };

    /// <summary>A category's page, e.g. "/capabilities/YMS".</summary>
    public static string CapabilitiesHref(AppCategory c, string? view = null) => $"/capabilities/{c}" + (view is null ? "" : $"?view={view}");

    /// <summary>A price as quoted: "$1,200 per site / month", "$90,000 one-time", "$0.15 per transaction / month (min $500)".</summary>
    public static string Price(PricingLine l)
    {
        var per = l.Basis switch
        {
            PricingBasis.Flat => "",
            PricingBasis.Other => $" per {l.Unit}",
            _ => $" per {CostRules.UnitName(l.Basis)}",
        };
        var when = l.Frequency switch { PriceFrequency.Monthly => " / month", PriceFrequency.Annual => " / year", _ => " one-time" };
        var qty = l.Quantity is { } q ? $" × {q.ToString("0.##", Us)}" : "";
        var min = l.Minimum is { } m ? $" (min {Money(m)})" : "";
        return $"{Money(l.UnitPrice)}{per}{when}{qty}{min}";
    }

    /// <summary>A site profile's sizes: "15 users · 10 dock doors"; "sizes not set" when none is.</summary>
    public static string Sizes(SiteProfile s)
    {
        var parts = new[]
        {
            s.Users is { } u ? $"{u:N0} users" : null,
            s.Licenses is { } l ? $"{l:N0} licenses" : null,
            s.DockDoors is { } d ? $"{d:N0} dock doors" : null,
            s.Assets is { } a ? $"{a:N0} trailers / assets" : null,
            s.TransactionsPerMonth is { } t ? $"{t:N0} transactions / month" : null,
        }.Where(p => p is not null).ToList();
        return parts.Count == 0 ? "sizes not set" : string.Join(" · ", parts);
    }

    /// <summary>A cost's monthly figure for a cell: "$1,200", "≥ $1,200" when incomplete, "–" when not priced.</summary>
    public static string MonthlyText(CostEstimate c) => !c.IsPriced ? "–" : (c.IsComplete ? "" : "≥ ") + Money(Math.Round(c.Monthly));

    /// <summary>Hover text explaining a cost.</summary>
    public static string CostTip(string app, CostEstimate c, SiteProfile site) => !c.IsPriced
        ? $"{app}: not priced yet. Add its pricing on the Cost tab."
        : c.IsComplete
            ? $"{app} for {site.Name}: {Money(c.Monthly)} a month, {Money(c.OneTime)} one-time, {Money(c.FirstYear)} in the first year."
            : $"{app} for {site.Name}: at least {Money(c.Monthly)} a month. Incomplete: the site profile doesn't say how many {string.Join(", ", c.Missing)}.";
}
