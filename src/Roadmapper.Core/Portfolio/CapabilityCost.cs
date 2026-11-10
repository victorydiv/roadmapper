namespace Roadmapper.Core.Portfolio;

/// <summary>
/// One line of an application's pricing in a category's comparison, as the vendor quotes it: e.g. "$1,200 per site
/// per month" or "$90,000 implementation, one-time". The quantity comes from the <see cref="SiteProfile"/> the
/// comparison is costed for (or <see cref="Quantity"/> when the line sets its own).
/// </summary>
public class PricingLine
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    public string Label { get; set; } = "";
    public PricingBasis Basis { get; set; } = PricingBasis.Site;
    /// <summary>What's counted, for <see cref="PricingBasis.Other"/> (e.g. "kiosk").</summary>
    public string? Unit { get; set; }
    /// <summary>A fixed quantity for this line, used instead of the site profile's.</summary>
    public decimal? Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public PriceFrequency Frequency { get; set; } = PriceFrequency.Monthly;
    /// <summary>The least this line costs per period (or once, for one-time lines).</summary>
    public decimal? Minimum { get; set; }
    public PriceSource Source { get; set; } = PriceSource.Estimate;
    public DateOnly? AsOf { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A typical site a category's solutions are costed for, e.g. "Small DC": its size in each of the units vendors price
/// by. A blank size is unknown, so lines priced by it can't be costed.
/// </summary>
public class SiteProfile
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public int? Users { get; set; }
    public int? Licenses { get; set; }
    public int? DockDoors { get; set; }
    public int? Assets { get; set; }
    public int? TransactionsPerMonth { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The profile used when a category has none: one site, every other size unknown.</summary>
    public static SiteProfile OneSite(AppCategory category) => new() { Category = category, Name = "One site" };

    /// <summary>How many of <paramref name="basis"/> this site has (Flat and Site: 1; null: unknown or Other).</summary>
    public decimal? QuantityOf(PricingBasis basis) => basis switch
    {
        PricingBasis.Flat or PricingBasis.Site => 1,
        PricingBasis.User => Users,
        PricingBasis.License => Licenses,
        PricingBasis.DockDoor => DockDoors,
        PricingBasis.Asset => Assets,
        PricingBasis.Transaction => TransactionsPerMonth,
        _ => null,
    };

    public SiteProfile Copy() => (SiteProfile)MemberwiseClone();
}

/// <summary>One line costed for a site: its quantity and charge (null when the quantity is unknown).</summary>
public sealed record CostedLine(PricingLine Line, decimal? Quantity, decimal? Charge);

/// <summary>
/// What an application would cost one site: <see cref="Monthly"/> (monthly lines plus annual ones ÷ 12),
/// <see cref="OneTime"/> and <see cref="FirstYear"/>. <see cref="IsPriced"/>: it has pricing at all;
/// <see cref="IsComplete"/>: every line could be costed for the site (otherwise <see cref="Missing"/> says what size is
/// needed, and the totals leave those lines out).
/// </summary>
public sealed record CostEstimate(IReadOnlyList<CostedLine> Lines, decimal Monthly, decimal OneTime, IReadOnlyList<string> Missing)
{
    public bool IsPriced => Lines.Count > 0;
    public bool IsComplete => IsPriced && Missing.Count == 0;
    public decimal Annual => Monthly * 12;
    public decimal FirstYear => Annual + OneTime;
    /// <summary>The sources of its prices, e.g. Quote and Estimate.</summary>
    public IReadOnlyList<PriceSource> Sources => Lines.Select(l => l.Line.Source).Distinct().Order().ToList();

    /// <summary>Monthly cost per unit of <paramref name="count"/> (e.g. per user), when it's known and above zero.</summary>
    public decimal? MonthlyPer(int? count) => IsPriced && count is > 0 ? Math.Round(Monthly / count.Value, 2) : null;

    public static readonly CostEstimate NotPriced = new([], 0, 0, []);

    /// <summary>Costs <paramref name="lines"/> for <paramref name="site"/>.</summary>
    public static CostEstimate For(IEnumerable<PricingLine> lines, SiteProfile site)
    {
        var costed = new List<CostedLine>();
        var missing = new List<string>();
        decimal monthly = 0, oneTime = 0;
        foreach (var line in lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id))
        {
            var qty = line.Quantity ?? site.QuantityOf(line.Basis);
            if (qty is null)
            {
                costed.Add(new(line, null, null));
                var what = CostRules.UnitName(line, plural: true);
                if (!missing.Contains(what)) missing.Add(what);
                continue;
            }
            var charge = Math.Max(qty.Value * line.UnitPrice, line.Minimum ?? 0);
            costed.Add(new(line, qty, charge));
            switch (line.Frequency)
            {
                case PriceFrequency.Monthly: monthly += charge; break;
                case PriceFrequency.Annual: monthly += charge / 12; break;
                default: oneTime += charge; break;
            }
        }
        return new(costed, Math.Round(monthly, 2), Math.Round(oneTime, 2), missing);
    }
}

