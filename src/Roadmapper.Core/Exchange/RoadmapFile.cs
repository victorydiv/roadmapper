using System.Text.Json;
using System.Text.Json.Serialization;

namespace Roadmapper.Core.Exchange;

/// <summary>
/// A whole product as one JSON document: the ".rmp" (Roadmapper) file used for backups and sharing.
/// Items, themes, tags and templates are listed flat and refer to each other by <c>id</c>. Those ids are
/// local to the file and are replaced with new ones on import, which always creates a new product.
/// </summary>
public sealed class RoadmapFile
{
    public const string FormatName = "roadmapper";
    public const int CurrentVersion = 1;
    public const string Extension = ".rmp";
    public const string ContentType = "application/json";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    public FileProduct Product { get; set; } = new();
    public List<FileTheme> Themes { get; set; } = [];
    public List<FileTag> Tags { get; set; } = [];
    public List<FileItem> Items { get; set; } = [];
    public List<FileDependency> Dependencies { get; set; } = [];
    public List<FileTemplate> Templates { get; set; } = [];
    /// <summary>The WSJF scoring rubric's criteria. Items answer them in <see cref="FileItem.RubricAnswers"/>.</summary>
    public List<FileCriterion> Rubric { get; set; } = [];
    public List<FileSnapshot> Snapshots { get; set; } = [];
    public List<FileHistoryEntry> History { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Reads and checks a file; throws <see cref="InvalidOperationException"/> with a user-facing message when it can't be used.</summary>
    public static RoadmapFile Read(Stream stream)
    {
        RoadmapFile? file;
        try { file = JsonSerializer.Deserialize<RoadmapFile>(stream, Json); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"This isn't a readable Roadmapper file{(ex.LineNumber is { } line ? $" (problem near line {line + 1})" : "")}.");
        }
        if (file is null) throw new InvalidOperationException("This file is empty.");
        if (file.Validate() is { Count: > 0 } errors)
            throw new InvalidOperationException(errors.Count == 1
                ? errors[0]
                : $"This file has {errors.Count} problems: {string.Join(" ", errors.Take(5))}{(errors.Count > 5 ? " …" : "")}");
        return file;
    }

    /// <summary>Every reason the file can't be imported; empty when it's fine.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (!string.Equals(Format, FormatName, StringComparison.OrdinalIgnoreCase))
            return ["This isn't a Roadmapper file."];
        if (Version > CurrentVersion)
            return ["This file was made by a newer version of Roadmapper. Update the app to open it."];
        if (Version < 1) return ["This file's version number isn't valid."];

        // Null lists can come from hand-edited files; treat them as empty.
        Product ??= new FileProduct();
        Themes ??= []; Tags ??= []; Items ??= []; Dependencies ??= []; Templates ??= []; Rubric ??= []; Snapshots ??= []; History ??= [];

        if (string.IsNullOrWhiteSpace(Product.Name)) errors.Add("The product has no name.");

        Unique(Themes.Select(t => t.Id), "theme", errors);
        Unique(Tags.Select(t => t.Id), "tag", errors);
        Unique(Items.Select(i => i.Id), "item", errors);
        if (Tags.GroupBy(t => t.Name?.Trim() ?? "", StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } dupTag)
            errors.Add($"The tag \"{dupTag.Key}\" is listed more than once.");
        if (Themes.Any(t => string.IsNullOrWhiteSpace(t.Name))) errors.Add("A theme has no name.");
        if (Tags.Any(t => string.IsNullOrWhiteSpace(t.Name))) errors.Add("A tag has no name.");

