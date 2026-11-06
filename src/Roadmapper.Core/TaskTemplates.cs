namespace Roadmapper.Core;

/// <summary>
/// A reusable, ordered set of tasks that can be added to any Feature in its product. Applying a template
/// copies its lines into ordinary tasks; there is no live link afterwards.
/// </summary>
public class TaskTemplate
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<TaskTemplateLine> Lines { get; set; } = new();
}

public class TaskTemplateLine
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Owner { get; set; }
    /// <summary>
    /// Gap in days before this task starts: after the previous task in the list ends (0 = the next day), or for
    /// the first task, after the Feature starts (0 = same day). Blank counts as 0; negative overlaps. See <see cref="TemplateRules.Schedule"/>.
    /// </summary>
    public int? StartOffsetDays { get; set; }
    /// <summary>Calendar days the task lasts, counting the start day (1 = a one-day task).</summary>
    public int? DurationDays { get; set; }
    public List<Tag> Tags { get; set; } = new();
}

/// <summary>A detached, editable copy of a template, so the UI never binds to tracked entities.</summary>
public class TemplateDraft
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<TemplateLineDraft> Lines { get; set; } = new();

    public bool IsNew => Id == 0;

    public static TemplateDraft From(TaskTemplate t) => new()
    {
        Id = t.Id,
        ProductId = t.ProductId,
        Name = t.Name,
        Description = t.Description,
        Lines = t.Lines.OrderBy(l => l.SortOrder).Select(TemplateLineDraft.From).ToList(),
    };

    /// <summary>Returns a user-facing error, or null when valid. Name uniqueness is checked by the service.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Template name is required.";
        if (Lines.Count == 0) return "Add at least one task.";
        for (var i = 0; i < Lines.Count; i++)
        {
            var l = Lines[i];
            if (string.IsNullOrWhiteSpace(l.Title)) return $"Task {i + 1} needs a title.";
            if (l.DurationDays is < 1) return $"Task {i + 1}: duration must be at least 1 day.";
        }
        return null;
    }
}

public class TemplateLineDraft
{
    /// <summary>Stable key for the UI while lines are added and reordered.</summary>
    public Guid Key { get; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public int? StartOffsetDays { get; set; }
    public int? DurationDays { get; set; }
    public HashSet<int> TagIds { get; set; } = new();

    public static TemplateLineDraft From(TaskTemplateLine l) => new()
    {
        Title = l.Title,
        Description = l.Description,
        Owner = l.Owner,
        StartOffsetDays = l.StartOffsetDays,
        DurationDays = l.DurationDays,
        TagIds = l.Tags.Select(t => t.Id).ToHashSet(),
    };
}

/// <summary>One task the picker proposes to create under a Feature. Dates and the checkbox are editable.</summary>
public class PlannedTask
{
    public required int FeatureId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Owner { get; init; }
    public IReadOnlyList<int> TagIds { get; init; } = [];
    /// <summary>The template line's gap and duration, kept so the chain can be recalculated in the preview.</summary>
    public int? OffsetDays { get; init; }
    public int? DurationDays { get; init; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    /// <summary>The user typed this task's dates in the preview, so rescheduling keeps them (later tasks still follow on).</summary>
    public bool DatesEdited { get; set; }
    /// <summary>The Feature already has a task with this title.</summary>
    public bool IsDuplicate { get; init; }
    public bool Include { get; set; }
}

public static class TemplateRules
{
    public const int MaxNameLength = 200, MaxTitleLength = 300;

    /// <summary>
    /// Dates for one task that follows on from <paramref name="anchor"/>: Start = anchor + offset (blank = 0),
    /// End = Start + duration − 1 (the duration counts the start day; blank = no end date).
    /// </summary>
    public static (DateOnly Start, DateOnly? End) DatesFor(int? offsetDays, int? durationDays, DateOnly anchor)
    {
        var start = anchor.AddDays(offsetDays ?? 0);
        return (start, durationDays is { } d and >= 1 ? start.AddDays(d - 1) : null);
    }

    /// <summary>Where the task after this one is anchored: the day after it ends, or its start when it has no end.</summary>
    public static DateOnly NextAnchor(DateOnly start, DateOnly? end) => end?.AddDays(1) ?? start;

    /// <summary>
    /// Chains the included tasks in order: the first starts from the Feature's start date, each later one from
    /// the day after the previous included task ends. Tasks whose dates the user edited keep them, and the chain
    /// continues from them. Excluded tasks get no dates. With no Feature start date, every task is undated.
    /// </summary>
    public static void Schedule(IEnumerable<PlannedTask> tasks, DateOnly? featureStart)
    {
        DateOnly? anchor = featureStart;
        foreach (var t in tasks)
        {
            if (!t.Include)
            {
                if (!t.DatesEdited) (t.StartDate, t.EndDate) = (null, null);
                continue;
            }
            if (!t.DatesEdited)
            {
                if (anchor is { } a) (t.StartDate, t.EndDate) = DatesFor(t.OffsetDays, t.DurationDays, a);
                else (t.StartDate, t.EndDate) = (null, null);
            }
            if (t.StartDate is { } s) anchor = NextAnchor(s, t.EndDate);
            else if (t.EndDate is { } e) anchor = e.AddDays(1);
        }
    }

