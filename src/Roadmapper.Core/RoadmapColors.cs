namespace Roadmapper.Core;

/// <summary>Bar and badge colors shared by the UI and every export, so they all match.</summary>
public static class RoadmapColors
{
    public static string TypeColor(ItemType t) => t switch
    {
        ItemType.Goal => "#7c4dcc",
        ItemType.Initiative => "#3f5bd9",
        ItemType.Feature => "#128a8a",
        _ => "#6b7280",
    };

    /// <summary>Theme color when the item has one, otherwise the item-type color.</summary>
    public static string ColorOf(RoadmapItem i) => i.Theme?.Color ?? TypeColor(i.ItemType);

    public const string Baseline = "#9aa3b2";
    public const string Dependency = "#9aa3b2";
    public const string Conflict = "#c8372d";
    public const string Blocked = "#d03b3b";
}