        var themeIds = Themes.Select(t => t.Id).ToHashSet();
        var tagIds = Tags.Select(t => t.Id).ToHashSet();
        var items = Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var item in Items)
        {
            var label = string.IsNullOrWhiteSpace(item.Title) ? $"Item {item.Id}" : $"\"{item.Title}\"";
            if (string.IsNullOrWhiteSpace(item.Title)) errors.Add($"Item {item.Id} has no title.");
            if (item.StartDate is { } s && item.EndDate is { } e && e < s) errors.Add($"{label} ends before it starts.");
            if (item.ProgressPct is < 0 or > 100) errors.Add($"{label} has progress outside 0–100.");
            if (item.ManualRank is < 1) errors.Add($"{label} has a manual rank below 1.");
            if (item.ThemeId is { } th && !themeIds.Contains(th)) errors.Add($"{label} refers to theme {th}, which isn't in the file.");
            foreach (var missingTag in (item.TagIds ?? []).Where(t => !tagIds.Contains(t)).Take(1))
                errors.Add($"{label} refers to tag {missingTag}, which isn't in the file.");

            ItemType? parentType = null;
            if (item.ParentId is { } pid)
            {
                if (!items.TryGetValue(pid, out var parent)) { errors.Add($"{label}'s parent ({pid}) isn't in the file."); continue; }
                parentType = parent.Type;
            }
            // Each level sits exactly one below its parent, so this also rules out parent loops.
            if (!HierarchyRules.IsValidParent(item.Type, parentType))
                errors.Add(HierarchyRules.ParentTypeOf(item.Type) is { } expected
                    ? $"{label} is a {item.Type}, so it must belong to a {expected}."
                    : $"{label} is a Goal, so it can't have a parent.");
        }

        var links = new List<(int, int)>();
        foreach (var d in Dependencies)
        {
            if (!items.ContainsKey(d.PredecessorId) || !items.ContainsKey(d.SuccessorId))
                errors.Add($"A dependency refers to item {(items.ContainsKey(d.PredecessorId) ? d.SuccessorId : d.PredecessorId)}, which isn't in the file.");
            else if (DependencyRules.WouldCreateCycle(links, d.PredecessorId, d.SuccessorId))
                errors.Add($"The dependency \"{items[d.PredecessorId].Title}\" → \"{items[d.SuccessorId].Title}\" would create a loop.");
            else links.Add((d.PredecessorId, d.SuccessorId));
        }

        if (Templates.GroupBy(t => t.Name?.Trim() ?? "", StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } dupTemplate)
            errors.Add($"The template \"{dupTemplate.Key}\" is listed more than once.");
        foreach (var t in Templates)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) errors.Add("A template has no name.");
            else if ((t.Lines ?? []).Count == 0) errors.Add($"The template \"{t.Name}\" has no tasks.");
            foreach (var l in t.Lines ?? [])
            {
                if (string.IsNullOrWhiteSpace(l.Title)) errors.Add($"A task in the template \"{t.Name}\" has no title.");
                if (l.DurationDays is < 1) errors.Add($"A task in the template \"{t.Name}\" has a duration below 1 day.");
                foreach (var missingTag in (l.TagIds ?? []).Where(x => !tagIds.Contains(x)).Take(1))
                    errors.Add($"A task in the template \"{t.Name}\" refers to tag {missingTag}, which isn't in the file.");
            }
        }
        if (Snapshots.Any(s => string.IsNullOrWhiteSpace(s.Name))) errors.Add("A snapshot has no name.");

        ValidateRubric(items.Values, errors);

        return errors.Distinct().ToList();
    }

    private void ValidateRubric(IEnumerable<FileItem> items, List<string> errors)
    {
        if (!RubricScale.TryParse(Product.WsjfScale, out var scale, out var scaleError))
            errors.Add($"The WSJF scale isn't valid: {scaleError}");
        Unique(Rubric.Select(c => c.Id), "scoring criterion", errors);
        Unique(Rubric.SelectMany(c => c.Options ?? []).Select(o => o.Id), "scoring choice or band", errors);
        foreach (var c in Rubric)
            if (RubricRules.Validate(c.ToCriterion(0), scale) is { } error) errors.Add($"Scoring rubric: {error}");

        var criteria = Rubric.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var item in items)
        {
            var label = $"\"{item.Title}\"";
            var answers = item.RubricAnswers ?? [];
            if (answers.GroupBy(a => a.CriterionId).FirstOrDefault(g => g.Count() > 1) is { } dup)
                errors.Add($"{label} answers scoring criterion {dup.Key} more than once.");
            foreach (var a in answers)
            {
                if (!criteria.TryGetValue(a.CriterionId, out var c))
                    errors.Add($"{label} answers scoring criterion {a.CriterionId}, which isn't in the file.");
                else if (c.Kind == CriterionKind.StartDate)
                    errors.Add($"{label} has an answer for \"{c.Name}\", which is worked out from the start date.");
                else if (c.Kind == CriterionKind.Choice && !(c.Options ?? []).Any(o => o.Id == a.OptionId))
                    errors.Add($"{label}'s answer for \"{c.Name}\" isn't one of its choices.");
                else if (c.Kind == CriterionKind.NumberBands && a.Number is null)
                    errors.Add($"{label}'s answer for \"{c.Name}\" has no number.");
            }
        }
    }

    private static void Unique(IEnumerable<int> ids, string what, List<string> errors)
    {
        if (ids.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1) is { } dup)
            errors.Add($"More than one {what} has the id {dup.Key}.");
    }
}

