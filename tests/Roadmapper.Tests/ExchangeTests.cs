using Roadmapper.Core;
using Roadmapper.Core.Exchange;

namespace Roadmapper.Tests;

public class CsvTests
{
    [Fact]
    public void Parses_quotes_embedded_delimiters_newlines_and_bom()
    {
        var rows = Csv.Parse("﻿Title,Description\r\n\"Hello, world\",\"Line 1\nLine \"\"2\"\"\"\r\nPlain,\r\n\r\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["Title", "Description"], rows[0]);
        Assert.Equal(["Hello, world", "Line 1\nLine \"2\""], rows[1]);
        Assert.Equal(["Plain", ""], rows[2]);
    }

    [Fact]
    public void Detects_semicolon_delimiter() =>
        Assert.Equal(["a", "b,c"], Csv.Parse("x;y\na;b,c")[1]);

    [Fact]
    public void Write_then_parse_round_trips()
    {
        string[][] original = [["A", "B"], ["with, comma", "with \"quote\""], ["multi\nline", ""]];
        Assert.Equal(original, Csv.Parse(Csv.Write(original)));
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("Normal", "Normal")]
    public void Formula_protection_round_trips(string raw, string protectedValue)
    {
        Assert.Equal(protectedValue, Csv.ProtectFormula(raw));
        Assert.Equal(raw, Csv.UnprotectFormula(Csv.ProtectFormula(raw)));
    }
}

public class ImportPlannerTests
{
    private static readonly ProductRoadmap Empty = new(new Product { Id = 1 }, [], []);

    private static ProductRoadmap WithExisting()
    {
        var goal = new RoadmapItem { Id = 10, ProductId = 1, ItemType = ItemType.Goal, Title = "Grow" };
        var init = new RoadmapItem { Id = 11, ProductId = 1, ItemType = ItemType.Initiative, Title = "Checkout", ParentId = 10 };
        var init2 = new RoadmapItem { Id = 12, ProductId = 1, ItemType = ItemType.Initiative, Title = "Dup", ParentId = 10 };
        var goal2 = new RoadmapItem { Id = 13, ProductId = 1, ItemType = ItemType.Goal, Title = "Retain" };
        var init3 = new RoadmapItem { Id = 14, ProductId = 1, ItemType = ItemType.Initiative, Title = "Dup", ParentId = 13 };
        return new ProductRoadmap(new Product { Id = 1 }, [goal, init, init2, goal2, init3], []);
    }

    private static ImportRow Row(int n, params (string Field, string Value)[] values) =>
        new(n, values.ToDictionary(v => v.Field, v => v.Value));

    [Fact]
    public void Links_rows_to_each_other_and_infers_types()
    {
        var plans = ImportPlanner.Plan(
        [
            Row(2, ("Title", "Grow")),
            Row(3, ("Title", "Checkout"), ("Parent", "Grow")),
            Row(4, ("Title", "One-page"), ("Parent", "Grow › Checkout")),
            Row(5, ("Title", "Build form"), ("Parent", "Grow > Checkout > One-page")),
        ], Empty);

        Assert.All(plans, p => Assert.True(p.IsValid, string.Join("; ", p.Errors)));
        Assert.Equal([ItemType.Goal, ItemType.Initiative, ItemType.Feature, ItemType.Task], plans.Select(p => p.Draft.ItemType));
        Assert.Equal([null, 2, 3, 4], plans.Select(p => p.ParentRow));
    }

    [Fact]
    public void Resolves_existing_parents_by_path_or_unique_title()
    {
        var plans = ImportPlanner.Plan(
        [
            Row(2, ("Title", "F1"), ("Parent", "Checkout")),
            Row(3, ("Title", "F2"), ("Parent", "retain › dup")),
            Row(4, ("Title", "F3"), ("Parent", "Dup")),
            Row(5, ("Title", "F4"), ("Parent", "Nowhere")),
        ], WithExisting());

        Assert.Equal(11, plans[0].Draft.ParentId);
        Assert.Equal(ItemType.Feature, plans[0].Draft.ItemType);
        Assert.Equal(14, plans[1].Draft.ParentId);
        Assert.Contains(plans[2].Errors, e => e.Contains("matches 2 existing items"));
        Assert.Contains(plans[3].Errors, e => e.Contains("wasn't found"));
    }

