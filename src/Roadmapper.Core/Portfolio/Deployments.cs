namespace Roadmapper.Core.Portfolio;

/// <summary>
/// Where an application stands at a customer site. Pipeline: an opportunity, not agreed. Evaluating: a demo or trial.
/// Implementing: agreed and being set up. Live: in production use. Retired: no longer used there.
/// </summary>
public enum DeploymentStatus { Pipeline, Evaluating, Implementing, Live, Retired }

/// <summary>
/// An application in use (or on the way) for a customer, at one of its sites or for the whole customer
/// (<see cref="Site"/> null). Recorded per comparison (<see cref="Category"/>), like prices, so an application added
/// to another category's comparison (a WMS's built-in yard features in the YMS comparison) can stand differently there.
/// </summary>
public class AppDeployment
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    public int TradingPartnerId { get; set; }
    public TradingPartner? TradingPartner { get; set; }
    /// <summary>The site or location, e.g. "AZ" or "Ontario Corporate"; null for the whole customer.</summary>
    public string? Site { get; set; }
    public DeploymentStatus Status { get; set; } = DeploymentStatus.Live;
    /// <summary>When it reached its status, e.g. the go-live date.</summary>
    public DateOnly? Since { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class DeploymentRules
{
    public const int MaxSiteLength = 100;

    /// <summary>Statuses in progress: on the way, not live yet.</summary>
    public static bool InProgress(DeploymentStatus s) => s is DeploymentStatus.Pipeline or DeploymentStatus.Evaluating or DeploymentStatus.Implementing;

    /// <summary>
    /// The sites typed into one box: separated by commas, semicolons or new lines, trimmed, repeats dropped (ignoring
    /// case). None typed means the whole customer, a single null.
    /// </summary>
    public static IReadOnlyList<string?> SplitSites(string? text)
    {
        var sites = (text ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Cast<string?>().ToList();
        return sites.Count == 0 ? [null] : sites;
    }

    /// <summary>"Interstate Batteries · AZ", or just the customer when it's the whole customer.</summary>
    public static string Where(string customer, string? site) => site is null ? customer : $"{customer} · {site}";

    public static bool SameSite(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A deployment's editable fields. A new one may name several sites (one deployment each) and a new customer.</summary>
public class DeploymentDraft
{
    public int Id { get; set; }
    public AppCategory Category { get; set; }
    public int ApplicationId { get; set; }
    /// <summary>The customer; 0 with <see cref="NewCustomerName"/> set adds the customer as a trading partner.</summary>
    public int TradingPartnerId { get; set; }
    public string? NewCustomerName { get; set; }
    /// <summary>The site; for a new deployment, one or more separated by commas.</summary>
    public string? Sites { get; set; }
    public DeploymentStatus Status { get; set; } = DeploymentStatus.Live;
    public DateOnly? Since { get; set; }
    public string? Note { get; set; }

    public bool IsNew => Id == 0;

    public static DeploymentDraft New(AppCategory category, int applicationId = 0, int partnerId = 0, string? site = null) =>
        new() { Category = category, ApplicationId = applicationId, TradingPartnerId = partnerId, Sites = site };

    public static DeploymentDraft From(AppDeployment d) => new()
    {
        Id = d.Id, Category = d.Category, ApplicationId = d.ApplicationId, TradingPartnerId = d.TradingPartnerId, Sites = d.Site,
        Status = d.Status, Since = d.Since, Note = d.Note,
    };

    public IReadOnlyList<string?> SiteList => IsNew ? DeploymentRules.SplitSites(Sites) : [DraftText.Clean(Sites)];

    public string? Validate()
    {
        if (ApplicationId <= 0) return "Pick the application.";
        if (TradingPartnerId <= 0 && string.IsNullOrWhiteSpace(NewCustomerName)) return "Pick the customer, or type a new one's name.";
        if (TradingPartnerId <= 0 && NewCustomerName!.Trim().Length > PortfolioRules.MaxNameLength)
            return $"A customer's name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (!IsNew && Sites?.IndexOfAny([',', ';', '\n']) >= 0) return "One site per entry when editing. Add the other sites as new entries.";
        if (SiteList.FirstOrDefault(s => s?.Length > DeploymentRules.MaxSiteLength) is { } longSite)
            return $"A site can be at most {DeploymentRules.MaxSiteLength} characters (“{longSite[..20]}…”).";
        if (Note?.Trim().Length > CapabilityRules.MaxNoteLength) return $"A note can be at most {CapabilityRules.MaxNoteLength} characters.";
        return null;
    }

    /// <summary>Copies the fields for one site (the service sets the customer).</summary>
    public void ApplyTo(AppDeployment d, string? site)
    {
        d.Category = Category;
        d.ApplicationId = ApplicationId;
        d.Site = DraftText.Clean(site);
        d.Status = Status;
        d.Since = Since;
        d.Note = DraftText.Clean(Note);
    }
}

/// <summary>One row of the customer matrix: a customer at one site (or the whole customer).</summary>
public sealed record CustomerSite(int PartnerId, string Customer, string? Site)
{
    public string Key => $"{PartnerId}|{(Site ?? "").ToLowerInvariant()}";
}

/// <summary>An application's reach: live sites and customers, and how many are on the way.</summary>
public sealed record DeploymentReach(int LiveSites, int LiveCustomers, int InProgress)
{
    public bool NoneLive => LiveSites == 0;
}

/// <summary>
/// Customers and sites × the applications of a comparison, with each cell's deployment. Rows are customers by name,
/// then their sites (the whole customer first). Only rows with a deployment of a shown application are included.
/// </summary>
public sealed class CustomerMatrix
{
    private readonly Dictionary<(string Row, int App), AppDeployment> _cells;

    private CustomerMatrix(IReadOnlyList<CustomerSite> rows, IReadOnlyList<int> apps, Dictionary<(string, int), AppDeployment> cells,
        IReadOnlyDictionary<int, DeploymentReach> reach)
    {
        (Rows, Apps, _cells, Reach) = (rows, apps, cells, reach);
    }

    public IReadOnlyList<CustomerSite> Rows { get; }
    public IReadOnlyList<int> Apps { get; }
    public IReadOnlyDictionary<int, DeploymentReach> Reach { get; }
    public int Customers => Rows.Select(r => r.PartnerId).Distinct().Count();

    public AppDeployment? Cell(CustomerSite row, int appId) => _cells.GetValueOrDefault((row.Key, appId));

    /// <summary>
    /// Builds the matrix for <paramref name="appIds"/> (in that order) from deployments with their trading partner
    /// loaded. <paramref name="statuses"/> keeps only those statuses (all when null); reach counts all of them anyway.
    /// </summary>
    public static CustomerMatrix Build(IReadOnlyList<int> appIds, IEnumerable<AppDeployment> deployments, IReadOnlyCollection<DeploymentStatus>? statuses = null)
    {
        var shown = appIds.ToHashSet();
        var all = deployments.Where(d => shown.Contains(d.ApplicationId)).ToList();
        var reach = appIds.Distinct().ToDictionary(id => id, id =>
        {
            var mine = all.Where(d => d.ApplicationId == id).ToList();
            var live = mine.Where(d => d.Status == DeploymentStatus.Live).ToList();
            return new DeploymentReach(live.Count, live.Select(d => d.TradingPartnerId).Distinct().Count(), mine.Count(d => DeploymentRules.InProgress(d.Status)));
        });
        var kept = all.Where(d => statuses is null || statuses.Contains(d.Status)).ToList();
        var cells = new Dictionary<(string, int), AppDeployment>();
        var rows = new Dictionary<string, CustomerSite>();
        foreach (var d in kept)
        {
            var row = new CustomerSite(d.TradingPartnerId, d.TradingPartner?.Name ?? $"#{d.TradingPartnerId}", d.Site);
            rows.TryAdd(row.Key, row);
            cells[(row.Key, d.ApplicationId)] = d;
        }
        var ordered = rows.Values
            .OrderBy(r => r.Customer, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.PartnerId)
            .ThenBy(r => r.Site is not null).ThenBy(r => r.Site, StringComparer.OrdinalIgnoreCase).ToList();
        return new CustomerMatrix(ordered, appIds, cells, reach);
    }
}