public sealed class FileProduct
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ScoringModel ScoringModel { get; set; } = ScoringModel.Rice;
    /// <summary>The WSJF scoring rubric's scale, e.g. "1, 2, 3, 5, 8"; absent means the default.</summary>
    public string? WsjfScale { get; set; }
}

public sealed record FileTheme(int Id, string Name, string Color);

public sealed record FileTag(int Id, string Name);

public sealed class FileItem
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public ItemType Type { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public ItemStatus Status { get; set; }
    public int ProgressPct { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Horizon? Horizon { get; set; }
    public string? Owner { get; set; }
    /// <summary>Absent in files from before 1.2.</summary>
    public string? JiraKey { get; set; }
    public string? ExternalUrl { get; set; }
    public int? ThemeId { get; set; }
    public List<int>? TagIds { get; set; }
    /// <summary>Position among siblings; items are re-numbered in this order on import.</summary>
    public int SortOrder { get; set; }
    public int? ManualRank { get; set; }
    public bool IsRollupOverridden { get; set; }
    public FileScore? Score { get; set; }
    /// <summary>WSJF factors typed by hand, which the rubric leaves alone. Absent in older files: every WSJF value present counts as typed.</summary>
    public WsjfFactors? ManualFactors { get; set; }
    public List<FileRubricAnswer>? RubricAnswers { get; set; }
    public string? SourceTemplateName { get; set; }
    public DateTime? SourceTemplateAppliedAt { get; set; }
    public string? TemplateBatchId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Scoring inputs for every model. The score itself isn't stored; it's recalculated on import.</summary>
public sealed record FileScore(
    double? Reach, double? Impact, double? Confidence, double? Effort,
    double? BusinessValue, double? TimeCriticality, double? RiskReduction, double? JobSize,
    MoscowPriority? Moscow);

public sealed record FileDependency(int PredecessorId, int SuccessorId);

public sealed class FileTemplate
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<FileTemplateLine> Lines { get; set; } = [];
}

/// <summary>A template task line. <see cref="GapDays"/> is the line's gap after the previous task (see <see cref="TemplateRules.Schedule"/>).</summary>
public sealed record FileTemplateLine(string Title, string? Description, string? Owner, int? GapDays, int? DurationDays, List<int>? TagIds);

/// <summary>A saved baseline. Item ids refer to <see cref="RoadmapFile.Items"/>; items deleted since keep ids that match nothing.</summary>
public sealed class FileSnapshot
{
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public DateTime TakenAt { get; set; }
    public List<SnapshotItem> Items { get; set; } = [];
}

/// <summary>One change-history row. ItemId is null when the item no longer exists.</summary>
public sealed record FileHistoryEntry(
    DateTime ChangedAt, AuditAction Action, int? ItemId, string? ItemTitle, string? Field, string? OldValue, string? NewValue);

/// <summary>A WSJF scoring rubric criterion. Option ids are local to the file, like item ids.</summary>
public sealed class FileCriterion
{
    public int Id { get; set; }
    public WsjfFactor Factor { get; set; }
    public CriterionRole Role { get; set; }
    public CriterionKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Help { get; set; }
    public List<FileCriterionOption> Options { get; set; } = [];

    public ScoringCriterion ToCriterion(int productId) => new()
    {
        ProductId = productId, Factor = Factor, Role = Role, Kind = Kind, Name = Name?.Trim() ?? "",
        Help = string.IsNullOrWhiteSpace(Help) ? null : Help.Trim(),
        Options = (Options ?? []).Select((o, i) => new ScoringCriterionOption
        {
            SortOrder = i, Label = string.IsNullOrWhiteSpace(o.Label) ? null : o.Label.Trim(),
            From = Kind == CriterionKind.Choice ? null : o.From, Value = o.Value,
        }).ToList(),
    };
}

/// <summary>A band (<see cref="From"/>) or choice (<see cref="Label"/>); <see cref="Value"/> is a score, or steps for an adjuster.</summary>
public sealed record FileCriterionOption(int Id, string? Label, double? From, double Value);

/// <summary>An item's answer: <see cref="Number"/> for number bands, <see cref="OptionId"/> for a pick-list.</summary>
public sealed record FileRubricAnswer(int CriterionId, double? Number, int? OptionId);