    [Fact]
    public void Reports_hierarchy_and_field_errors_and_cascades_to_children()
    {
        var plans = ImportPlanner.Plan(
        [
            Row(2, ("Type", "Task"), ("Title", "Orphan task")),
            Row(3, ("Type", "Initiative"), ("Title", "Bad dates"), ("Parent", "Grow"), ("Start", "2026-05-01"), ("End", "2026-04-01")),
            Row(4, ("Title", "Child of bad"), ("Parent", "Bad dates")),
            Row(5, ("Title", "Bad fields"), ("Status", "Someday"), ("Progress", "150"), ("Start", "not a date"), ("Reach", "-3")),
            Row(6, ("Title", "")),
        ], WithExisting());

        Assert.Contains(plans[0].Errors, e => e.Contains("needs a Feature"));
        Assert.Contains(plans[1].Errors, e => e.Contains("End is before Start"));
        Assert.Contains(plans[2].Errors, e => e.Contains("parent (row 3) has errors"));
        Assert.Equal(4, plans[3].Errors.Count);
        Assert.Contains(plans[4].Errors, e => e.Contains("Title is required"));
    }

    [Fact]
    public void Parses_friendly_values_and_warns_about_duplicates()
    {
        var plan = Assert.Single(ImportPlanner.Plan(
        [
            Row(2, ("Title", "Checkout"), ("Parent", "Grow"), ("Status", "in progress"), ("Progress", "40%"),
                ("Start", "1/15/2026"), ("End", "Feb 20, 2026"), ("Horizon", "now"), ("MoSCoW", "Must have"),
                ("Tags", "a; b, a"), ("Theme", "Growth"), ("Manual rank", "2")),
        ], WithExisting()));

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(ItemStatus.InProgress, plan.Draft.Status);
        Assert.Equal(40, plan.Draft.ProgressPct);
        Assert.Equal(new DateOnly(2026, 1, 15), plan.Draft.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 20), plan.Draft.EndDate);
        Assert.Equal(Horizon.Now, plan.Draft.Horizon);
        Assert.Equal(MoscowPriority.MustHave, plan.Draft.Score.Moscow);
        Assert.Equal(["a", "b"], plan.TagNames);
        Assert.Equal("Growth", plan.ThemeName);
        Assert.Equal(2, plan.Draft.ManualRank);
        Assert.Contains(plan.Warnings, w => w.Contains("already exists"));
    }

    [Fact]
    public void Header_guessing_handles_aliases() =>
        Assert.Equal(["Title", "End", "Owner", "Parent", null], new[] { "Name", "Due date", "Assignee", "parent", "Whatever" }.Select(RoadmapColumns.Guess));
}

public class SnapshotComparerTests
{
    [Fact]
    public void Classifies_added_removed_changed_and_slips()
    {
        var product = new Product { Id = 1 };
        var a = new RoadmapItem { Id = 1, ItemType = ItemType.Goal, Title = "A", EndDate = new DateOnly(2026, 3, 10), Status = ItemStatus.Done };
        var b = new RoadmapItem { Id = 2, ItemType = ItemType.Goal, Title = "B", Status = ItemStatus.Planned };
        var c = new RoadmapItem { Id = 3, ItemType = ItemType.Goal, Title = "C (new)" };
        var current = new ProductRoadmap(product, [a, b, c], []);

        SnapshotItem Snap(int id, string title, DateOnly? end = null, ItemStatus status = ItemStatus.Planned) =>
            new(id, null, ItemType.Goal, title, status, 0, null, end, null, null, null);
        var diff = SnapshotComparer.Compare(
            [Snap(1, "A", new DateOnly(2026, 3, 1)), Snap(2, "B", status: ItemStatus.Idea), Snap(9, "Gone")], current);

        Assert.Equal(DiffKind.Changed, diff.Items.Single(d => d.Id == 1).Kind);
        Assert.Equal(9, diff.Items.Single(d => d.Id == 1).EndSlipDays);
        Assert.Equal(DiffKind.Changed, diff.Items.Single(d => d.Id == 2).Kind);
        Assert.Equal(DiffKind.Added, diff.Items.Single(d => d.Id == 3).Kind);
        Assert.Equal(DiffKind.Removed, diff.Items.Single(d => d.Id == 9).Kind);
        Assert.Equal((1, 1, 1, 1), (diff.Slipped, diff.Added, diff.Removed, diff.Completed));
    }
}
