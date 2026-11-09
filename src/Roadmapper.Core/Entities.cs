namespace Roadmapper.Core;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ScoringModel ScoringModel { get; set; } = ScoringModel.Rice;
    /// <summary>The WSJF scoring rubric's scale, e.g. "1,2,3,5,8,13,20"; null means <see cref="RubricScale.Default"/>.</summary>
    public string? WsjfScale { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }

    public List<RoadmapItem> Items { get; set; } = new();
    public List<Theme> Themes { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
}

/// <summary>A Goal, Initiative, Feature or Task. All levels share one table, linked by <see cref="ParentId"/>.</summary>
public class RoadmapItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int? ParentId { get; set; }
    public RoadmapItem? Parent { get; set; }
    public List<RoadmapItem> Children { get; set; } = new();

    public ItemType ItemType { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Idea;
    public int ProgressPct { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Horizon? Horizon { get; set; }
    public string? Owner { get; set; }
    /// <summary>The matching Jira issue, e.g. "WMS-123". Jira stays the system of record for the work itself.</summary>
    public string? JiraKey { get; set; }
    /// <summary>A link to the item elsewhere (a Jira issue, Confluence page, ticket). Overrides the link built from <see cref="JiraKey"/>.</summary>
    public string? ExternalUrl { get; set; }

    public int? ThemeId { get; set; }
    public Theme? Theme { get; set; }

    public int SortOrder { get; set; }
    /// <summary>When set, ranks ahead of any computed score (1 = top).</summary>
    public int? ManualRank { get; set; }
    /// <summary>When true, a parent keeps its own dates/progress instead of rolling up from children.</summary>
    public bool IsRollupOverridden { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>For reference only: the template this task was copied from, by name, as it was when applied.</summary>
    public string? SourceTemplateName { get; set; }
    public DateTime? SourceTemplateAppliedAt { get; set; }
    /// <summary>Shared by every task created in one "apply template" action, so the batch can be undone together.</summary>
    public string? TemplateBatchId { get; set; }

    public ItemScore? Score { get; set; }
    /// <summary>This item's answers to the product's WSJF scoring rubric.</summary>
    public List<ItemRubricAnswer> RubricAnswers { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
    /// <summary>Dependencies where this item is the successor (i.e. it is blocked by them).</summary>
    public List<Dependency> Predecessors { get; set; } = new();
    /// <summary>Dependencies where this item is the predecessor (i.e. it blocks them).</summary>
    public List<Dependency> Successors { get; set; } = new();
}

/// <summary>Inputs for every scoring model; only the product's active model is used to compute <see cref="ComputedScore"/>.</summary>
public class ItemScore
{
    public int ItemId { get; set; }

    // RICE
    public double? Reach { get; set; }
    public double? Impact { get; set; }
    /// <summary>Percent, 0-100.</summary>
    public double? Confidence { get; set; }
    public double? Effort { get; set; }

    // WSJF
    public double? BusinessValue { get; set; }
    public double? TimeCriticality { get; set; }
    public double? RiskReduction { get; set; }
    public double? JobSize { get; set; }

    /// <summary>WSJF factors typed by hand. The scoring rubric never overwrites these.</summary>
    public WsjfFactors ManualFactors { get; set; }

    // MoSCoW
    public MoscowPriority? Moscow { get; set; }

    public double? ComputedScore { get; set; }
}

public class Theme
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4f7cff";
}

public class Tag
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public List<RoadmapItem> Items { get; set; } = new();
}

/// <summary>
/// One recorded change. Deliberately has no foreign key to the item, so history survives
/// deletion; <see cref="ItemTitle"/> is a snapshot taken at the time of the change.
/// </summary>
public class AuditEntry
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? ItemId { get; set; }
    public string? ItemTitle { get; set; }
    public AuditAction Action { get; set; }
    /// <summary>Display name of the changed field; null for whole-item create/delete.</summary>
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

public class Dependency
{
    public int Id { get; set; }
    public int PredecessorId { get; set; }
    public RoadmapItem? Predecessor { get; set; }
    public int SuccessorId { get; set; }
    public RoadmapItem? Successor { get; set; }
    public DependencyType Type { get; set; } = DependencyType.Blocks;
}

/// <summary>
/// One question in a product's WSJF scoring rubric. It feeds one factor, either as a base score or as an
/// adjuster that moves the factor up or down the scale. See <see cref="RubricCalculator"/>.
/// </summary>
public class ScoringCriterion
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public WsjfFactor Factor { get; set; }
    public CriterionRole Role { get; set; }
    public CriterionKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Help { get; set; }
    public int SortOrder { get; set; }
    public List<ScoringCriterionOption> Options { get; set; } = new();
}

/// <summary>
/// A band (number bands and start date: applies from <see cref="From"/> upwards) or a choice (pick-list: <see cref="Label"/>).
/// <see cref="Value"/> is a score on the scale for a base criterion, or a number of steps for an adjuster.
/// </summary>
public class ScoringCriterionOption
{
    public int Id { get; set; }
    public int CriterionId { get; set; }
    public int SortOrder { get; set; }
    public string? Label { get; set; }
    public double? From { get; set; }
    public double Value { get; set; }
}

/// <summary>An item's answer to one criterion: <see cref="Number"/> for number bands, <see cref="OptionId"/> for a pick-list.</summary>
public class ItemRubricAnswer
{
    public int ItemId { get; set; }
    public int CriterionId { get; set; }
    public double? Number { get; set; }
    public int? OptionId { get; set; }
}