    public static bool SameTitle(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The tasks a template would add to one Feature, in template order. Lines whose title matches a task
    /// already under the Feature are flagged and start unchecked.
    /// </summary>
    public static List<PlannedTask> Plan(TaskTemplate template, RoadmapItem feature, IEnumerable<RoadmapItem> existingTasks)
    {
        var titles = existingTasks.Select(t => t.Title).ToList();
        var plan = template.Lines.OrderBy(l => l.SortOrder).Select(l =>
        {
            var duplicate = titles.Any(t => SameTitle(t, l.Title));
            return new PlannedTask
            {
                FeatureId = feature.Id,
                Title = l.Title,
                Description = l.Description,
                Owner = l.Owner,
                TagIds = l.Tags.Select(t => t.Id).ToList(),
                OffsetDays = l.StartOffsetDays,
                DurationDays = l.DurationDays,
                IsDuplicate = duplicate,
                Include = !duplicate,
            };
        }).ToList();
        Schedule(plan, feature.StartDate);
        return plan;
    }

    /// <summary>
    /// A draft template built from a Feature's tasks in their order: the reverse of <see cref="Schedule"/>.
    /// Each dated task's gap is measured from the previous dated task (the first from the Feature's start), and
    /// its duration from its own dates, so applying the template to a Feature with the same start reproduces them.
    /// </summary>
    public static TemplateDraft FromFeature(RoadmapItem feature, IEnumerable<RoadmapItem> tasksInOrder)
    {
        DateOnly? anchor = feature.StartDate;
        var lines = new List<TemplateLineDraft>();
        foreach (var t in tasksInOrder)
        {
            int? gap = null;
            if (anchor is { } a && t.StartDate is { } s)
            {
                gap = s.DayNumber - a.DayNumber;
                anchor = NextAnchor(s, t.EndDate);
            }
            lines.Add(new TemplateLineDraft
            {
                Title = t.Title,
                Description = t.Description,
                Owner = t.Owner,
                StartOffsetDays = gap is 0 ? null : gap,
                DurationDays = feature.StartDate is not null && t.StartDate is { } ts && t.EndDate is { } te ? te.DayNumber - ts.DayNumber + 1 : null,
                TagIds = t.Tags.Select(x => x.Id).ToHashSet(),
            });
        }
        return new TemplateDraft { ProductId = feature.ProductId, Name = Truncate($"{feature.Title} tasks", MaxNameLength), Lines = lines };
    }

    /// <summary>
    /// What the Feature's rolled-up dates and progress become once <paramref name="added"/> join its tasks,
    /// using the same rule as <see cref="RollupService"/>. Null when the Feature's values won't change.
    /// </summary>
    public static (DateOnly? Start, DateOnly? End, int Progress)? RollupAfter(
        RoadmapItem feature, IEnumerable<RoadmapItem> existingTasks, IEnumerable<PlannedTask> added)
    {
        if (feature.IsRollupOverridden) return null;
        var children = existingTasks.Where(c => c.Status != ItemStatus.Cancelled)
            .Select(c => (c.StartDate, c.EndDate, c.ProgressPct))
            .Concat(added.Where(a => a.Include).Select(a => (a.StartDate, a.EndDate, ProgressPct: 0)))
            .ToList();
        if (children.Count == 0) return null;

        var start = children.Min(c => c.StartDate) ?? feature.StartDate;
        var end = children.Max(c => c.EndDate) ?? feature.EndDate;
        var progress = (int)Math.Round(children.Average(c => c.ProgressPct), MidpointRounding.AwayFromZero);
        return start == feature.StartDate && end == feature.EndDate && progress == feature.ProgressPct
            ? null
            : (start, end, progress);
    }

    /// <summary>"Name (copy)", then "Name (copy 2)", … avoiding names already taken (case-insensitive).</summary>
    public static string CopyName(string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var candidate = Truncate(n == 1 ? $"{name} (copy)" : $"{name} (copy {n})", MaxNameLength);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>The example template every product starts with. Users can edit or delete it.</summary>
public static class StarterTemplates
{
    public const string EdiMigrationName = "EDI Migration Standard";

    public static readonly string[] EdiMigrationTasks =
        ["Build BRD", "Review EDI documents", "Develop EDI docs", "Test EDI docs", "TR/CAB", "Implementation"];

    public static TaskTemplate EdiMigration(int productId) => new()
    {
        ProductId = productId,
        Name = EdiMigrationName,
        Description = "Standard task set for one customer EDI migration.",
        Lines = EdiMigrationTasks.Select((title, i) => new TaskTemplateLine { SortOrder = i, Title = title }).ToList(),
    };
}
