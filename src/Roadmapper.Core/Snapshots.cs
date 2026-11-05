namespace Roadmapper.Core;

/// <summary>A saved baseline. The roadmap state lives in <see cref="PayloadJson"/> (a list of <see cref="SnapshotItem"/>).</summary>
public class Snapshot
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;
    public int ItemCount { get; set; }
    public string PayloadJson { get; set; } = "[]";
}

/// <summary>An item as it was when the snapshot was taken. Ids link it back to the live item.</summary>
public record SnapshotItem(
    int Id, int? ParentId, ItemType ItemType, string Title, ItemStatus Status, int ProgressPct,
    DateOnly? StartDate, DateOnly? EndDate, Horizon? Horizon, string? Owner, double? Score)
{
    public static SnapshotItem From(RoadmapItem i) => new(
        i.Id, i.ParentId, i.ItemType, i.Title, i.Status, i.ProgressPct,
        i.StartDate, i.EndDate, i.Horizon, i.Owner, i.Score?.ComputedScore);
}

public enum DiffKind { Unchanged, Changed, Added, Removed }

public record ItemDiff(int Id, ItemType ItemType, DiffKind Kind, SnapshotItem? Baseline, RoadmapItem? Current)
{
    public string Title => Current?.Title ?? Baseline!.Title;
    /// <summary>Positive = later than planned.</summary>
    public int? StartSlipDays => Days(Baseline?.StartDate, Current?.StartDate);
    /// <summary>Positive = finishing later than planned.</summary>
    public int? EndSlipDays => Days(Baseline?.EndDate, Current?.EndDate);
    public bool StatusChanged => Baseline is not null && Current is not null && Baseline.Status != Current.Status;
    public int? ProgressDelta => Baseline is not null && Current is not null ? Current.ProgressPct - Baseline.ProgressPct : null;
    public bool HorizonChanged => Baseline is not null && Current is not null && Baseline.Horizon != Current.Horizon;
    public bool Renamed => Baseline is not null && Current is not null && Baseline.Title != Current.Title;
    /// <summary>Dates added or removed entirely (as opposed to moved).</summary>
    public bool DatesSetOrCleared => Baseline is not null && Current is not null &&
        (Baseline.EndDate is null != Current.EndDate is null || Baseline.StartDate is null != Current.StartDate is null);

    private static int? Days(DateOnly? before, DateOnly? after) =>
        before is { } b && after is { } a ? a.DayNumber - b.DayNumber : null;
}

public record SnapshotDiff(IReadOnlyList<ItemDiff> Items)
{
    public int Added => Items.Count(i => i.Kind == DiffKind.Added);
    public int Removed => Items.Count(i => i.Kind == DiffKind.Removed);
    public int Changed => Items.Count(i => i.Kind == DiffKind.Changed);
    public int Slipped => Items.Count(i => i.EndSlipDays > 0);
    public int PulledIn => Items.Count(i => i.EndSlipDays < 0);
    public int Completed => Items.Count(i => i.StatusChanged && i.Current!.Status == ItemStatus.Done);
}

public static class SnapshotComparer
{
    /// <summary>Matches items by id; output follows the current tree order, with removed items last.</summary>
    public static SnapshotDiff Compare(IReadOnlyList<SnapshotItem> baseline, ProductRoadmap current)
    {
        var before = baseline.ToDictionary(b => b.Id);
        var diffs = new List<ItemDiff>();

        foreach (var row in current.Flatten())
        {
            var item = row.Item;
            if (!before.Remove(item.Id, out var b))
            {
                diffs.Add(new ItemDiff(item.Id, item.ItemType, DiffKind.Added, null, item));
                continue;
            }
            var changed = b.Title != item.Title || b.Status != item.Status || b.ProgressPct != item.ProgressPct ||
                          b.StartDate != item.StartDate || b.EndDate != item.EndDate || b.Horizon != item.Horizon ||
                          b.Owner != item.Owner || b.ParentId != item.ParentId;
            diffs.Add(new ItemDiff(item.Id, item.ItemType, changed ? DiffKind.Changed : DiffKind.Unchanged, b, item));
        }

        diffs.AddRange(before.Values.OrderBy(b => b.ItemType).ThenBy(b => b.Title)
            .Select(b => new ItemDiff(b.Id, b.ItemType, DiffKind.Removed, b, null)));
        return new SnapshotDiff(diffs);
    }
}
