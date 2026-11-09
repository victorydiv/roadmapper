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
    public string? JiraKey { get; set; }
    public string? ExternalUrl { get; set; }
    public int? ThemeId { get; set; }
    public int? ManualRank { get; set; }
    public bool IsRollupOverridden { get; set; }
    public HashSet<int> TagIds { get; set; } = new();
    public ItemScore Score { get; set; } = new();
    /// <summary>Answers to the product's WSJF scoring rubric, by criterion id. The service saves them with the item.</summary>
    public Dictionary<int, RubricAnswer> Answers { get; set; } = new();
    /// <summary>Carried through edits and import unchanged; the editor shows them read-only.</summary>
    public string? SourceTemplateName { get; set; }
    public DateTime? SourceTemplateAppliedAt { get; set; }

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
        JiraKey = i.JiraKey,
        ExternalUrl = i.ExternalUrl,
        ThemeId = i.ThemeId,
        ManualRank = i.ManualRank,
        IsRollupOverridden = i.IsRollupOverridden,
        TagIds = i.Tags.Select(t => t.Id).ToHashSet(),
        Score = CopyScore(i.Score ?? new ItemScore()),
        Answers = RubricCalculator.AnswersOf(i),
        SourceTemplateName = i.SourceTemplateName,
        SourceTemplateAppliedAt = i.SourceTemplateAppliedAt,
    };

    /// <summary>Returns a user-facing error, or null when valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title)) return "Title is required.";
        if (StartDate is { } s && EndDate is { } e && e < s) return "End date can't be before the start date.";
        if (ProgressPct is < 0 or > 100) return "Progress must be between 0 and 100.";
        if (ManualRank is < 1) return "Manual rank must be 1 or higher.";
        if (JiraKey?.Trim().ToUpperInvariant() is { Length: > 0 } key && (key.Length > ExternalLinks.MaxKeyLength || !ExternalLinks.IsJiraKey(key)))
            return "A Jira key looks like WMS-123: the project key, a dash and the issue number.";
        if (ExternalUrl?.Trim() is { Length: > 0 } url && (url.Length > ExternalLinks.MaxUrlLength || !ExternalLinks.IsWebUrl(url)))
            return "The link must be a full web address starting with https:// or http://.";
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
        item.JiraKey = string.IsNullOrWhiteSpace(JiraKey) ? null : JiraKey.Trim().ToUpperInvariant();
        item.ExternalUrl = string.IsNullOrWhiteSpace(ExternalUrl) ? null : ExternalUrl.Trim();
        item.ThemeId = ThemeId;
        item.ManualRank = ManualRank;
        item.IsRollupOverridden = IsRollupOverridden;
        item.SourceTemplateName = SourceTemplateName;
        item.SourceTemplateAppliedAt = SourceTemplateAppliedAt;

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
        s.ManualFactors = Score.ManualFactors;
    }

    /// <summary>A detached copy of a score.</summary>
    public static ItemScore CopyScore(ItemScore s) => new()
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
        ManualFactors = s.ManualFactors,
        ComputedScore = s.ComputedScore,
    };
}