public class PricingLineDraft
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public int ApplicationId { get; set; }
    public string Label { get; set; } = "";
    public PricingBasis Basis { get; set; } = PricingBasis.Site;
    public string? Unit { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public PriceFrequency Frequency { get; set; } = PriceFrequency.Monthly;
    public decimal? Minimum { get; set; }
    public PriceSource Source { get; set; } = PriceSource.Estimate;
    public DateOnly? AsOf { get; set; }
    public string? Note { get; set; }

    public bool IsNew => Id == 0;

    public static PricingLineDraft New(AppCategory category, int applicationId) => new() { Category = category, ApplicationId = applicationId };

    public static PricingLineDraft From(PricingLine l) => new()
    {
        Id = l.Id, Category = l.Category, ApplicationId = l.ApplicationId, Label = l.Label, Basis = l.Basis, Unit = l.Unit,
        Quantity = l.Quantity, UnitPrice = l.UnitPrice, Frequency = l.Frequency, Minimum = l.Minimum, Source = l.Source, AsOf = l.AsOf, Note = l.Note,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Label)) return "Give the price a name, e.g. Subscription or Implementation.";
        if (Label.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (UnitPrice is null) return "Enter a price.";
        if (UnitPrice < 0) return "Price can't be negative.";
        if (Minimum is < 0) return "Minimum can't be negative.";
        if (Quantity is < 0) return "Quantity can't be negative.";
        if (Basis == PricingBasis.Other && string.IsNullOrWhiteSpace(Unit)) return "Say what's counted (e.g. kiosk).";
        if (Basis == PricingBasis.Other && Quantity is null) return "Enter how many, since a site profile doesn't size this.";
        if (Unit?.Trim().Length > 40) return "Unit can be at most 40 characters.";
        if (Basis == PricingBasis.Transaction && Frequency != PriceFrequency.Monthly) return "Per-transaction prices are costed monthly from transactions per month.";
        if (Note?.Trim().Length > CapabilityRules.MaxNoteLength) return $"A note can be at most {CapabilityRules.MaxNoteLength} characters.";
        return null;
    }

    public void ApplyTo(PricingLine l)
    {
        l.Category = Category;
        l.ApplicationId = ApplicationId;
        l.Label = Label.Trim();
        l.Basis = Basis;
        l.Unit = Basis == PricingBasis.Other ? DraftText.Clean(Unit) : null;
        l.Quantity = Basis is PricingBasis.Flat ? null : Quantity;
        l.UnitPrice = UnitPrice ?? 0;
        l.Frequency = Frequency;
        l.Minimum = Minimum is 0 ? null : Minimum;
        l.Source = Source;
        l.AsOf = AsOf;
        l.Note = DraftText.Clean(Note);
    }
}

public class SiteProfileDraft
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public int? Users { get; set; }
    public int? Licenses { get; set; }
    public int? DockDoors { get; set; }
    public int? Assets { get; set; }
    public int? TransactionsPerMonth { get; set; }
    public string? Notes { get; set; }

    public bool IsNew => Id == 0;

    public static SiteProfileDraft From(SiteProfile p) => new()
    {
        Id = p.Id, Category = p.Category, Name = p.Name, IsDefault = p.IsDefault, Users = p.Users, Licenses = p.Licenses,
        DockDoors = p.DockDoors, Assets = p.Assets, TransactionsPerMonth = p.TransactionsPerMonth, Notes = p.Notes,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Give the site profile a name, e.g. Small DC.";
        if (Name.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (Users < 0 || Licenses < 0 || DockDoors < 0 || Assets < 0 || TransactionsPerMonth < 0) return "Sizes can't be negative.";
        if (Notes?.Trim().Length > 1000) return "Notes can be at most 1000 characters.";
        return null;
    }

    public void ApplyTo(SiteProfile p)
    {
        p.Category = Category;
        p.Name = Name.Trim();
        p.Users = Users;
        p.Licenses = Licenses;
        p.DockDoors = DockDoors;
        p.Assets = Assets;
        p.TransactionsPerMonth = TransactionsPerMonth;
        p.Notes = DraftText.Clean(Notes);
    }

    /// <summary>The profile as typed, unsaved (for "try your site").</summary>
    public SiteProfile ToProfile()
    {
        var p = new SiteProfile { Id = Id, IsDefault = IsDefault };
        ApplyTo(p);
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Your site";
        return p;
    }
}

public static class CostRules
{
    /// <summary>The thing a line is priced per, e.g. "user" / "users", or its own unit for Other.</summary>
    public static string UnitName(PricingLine line, bool plural = false) => line.Basis switch
    {
        PricingBasis.Other => (line.Unit ?? "unit") + (plural ? "s" : ""),
        _ => UnitName(line.Basis, plural),
    };

    public static string UnitName(PricingBasis basis, bool plural = false) => (basis, plural) switch
    {
        (PricingBasis.Flat, _) => "flat",
        (PricingBasis.Site, false) => "site",
        (PricingBasis.Site, true) => "sites",
        (PricingBasis.User, false) => "user",
        (PricingBasis.User, true) => "users",
        (PricingBasis.License, false) => "license",
        (PricingBasis.License, true) => "licenses",
        (PricingBasis.DockDoor, false) => "dock door",
        (PricingBasis.DockDoor, true) => "dock doors",
        (PricingBasis.Asset, false) => "trailer / asset",
        (PricingBasis.Asset, true) => "trailers / assets",
        (PricingBasis.Transaction, false) => "transaction",
        (PricingBasis.Transaction, true) => "transactions per month",
        _ => plural ? "units" : "unit",
    };
}
