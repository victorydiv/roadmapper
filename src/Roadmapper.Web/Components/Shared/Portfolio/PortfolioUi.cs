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

        public static string Fit(int? score) => score is { } s ? $"{s}/5" : "–";

    /// <summary>The detail page of an application or trading partner.</summary>
    public static string Href(NodeRef node) => node.IsPartner ? $"/portfolio/partners/{node.Id}" : $"/portfolio/applications/{node.Id}";
}
