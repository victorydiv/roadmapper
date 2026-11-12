namespace Roadmapper.Core.Portfolio;

/// <summary>
/// Where a development project is in the pipeline. The main stages run Requested → Quoted → Approved → In
/// development → Test → Complete (deployed); On hold and Cancelled are set aside.
/// </summary>
public enum DevStatus { Requested, Quoted, Approved, InDevelopment, Test, Complete, OnHold, Cancelled }

/// <summary>
/// A piece of development work for a product: a customer request, an EDI map, a WMS enhancement. It moves through the
/// pipeline (<see cref="Status"/>), has a cost breakdown by category (<see cref="CostLines"/>) and documents attached
/// to it (its SOW, quote) like any other record.
/// </summary>
public class DevProject
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string Name { get; set; } = "";
    public DevStatus Status { get; set; } = DevStatus.Requested;
    public string? Description { get; set; }
    /// <summary>A quote, ticket or PO number.</summary>
    public string? Reference { get; set; }
    /// <summary>The customer it's for (a trading partner), if any.</summary>
    public int? CustomerId { get; set; }
    public TradingPartner? Customer { get; set; }
    /// <summary>The system being changed, if any.</summary>
    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }
    /// <summary>Who's doing the work, when it's a vendor.</summary>
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    /// <summary>The roadmap item it delivers, if any (always one of <see cref="ProductId"/>'s).</summary>
    public int? RoadmapItemId { get; set; }
    public RoadmapItem? RoadmapItem { get; set; }
    public DateOnly? TargetDate { get; set; }
    /// <summary>When it was deployed / completed; set to the day it's marked Complete when left blank.</summary>
    public DateOnly? CompletedOn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<DevCostLine> CostLines { get; set; } = [];

    public decimal TotalCost => CostLines.Sum(l => l.Amount);
    public decimal TotalHours => CostLines.Sum(l => l.Hours ?? 0);
    public decimal CostOf(int categoryId) => CostLines.Where(l => l.CategoryId == categoryId).Sum(l => l.Amount);
}

/// <summary>A kind of cost in a development project's breakdown (DEV, QA, EDI, PM…), shared by all products.</summary>
public class DevCostCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>The usual hourly rate, filled in when a line of this category is added.</summary>
    public decimal? DefaultRate { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>One line of a project's cost: hours × rate, or a fixed amount (e.g. a flat fee) when there are no hours.</summary>
public class DevCostLine
{
    public int Id { get; set; }
    public int DevProjectId { get; set; }
    public DevProject? DevProject { get; set; }
    public int CategoryId { get; set; }
    public DevCostCategory? Category { get; set; }
    public decimal? Hours { get; set; }
    public decimal? Rate { get; set; }
    /// <summary>The line's amount when it isn't hours × rate.</summary>
    public decimal? FixedAmount { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }

    public decimal Amount => DevRules.Amount(Hours, Rate, FixedAmount);
}

public static class DevRules
{
    public const int MaxReferenceLength = 100;
    public const int MaxNoteLength = 500;
    public const int MaxCategoryLength = 40;

    /// <summary>The statuses in pipeline order: the main stages, then On hold and Cancelled.</summary>
    public static readonly IReadOnlyList<DevStatus> Pipeline = Enum.GetValues<DevStatus>();

    /// <summary>Still in the pipeline: not complete, cancelled or on hold.</summary>
    public static bool IsOpen(DevStatus s) => s is not (DevStatus.Complete or DevStatus.Cancelled or DevStatus.OnHold);

    /// <summary>Approved and not yet complete: money that's committed.</summary>
    public static bool IsCommitted(DevStatus s) => s is DevStatus.Approved or DevStatus.InDevelopment or DevStatus.Test;

    /// <summary>Not approved yet: money that's only quoted or requested.</summary>
    public static bool IsProposed(DevStatus s) => s is DevStatus.Requested or DevStatus.Quoted;

    public static decimal Amount(decimal? hours, decimal? rate, decimal? fixedAmount) =>
        hours is { } h ? Math.Round(h * (rate ?? 0), 2) : fixedAmount ?? 0;

    /// <summary>Counts and cost by status, and the proposed / committed / completed totals, of some projects.</summary>
    public static DevPipelineSummary Summarize(IEnumerable<DevProject> projects)
    {
        var list = projects.ToList();
        return new DevPipelineSummary(
            Pipeline.ToDictionary(s => s, s => (list.Count(p => p.Status == s), list.Where(p => p.Status == s).Sum(p => p.TotalCost))),
            list.Where(p => IsProposed(p.Status)).Sum(p => p.TotalCost),
            list.Where(p => IsCommitted(p.Status)).Sum(p => p.TotalCost),
            list.Where(p => p.Status == DevStatus.Complete).Sum(p => p.TotalCost));
    }
}

/// <summary>A pipeline's projects by status, and its money: proposed (requested, quoted), committed (approved to test) and complete.</summary>
public sealed record DevPipelineSummary(IReadOnlyDictionary<DevStatus, (int Count, decimal Cost)> ByStatus, decimal Proposed, decimal Committed, decimal Completed);

public class DevCostLineDraft
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public decimal? Hours { get; set; }
    public decimal? Rate { get; set; }
    /// <summary>A fixed amount; used only when <see cref="Hours"/> is blank.</summary>
    public decimal? Amount { get; set; }
    public string? Note { get; set; }

    public bool ByHours => Hours is not null;
    public decimal Total => DevRules.Amount(Hours, Rate, Amount);
    public bool IsBlank => CategoryId == 0 && Hours is null && Rate is null && Amount is null && string.IsNullOrWhiteSpace(Note);

    public static DevCostLineDraft From(DevCostLine l) => new()
    {
        Id = l.Id, CategoryId = l.CategoryId, Hours = l.Hours, Rate = l.Rate, Amount = l.Hours is null ? l.FixedAmount : null, Note = l.Note,
    };

    public string? Validate()
    {
        if (CategoryId == 0) return "Choose a category for each cost line.";
        if (Hours is < 0 || Rate is < 0 || Amount is < 0) return "Hours, rates and amounts can't be negative.";
        if (Hours is not null && Rate is null) return "Enter a rate for each line with hours (or clear the hours and enter an amount).";
        if (Hours is null && Amount is null) return "Enter hours and a rate, or an amount, for each cost line.";
        if (Note?.Trim().Length > DevRules.MaxNoteLength) return $"A cost note can be at most {DevRules.MaxNoteLength} characters.";
        return null;
    }

    public void ApplyTo(DevCostLine l)
    {
        l.CategoryId = CategoryId;
        l.Hours = Hours;
        l.Rate = Hours is null ? null : Rate;
        l.FixedAmount = Hours is null ? Amount : null;
        l.Note = DraftText.Clean(Note);
    }
}

