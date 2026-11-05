using System.Globalization;

namespace Roadmapper.Core.Exchange;

/// <summary>One spreadsheet row, keyed by import field (see <see cref="RoadmapColumns"/>). RowNumber is 1-based as shown in Excel.</summary>
public record ImportRow(int RowNumber, IReadOnlyDictionary<string, string> Values)
{
    public string Get(string field) => Values.TryGetValue(field, out var v) ? Csv.UnprotectFormula(v.Trim()) : "";
}

/// <summary>A row turned into an item to create, or the reasons it can't be.</summary>
public class PlannedItem
{
    public required int RowNumber { get; init; }
    public required ItemDraft Draft { get; init; }
    /// <summary>Set when the parent is another row in the same file (created first).</summary>
    public int? ParentRow { get; set; }
    public string? ParentDisplay { get; set; }
    public string? ThemeName { get; set; }
    public List<string> TagNames { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

public static class ImportPlanner
{
    /// <summary>
    /// Validates rows against each other and the product's existing items. Parents are matched by
    /// full path, falling back to a bare title; rows in the file win over existing items. A blank
    /// Type is inferred from the parent (one level down), or Goal when there's no parent.
    /// </summary>
    public static List<PlannedItem> Plan(IReadOnlyList<ImportRow> rows, ProductRoadmap existing)
    {
        var plans = rows.Select(r => ParseFields(r, existing)).ToList();
        var byRow = plans.ToDictionary(p => p.RowNumber);

        // Every candidate parent, keyed by normalized full path.
        var existingNodes = existing.Items.Select(i => (Key: Key(Join(existing.PathOf(i), i.Title)), Item: i)).ToList();
        var rowKeys = rows.ToDictionary(r => r.RowNumber, r => Key(Join(r.Get(RoadmapColumns.Parent), r.Get(RoadmapColumns.Title))));

        foreach (var row in rows)
        {
            var plan = byRow[row.RowNumber];
            if (rowKeys[row.RowNumber] is var self && existingNodes.Any(n => n.Key == self) && self.Length > 0)
                plan.Warnings.Add("An item with this path already exists; this creates a second one.");

            var parent = row.Get(RoadmapColumns.Parent);
            if (parent.Length == 0) continue;
            var key = Key(parent);
            plan.ParentDisplay = parent;

            var rowMatches = rowKeys.Where(k => k.Key != row.RowNumber && Matches(k.Value, key)).Select(k => k.Key).ToList();
            if (rowMatches.Count == 1) { plan.ParentRow = rowMatches[0]; continue; }
            if (rowMatches.Count > 1) { plan.Errors.Add($"Parent \"{parent}\" matches rows {string.Join(", ", rowMatches)}; use the full path."); continue; }

            var itemMatches = existingNodes.Where(n => Matches(n.Key, key)).Select(n => n.Item).ToList();
            if (itemMatches.Count == 1) plan.Draft.ParentId = itemMatches[0].Id;
            else if (itemMatches.Count > 1) plan.Errors.Add($"Parent \"{parent}\" matches {itemMatches.Count} existing items; use the full path.");
            else plan.Errors.Add($"Parent \"{parent}\" wasn't found in the file or the product.");
        }

        // Types depend on parents' types, which may themselves be inferred: memoize, and treat a
        // circular chain of parent references as unknown.
        var declaredType = rows.ToDictionary(r => r.RowNumber, r => r.Get(RoadmapColumns.Type).Length > 0);
        var types = new Dictionary<int, ItemType?>();
        var visiting = new HashSet<int>();
        ItemType? TypeOf(PlannedItem p)
        {
            if (types.TryGetValue(p.RowNumber, out var known)) return known;
            if (!visiting.Add(p.RowNumber)) return null;
            ItemType? result;
            if (declaredType[p.RowNumber]) result = p.Errors.Any(e => e.StartsWith("Type ")) ? null : p.Draft.ItemType;
            else if (p.ParentRow is null && p.Draft.ParentId is null) result = p.Errors.Any(e => e.StartsWith("Parent ")) ? null : ItemType.Goal;
            else result = ParentType(p) is { } pt ? HierarchyRules.ChildTypeOf(pt) : null;
            visiting.Remove(p.RowNumber);
            types[p.RowNumber] = result;
            return result;
        }
        ItemType? ParentType(PlannedItem p) =>
            p.ParentRow is { } r ? TypeOf(byRow[r]) : p.Draft.ParentId is { } id ? existing.Find(id)?.ItemType : null;

        foreach (var plan in plans)
        {
            var type = TypeOf(plan);
            if (type is null)
            {
                if (plan.IsValid) plan.Errors.Add("Couldn't work out the type: the parent is a Task, or its own type is unknown.");
                continue;
            }
            plan.Draft.ItemType = type.Value;
            var parentType = ParentType(plan);
            // An unresolved parent row is reported on that row and cascades below.
            if ((plan.ParentRow is not null || plan.Draft.ParentId is not null) && parentType is null) continue;
            if (!HierarchyRules.IsValidParent(type.Value, parentType))
            {
                var expected = HierarchyRules.ParentTypeOf(type.Value);
                plan.Errors.Add(expected is null
                    ? $"A Goal can't have a parent ({parentType} given)."
                    : $"A {type} needs a {expected} as its parent{(parentType is null ? "" : $", not a {parentType}")}.");
            }
        }

        // A row whose parent row fails can't be created either; propagate until stable.
        bool changed;
        do
        {
            changed = false;
            foreach (var plan in plans.Where(p => p.IsValid && p.ParentRow is { } r && !byRow[r].IsValid))
            {
                plan.Errors.Add($"Its parent (row {plan.ParentRow}) has errors.");
                changed = true;
            }
        } while (changed);

        return plans;
    }

    private static PlannedItem ParseFields(ImportRow row, ProductRoadmap existing)
    {
        var d = ItemDraft.New(existing.Product.Id, ItemType.Goal, null);
        var plan = new PlannedItem { RowNumber = row.RowNumber, Draft = d };
        void Error(string field, string value, string expected) => plan.Errors.Add($"{field} \"{value}\" isn't valid; expected {expected}.");

        d.Title = row.Get(RoadmapColumns.Title);
        if (d.Title.Length == 0) plan.Errors.Add("Title is required.");
        d.Description = NullIfEmpty(row.Get(RoadmapColumns.Description));
        d.Owner = NullIfEmpty(row.Get(RoadmapColumns.Owner));
        plan.ThemeName = NullIfEmpty(row.Get(RoadmapColumns.Theme));
        plan.TagNames.AddRange(row.Get(RoadmapColumns.Tags).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));

        if (row.Get(RoadmapColumns.Type) is { Length: > 0 } type)
        {
            if (ParseEnum<ItemType>(type) is { } t) d.ItemType = t;
            else Error("Type", type, "Goal, Initiative, Feature or Task");
        }
        if (row.Get(RoadmapColumns.Status) is { Length: > 0 } status)
        {
            if (ParseEnum<ItemStatus>(status) is { } s) d.Status = s;
            else Error("Status", status, "Idea, Planned, In progress, Done or Cancelled");
        }
        if (row.Get(RoadmapColumns.Horizon) is { Length: > 0 } horizon)
        {
            if (ParseEnum<Horizon>(horizon) is { } h) d.Horizon = h;
            else Error("Horizon", horizon, "Now, Next or Later");
        }
        if (row.Get(RoadmapColumns.Progress) is { Length: > 0 } progress)
        {
            if (ParseNumber(progress.TrimEnd('%')) is { } p && p is >= 0 and <= 100) d.ProgressPct = (int)Math.Round(p);
            else Error("Progress", progress, "a number from 0 to 100");
        }
        d.StartDate = ParseDate(row, RoadmapColumns.Start, Error);
        d.EndDate = ParseDate(row, RoadmapColumns.End, Error);
        if (d.StartDate is { } sd && d.EndDate is { } ed && ed < sd) plan.Errors.Add("End is before Start.");

        if (row.Get(RoadmapColumns.ManualRank) is { Length: > 0 } rank)
        {
            if (int.TryParse(rank, out var r) && r >= 1) d.ManualRank = r;
            else Error("Manual rank", rank, "a whole number of 1 or more");
        }

        double? Num(string field)
        {
            var v = row.Get(field);
            if (v.Length == 0) return null;
            if (ParseNumber(v.TrimEnd('%')) is { } n && n >= 0) return n;
            Error(field, v, "a non-negative number");
            return null;
        }
        d.Score.Reach = Num(RoadmapColumns.Reach);
        d.Score.Impact = Num(RoadmapColumns.Impact);
        d.Score.Confidence = Num(RoadmapColumns.Confidence);
        d.Score.Effort = Num(RoadmapColumns.Effort);
        d.Score.BusinessValue = Num(RoadmapColumns.BusinessValue);
        d.Score.TimeCriticality = Num(RoadmapColumns.TimeCriticality);
        d.Score.RiskReduction = Num(RoadmapColumns.RiskReduction);
        d.Score.JobSize = Num(RoadmapColumns.JobSize);
        if (row.Get(RoadmapColumns.Moscow) is { Length: > 0 } moscow)
        {
            if (ParseMoscow(moscow) is { } m) d.Score.Moscow = m;
            else Error("MoSCoW", moscow, "Must, Should, Could or Won't");
        }
        return plan;
    }

    // ---- Parsing helpers ----

    private static string Join(string parentPath, string title) =>
        parentPath.Length == 0 ? title : parentPath + RoadmapColumns.PathSeparator + title;

    /// <summary>Case-insensitive path key; accepts "›" or ">" as the separator.</summary>
    private static string Key(string path) =>
        string.Join("›", path.Split(['›', '>'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>A full path matches exactly; a single title matches any node with that last segment.</summary>
    private static bool Matches(string nodeKey, string wanted) =>
        nodeKey == wanted || (!wanted.Contains('›') && nodeKey.Split('›')[^1] == wanted);

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static T? ParseEnum<T>(string s) where T : struct, Enum
    {
        var compact = new string(s.Where(char.IsLetter).ToArray());
        return Enum.TryParse<T>(compact, ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : null;
    }

    private static MoscowPriority? ParseMoscow(string s) =>
        new string(s.Where(char.IsLetter).ToArray()).ToLowerInvariant() switch
        {
            "m" or "must" or "musthave" => MoscowPriority.MustHave,
            "s" or "should" or "shouldhave" => MoscowPriority.ShouldHave,
            "c" or "could" or "couldhave" => MoscowPriority.CouldHave,
            "w" or "wont" or "wonthave" => MoscowPriority.WontHave,
            _ => null,
        };

    private static double? ParseNumber(string s) =>
        double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v) ? v
        : double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out v) ? v
        : null;

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "M/d/yy", "MMM d, yyyy", "d MMM yyyy", "MMMM d, yyyy"];

    private static DateOnly? ParseDate(ImportRow row, string field, Action<string, string, string> error)
    {
        var v = row.Get(field);
        if (v.Length == 0) return null;
        if (DateOnly.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(v, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt)) return DateOnly.FromDateTime(dt);
        error(field, v, "a date like 2026-03-31");
        return null;
    }
}
