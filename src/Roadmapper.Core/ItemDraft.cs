namespace Roadmapper.Core;

/// <summary>A detached, editable copy of an item's fields, so the UI never binds to tracked entities.</summary>
public class ItemDraft
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? ParentId { get; set; }
    public ItemType ItemType { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Idea;
    public int ProgressPct { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Horizon? Horizon { get; set; }
    public string? Owner { get; set; }
    public int? ThemeId { get; set; }
    public int? ManualRank { get; set; }
    public bool IsRollupOverridden { get; set; }
    public HashSet<int> TagIds { get; set; } = new();
    public ItemScore Score { get; set; } = new();

    public bool IsNew => Id == 0;

    public static ItemDraft New(int productId, ItemType type, int? parentId) =>
        new() { ProductId = productId, ItemType = type, ParentId = parentId };

    public static ItemDraft From(RoadmapItem i) => new()
    {
        Id = i.Id,
        ProductId = i.ProductId,
        ParentId = i.ParentId,
        ItemType = i.ItemType,
        Title = i.Title,
        Description = i.Description,
        Status = i.Status,
        ProgressPct = i.ProgressPct,
        StartDate = i.StartDate,
        EndDate = i.EndDate,
        Horizon = i.Horizon,
        Owner = i.Owner,
        ThemeId = i.ThemeId,
        ManualRank = i.ManualRank,
        IsRollupOverridden = i.IsRollupOverridden,
        TagIds = i.Tags.Select(t => t.Id).ToHashSet(),
        Score = CopyScore(i.Score ?? new ItemScore()),
    };

    /// <summary>Returns a user-facing error, or null when valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title)) return "Title is required.";
        if (StartDate is { } s && EndDate is { } e && e < s) return "End date can't be before the start date.";
        if (ProgressPct is < 0 or > 100) return "Progress must be between 0 and 100.";
        if (ManualRank is < 1) return "Manual rank must be 1 or higher.";
        return null;
    }

    /// <summary>Copies editable fields onto an entity. Type and tags are handled by the caller.</summary>
    public void ApplyTo(RoadmapItem item)
    {
        item.ParentId = ParentId;
        item.Title = Title.Trim();
        item.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        item.Status = Status;
        item.ProgressPct = Status == ItemStatus.Done ? 100 : ProgressPct;
        item.StartDate = StartDate;
        item.EndDate = EndDate;
        item.Horizon = Horizon;
        item.Owner = string.IsNullOrWhiteSpace(Owner) ? null : Owner.Trim();
        item.ThemeId = ThemeId;
        item.ManualRank = ManualRank;
        item.IsRollupOverridden = IsRollupOverridden;

        item.Score ??= new ItemScore();
        var s = item.Score;
        s.Reach = Score.Reach;
        s.Impact = Score.Impact;
        s.Confidence = Score.Confidence;
        s.Effort = Score.Effort;
        s.BusinessValue = Score.BusinessValue;
        s.TimeCriticality = Score.TimeCriticality;
        s.RiskReduction = Score.RiskReduction;
        s.JobSize = Score.JobSize;
        s.Moscow = Score.Moscow;
    }

    private static ItemScore CopyScore(ItemScore s) => new()
    {
        ItemId = s.ItemId,
        Reach = s.Reach,
        Impact = s.Impact,
        Confidence = s.Confidence,
        Effort = s.Effort,
        BusinessValue = s.BusinessValue,
        TimeCriticality = s.TimeCriticality,
        RiskReduction = s.RiskReduction,
        JobSize = s.JobSize,
        Moscow = s.Moscow,
        ComputedScore = s.ComputedScore,
    };
}
