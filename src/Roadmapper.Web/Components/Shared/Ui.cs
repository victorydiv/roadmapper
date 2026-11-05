using Roadmapper.Core;

namespace Roadmapper.Web.Components.Shared;

/// <summary>Display helpers shared by the views.</summary>
public static class Ui
{
    public static string Label(ItemStatus s) => s == ItemStatus.InProgress ? "In progress" : s.ToString();

    public static string Label(MoscowPriority m) => m switch
    {
        MoscowPriority.MustHave => "Must have",
        MoscowPriority.ShouldHave => "Should have",
        MoscowPriority.CouldHave => "Could have",
        _ => "Won't have",
    };

    public static string Label(ScoringModel m) => Scoring.For(m).DisplayName;

    public static string TypeColor(ItemType t) => RoadmapColors.TypeColor(t);

    /// <summary>Theme color when the item has one, otherwise the item-type color.</summary>
    public static string ColorOf(RoadmapItem i) => RoadmapColors.ColorOf(i);

    public static string ScoreText(RoadmapItem i, ScoringModel model)
    {
        if (i.Score?.ComputedScore is not { } score) return "–";
        return model == ScoringModel.Moscow ? Label((MoscowPriority)(int)score) : score.ToString("0.#");
    }

    public static string DateRange(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        (null, null) => "",
        ({ } s, null) => $"{s:MMM d} →",
        (null, { } e) => $"→ {e:MMM d}",
        ({ } s, { } e) when s.Year == e.Year => $"{s:MMM d} – {e:MMM d, yyyy}",
        ({ } s, { } e) => $"{s:MMM d, yyyy} – {e:MMM d, yyyy}",
    };
}