public class DevProjectDraft
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public DevStatus Status { get; set; } = DevStatus.Requested;
    public string? Description { get; set; }
    public string? Reference { get; set; }
    public int? CustomerId { get; set; }
    public int? ApplicationId { get; set; }
    public int? VendorId { get; set; }
    public int? RoadmapItemId { get; set; }
    public DateOnly? TargetDate { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public List<DevCostLineDraft> Lines { get; set; } = [];

    public bool IsNew => Id == 0;
    public decimal Total => Lines.Sum(l => l.Total);
    public decimal Hours => Lines.Sum(l => l.Hours ?? 0);

    public static DevProjectDraft New(int productId, DevStatus status = DevStatus.Requested) => new() { ProductId = productId, Status = status };

    public static DevProjectDraft From(DevProject p) => new()
    {
        Id = p.Id, ProductId = p.ProductId, Name = p.Name, Status = p.Status, Description = p.Description, Reference = p.Reference,
        CustomerId = p.CustomerId, ApplicationId = p.ApplicationId, VendorId = p.VendorId, RoadmapItemId = p.RoadmapItemId,
        TargetDate = p.TargetDate, CompletedOn = p.CompletedOn,
        Lines = p.CostLines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id).Select(DevCostLineDraft.From).ToList(),
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Give the project a name.";
        if (Name.Trim().Length > PortfolioRules.MaxNameLength) return $"Name can be at most {PortfolioRules.MaxNameLength} characters.";
        if (Reference?.Trim().Length > DevRules.MaxReferenceLength) return $"Reference can be at most {DevRules.MaxReferenceLength} characters.";
        if (Description?.Trim().Length > PortfolioRules.MaxNotesLength) return $"Description can be at most {PortfolioRules.MaxNotesLength} characters.";
        foreach (var l in Lines.Where(l => !l.IsBlank))
            if (l.Validate() is { } error) return error;
        return null;
    }

    /// <summary>
    /// Copies the project's own fields (not its cost lines). A project marked Complete without a date is completed
    /// <paramref name="today"/>; one that isn't Complete has no completion date.
    /// </summary>
    public void ApplyTo(DevProject p, DateOnly today)
    {
        p.ProductId = ProductId;
        p.Name = Name.Trim();
        p.Status = Status;
        p.Description = DraftText.Clean(Description);
        p.Reference = DraftText.Clean(Reference);
        p.CustomerId = CustomerId is > 0 ? CustomerId : null;
        p.ApplicationId = ApplicationId is > 0 ? ApplicationId : null;
        p.VendorId = VendorId is > 0 ? VendorId : null;
        p.RoadmapItemId = RoadmapItemId is > 0 ? RoadmapItemId : null;
        p.TargetDate = TargetDate;
        p.CompletedOn = Status == DevStatus.Complete ? CompletedOn ?? today : null;
    }
}
