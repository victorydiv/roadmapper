using Roadmapper.Core;

namespace Roadmapper.Tests;

public class HierarchyRulesTests
{
    [Theory]
    [InlineData(ItemType.Goal, null, true)]
    [InlineData(ItemType.Initiative, ItemType.Goal, true)]
    [InlineData(ItemType.Feature, ItemType.Initiative, true)]
    [InlineData(ItemType.Task, ItemType.Feature, true)]
    [InlineData(ItemType.Goal, ItemType.Goal, false)]
    [InlineData(ItemType.Initiative, null, false)]
    [InlineData(ItemType.Task, ItemType.Initiative, false)]
    [InlineData(ItemType.Feature, ItemType.Task, false)]
    public void IsValidParent(ItemType child, ItemType? parent, bool expected) =>
        Assert.Equal(expected, HierarchyRules.IsValidParent(child, parent));

    [Fact]
    public void Validate_explains_the_expected_parent()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HierarchyRules.Validate(ItemType.Task, ItemType.Goal));
        Assert.Contains("Feature", ex.Message);
    }
}

public class RollupServiceTests
{
    private static RoadmapItem Item(int id, ItemType type, int? parent, string? start = null, string? end = null,
        int progress = 0, ItemStatus status = ItemStatus.Planned) => new()
    {
        Id = id, ItemType = type, ParentId = parent, ProgressPct = progress, Status = status,
        StartDate = start is null ? null : DateOnly.Parse(start),
        EndDate = end is null ? null : DateOnly.Parse(end),
    };

    [Fact]
    public void Rolls_dates_and_progress_up_through_every_level()
    {
        var goal = Item(1, ItemType.Goal, null);
        var initiative = Item(2, ItemType.Initiative, 1);
        var feature = Item(3, ItemType.Feature, 2);
        var t1 = Item(4, ItemType.Task, 3, "2026-01-05", "2026-01-20", 100);
        var t2 = Item(5, ItemType.Task, 3, "2026-01-15", "2026-02-10", 0);

        // Deliberately unordered to prove the bottom-up pass doesn't depend on input order.
        RollupService.Apply([goal, t2, initiative, t1, feature]);

        foreach (var parent in new[] { feature, initiative, goal })
        {
            Assert.Equal(new DateOnly(2026, 1, 5), parent.StartDate);
            Assert.Equal(new DateOnly(2026, 2, 10), parent.EndDate);
            Assert.Equal(50, parent.ProgressPct);
        }
    }

    [Fact]
    public void Ignores_cancelled_children()
    {
        var feature = Item(1, ItemType.Feature, null);
        var done = Item(2, ItemType.Task, 1, "2026-03-01", "2026-03-10", 100);
        var cancelled = Item(3, ItemType.Task, 1, "2026-01-01", "2026-12-31", 0, ItemStatus.Cancelled);

        RollupService.Apply([feature, done, cancelled]);

        Assert.Equal(100, feature.ProgressPct);
        Assert.Equal(new DateOnly(2026, 3, 1), feature.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 10), feature.EndDate);
    }

    [Fact]
    public void Overridden_parent_and_leaves_keep_their_values()
    {
        var feature = Item(1, ItemType.Feature, null, "2026-06-01", "2026-06-30", 10);
        feature.IsRollupOverridden = true;
        var task = Item(2, ItemType.Task, 1, "2026-01-01", "2026-01-02", 90);

        RollupService.Apply([feature, task]);

        Assert.Equal(new DateOnly(2026, 6, 1), feature.StartDate);
        Assert.Equal(10, feature.ProgressPct);
        Assert.Equal(90, task.ProgressPct);
    }

    [Fact]
    public void Keeps_parent_dates_when_no_child_has_dates()
    {
        var feature = Item(1, ItemType.Feature, null, "2026-06-01", "2026-06-30");
        var task = Item(2, ItemType.Task, 1, progress: 40);

        RollupService.Apply([feature, task]);

        Assert.Equal(new DateOnly(2026, 6, 1), feature.StartDate);
        Assert.Equal(40, feature.ProgressPct);
    }
}

public class ScoringTests
{
    [Fact]
    public void Rice() =>
        Assert.Equal(640, new RiceStrategy().Compute(new ItemScore { Reach = 800, Impact = 2, Confidence = 80, Effort = 2 }));

    [Fact]
    public void Wsjf() =>
        Assert.Equal(4, new WsjfStrategy().Compute(new ItemScore { BusinessValue = 8, TimeCriticality = 5, RiskReduction = 3, JobSize = 4 }));

    [Fact]
    public void Moscow() =>
        Assert.Equal(4, new MoscowStrategy().Compute(new ItemScore { Moscow = MoscowPriority.MustHave }));

    [Fact]
    public void Missing_or_zero_inputs_give_no_score()
    {
        Assert.Null(new RiceStrategy().Compute(new ItemScore { Reach = 1, Impact = 1, Confidence = 100 }));
        Assert.Null(new RiceStrategy().Compute(new ItemScore { Reach = 1, Impact = 1, Confidence = 100, Effort = 0 }));
        Assert.Null(new WsjfStrategy().Compute(new ItemScore { BusinessValue = 1, TimeCriticality = 1, RiskReduction = 1, JobSize = 0 }));
        Assert.Null(new MoscowStrategy().Compute(new ItemScore()));
    }

    [Fact]
    public void Rank_order_puts_manual_ranks_first_then_score_then_unscored()
    {
        RoadmapItem Scored(int id, double? score, int? rank = null) =>
            new() { Id = id, SortOrder = id, ManualRank = rank, Score = new ItemScore { ComputedScore = score } };

        var ordered = new[] { Scored(1, null), Scored(2, 5), Scored(3, 50), Scored(4, 1, rank: 2), Scored(5, 0, rank: 1) }
            .InRankOrder().Select(i => i.Id);

        Assert.Equal([5, 4, 3, 2, 1], ordered);
    }
}

public class DependencyRulesTests
{
    [Fact]
    public void Detects_self_direct_and_transitive_cycles()
    {
        (int, int)[] existing = [(1, 2), (2, 3)];

        Assert.True(DependencyRules.WouldCreateCycle(existing, 1, 1));
        Assert.True(DependencyRules.WouldCreateCycle(existing, 2, 1));
        Assert.True(DependencyRules.WouldCreateCycle(existing, 3, 1));
        Assert.False(DependencyRules.WouldCreateCycle(existing, 1, 3));
        Assert.False(DependencyRules.WouldCreateCycle(existing, 3, 4));
    }

    [Fact]
    public void Schedule_conflict_when_successor_starts_on_or_before_predecessor_end()
    {
        var pred = new RoadmapItem { EndDate = new DateOnly(2026, 3, 10) };

        Assert.True(DependencyRules.IsScheduleConflict(pred, new RoadmapItem { StartDate = new DateOnly(2026, 3, 10) }));
        Assert.False(DependencyRules.IsScheduleConflict(pred, new RoadmapItem { StartDate = new DateOnly(2026, 3, 11) }));
        Assert.False(DependencyRules.IsScheduleConflict(pred, new RoadmapItem()));
    }
}
