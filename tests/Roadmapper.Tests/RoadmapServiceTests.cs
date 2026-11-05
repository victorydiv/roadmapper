using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Data;
using Roadmapper.Data.Exchange;

namespace Roadmapper.Tests;

/// <summary>Runs the service against a real in-memory SQLite database built from the migrations.</summary>
public sealed class RoadmapServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RoadmapService _svc;

    public RoadmapServiceTests()
    {
        _connection.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        _svc = new RoadmapService(factory);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<int> Add(int productId, ItemType type, int? parent, string title, Action<ItemDraft>? configure = null)
    {
        var d = ItemDraft.New(productId, type, parent);
        d.Title = title;
        configure?.Invoke(d);
        return await _svc.SaveItemAsync(d);
    }

    [Fact]
    public async Task Saving_a_task_rolls_up_to_ancestors_and_scores_items()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var goal = await Add(pid, ItemType.Goal, null, "G");
        var init = await Add(pid, ItemType.Initiative, goal, "I");
        var feat = await Add(pid, ItemType.Feature, init, "F",
            d => d.Score = new ItemScore { Reach = 100, Impact = 1, Confidence = 50, Effort = 1 });
        await Add(pid, ItemType.Task, feat, "T", d =>
        {
            d.StartDate = new DateOnly(2026, 4, 1);
            d.EndDate = new DateOnly(2026, 4, 30);
            d.Status = ItemStatus.Done;
        });

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var g = roadmap.Find(goal)!;
        Assert.Equal(new DateOnly(2026, 4, 1), g.StartDate);
        Assert.Equal(new DateOnly(2026, 4, 30), g.EndDate);
        Assert.Equal(100, g.ProgressPct);
        Assert.Equal(50, roadmap.Find(feat)!.Score!.ComputedScore);
    }

    [Fact]
    public async Task Rejects_items_in_the_wrong_place_in_the_hierarchy()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var goal = await Add(pid, ItemType.Goal, null, "G");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Add(pid, ItemType.Task, goal, "T"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Add(pid, ItemType.Feature, null, "F"));
    }

    [Fact]
    public async Task Switching_scoring_model_recomputes_scores()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var goal = await Add(pid, ItemType.Goal, null, "G", d => d.Score = new ItemScore { Moscow = MoscowPriority.ShouldHave });
        Assert.Null((await _svc.GetRoadmapAsync(pid))!.Find(goal)!.Score!.ComputedScore);

        await _svc.UpdateProductAsync(pid, "P", null, ScoringModel.Moscow);

        Assert.Equal(3, (await _svc.GetRoadmapAsync(pid))!.Find(goal)!.Score!.ComputedScore);
    }

    [Fact]
    public async Task Dependencies_reject_cycles()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var a = await Add(pid, ItemType.Goal, null, "A");
        var b = await Add(pid, ItemType.Goal, null, "B");
        var c = await Add(pid, ItemType.Goal, null, "C");

        await _svc.AddDependencyAsync(a, b);
        await _svc.AddDependencyAsync(b, c);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AddDependencyAsync(c, a));
        Assert.Contains("cycle", ex.Message);
        Assert.Equal(2, (await _svc.GetRoadmapAsync(pid))!.Dependencies.Count);
    }

    [Fact]
    public async Task Deleting_an_item_removes_its_subtree_and_rerolls_the_parent()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var goal = await Add(pid, ItemType.Goal, null, "G");
        var keep = await Add(pid, ItemType.Initiative, goal, "Keep", d => d.ProgressPct = 20);
        var drop = await Add(pid, ItemType.Initiative, goal, "Drop");
        var dropFeature = await Add(pid, ItemType.Feature, drop, "Drop feature", d => d.ProgressPct = 100);
        await _svc.AddDependencyAsync(dropFeature, keep);
        Assert.Equal(60, (await _svc.GetRoadmapAsync(pid))!.Find(goal)!.ProgressPct);

        await _svc.DeleteItemAsync(drop);

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        Assert.Equal([goal, keep], roadmap.Items.Select(i => i.Id).Order());
        Assert.Empty(roadmap.Dependencies);
        Assert.Equal(20, roadmap.Find(goal)!.ProgressPct);
    }

    [Fact]
    public async Task Move_reorders_siblings()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var a = await Add(pid, ItemType.Goal, null, "A");
        var b = await Add(pid, ItemType.Goal, null, "B");

        await _svc.MoveItemAsync(b, -1);

        Assert.Equal([b, a], (await _svc.GetRoadmapAsync(pid))!.ChildrenOf(null).Select(i => i.Id));
    }

    [Fact]
    public async Task Sample_seed_builds_a_valid_roadmap()
    {
        await DbInitializer.SeedSampleAsync(_svc);

        var product = Assert.Single(await _svc.GetProductsAsync());
        var roadmap = (await _svc.GetRoadmapAsync(product.Id))!;
        Assert.Equal(12, roadmap.Items.Count);
        Assert.Equal(2, roadmap.Dependencies.Count);
        Assert.All(roadmap.Items.Where(i => i.ItemType != ItemType.Goal), i => Assert.NotNull(i.ParentId));
    }

    [Fact]
    public async Task Audit_records_user_edits_but_not_derived_rollups()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        await _svc.AddThemeAsync(pid, "Growth", "#000000");
        var themeId = (await _svc.GetRoadmapAsync(pid))!.Product.Themes.Single().Id;
        var goal = await Add(pid, ItemType.Goal, null, "G");
        var init = await Add(pid, ItemType.Initiative, goal, "I");

        var draft = ItemDraft.From((await _svc.GetRoadmapAsync(pid))!.Find(init)!);
        draft.Status = ItemStatus.InProgress;
        draft.ProgressPct = 40; // rolls up into the goal, which must NOT produce a goal entry
        draft.ThemeId = themeId;
        draft.Score.Reach = 10;
        await _svc.SaveItemAsync(draft);

        var initHistory = await _svc.GetActivityAsync(pid, init);
        Assert.Contains(initHistory, a => a.Field == "Status" && a.OldValue == "Idea" && a.NewValue == "In progress");
        Assert.Contains(initHistory, a => a.Field == "Progress" && a.OldValue == "0" && a.NewValue == "40");
        Assert.Contains(initHistory, a => a.Field == "Theme" && a.OldValue == null && a.NewValue == "Growth");
        Assert.Contains(initHistory, a => a.Field == "Reach" && a.NewValue == "10");
        Assert.Contains(initHistory, a => a.Action == AuditAction.Created);

        var goalHistory = await _svc.GetActivityAsync(pid, goal);
        Assert.Equal(AuditAction.Created, Assert.Single(goalHistory).Action);
        Assert.Equal(40, (await _svc.GetRoadmapAsync(pid))!.Find(goal)!.ProgressPct);
    }

    [Fact]
    public async Task Audit_records_board_moves_dependencies_tags_and_deletes()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        await _svc.AddTagAsync(pid, "urgent");
        var tagId = (await _svc.GetRoadmapAsync(pid))!.Product.Tags.Single().Id;
        var a = await Add(pid, ItemType.Goal, null, "A");
        var b = await Add(pid, ItemType.Goal, null, "B");

        await _svc.SetHorizonAsync(a, Horizon.Now);
        await _svc.SetStatusAsync(a, ItemStatus.Done);
        await _svc.AddDependencyAsync(a, b);
        var draft = ItemDraft.From((await _svc.GetRoadmapAsync(pid))!.Find(b)!);
        draft.TagIds.Add(tagId);
        await _svc.SaveItemAsync(draft);
        await _svc.RemoveDependencyAsync((await _svc.GetRoadmapAsync(pid))!.Dependencies.Single().Id);
        await _svc.DeleteItemAsync(a);

        var history = await _svc.GetActivityAsync(pid);
        Assert.Contains(history, e => e.ItemId == a && e.Field == "Horizon" && e.NewValue == "Now");
        Assert.Contains(history, e => e.ItemId == a && e.Field == "Status" && e.NewValue == "Done");
        Assert.Contains(history, e => e.ItemId == a && e.Field == "Progress" && e.NewValue == "100");
        Assert.Contains(history, e => e.ItemId == b && e.Action == AuditAction.Created && e.Field == "Blocked by" && e.NewValue == "A");
        Assert.Contains(history, e => e.ItemId == b && e.Action == AuditAction.Deleted && e.Field == "Blocked by" && e.OldValue == "A");
        Assert.Contains(history, e => e.ItemId == b && e.Field == "Tags" && e.OldValue == "" && e.NewValue == "urgent");
        Assert.Contains(history, e => e.ItemId == a && e.Action == AuditAction.Deleted && e.Field == null && e.ItemTitle == "A");
        // History outlives the deleted item.
        Assert.NotEmpty(await _svc.GetActivityAsync(pid, a));
    }

    [Fact]
    public async Task Moving_to_done_on_kanban_completes_and_rolls_up()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var goal = await Add(pid, ItemType.Goal, null, "G");
        var i1 = await Add(pid, ItemType.Initiative, goal, "I1");
        await Add(pid, ItemType.Initiative, goal, "I2");

        await _svc.SetStatusAsync(i1, ItemStatus.Done);

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        Assert.Equal(100, roadmap.Find(i1)!.ProgressPct);
        Assert.Equal(50, roadmap.Find(goal)!.ProgressPct);
    }

    [Fact]
    public async Task Snapshot_round_trips_and_compares_against_later_changes()
    {
        await DbInitializer.SeedSampleAsync(_svc);
        var pid = (await _svc.GetProductsAsync()).Single().Id;
        var snap = await _svc.TakeSnapshotAsync(pid, "Q1 plan", "agreed");

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var task = roadmap.Items.First(i => i.ItemType == ItemType.Task && i.EndDate is not null);
        var draft = ItemDraft.From(task);
        draft.EndDate = draft.EndDate!.Value.AddDays(14);
        await _svc.SaveItemAsync(draft);

        var (stored, items) = (await _svc.GetSnapshotAsync(snap.Id))!.Value;
        Assert.Equal(("Q1 plan", "agreed", 12), (stored.Name, stored.Note, items.Count));
        Assert.Equal(12, Assert.Single(await _svc.GetSnapshotsAsync(pid)).ItemCount);

        var diff = SnapshotComparer.Compare(items, (await _svc.GetRoadmapAsync(pid))!);
        Assert.Equal(14, diff.Items.Single(d => d.Id == task.Id).EndSlipDays);
        Assert.True(diff.Slipped >= 1); // the task, plus any parents whose end rolled up
    }

    [Fact]
    public async Task Export_then_import_into_a_new_product_reproduces_the_roadmap()
    {
        await DbInitializer.SeedSampleAsync(_svc);
        var source = (await _svc.GetRoadmapAsync((await _svc.GetProductsAsync()).Single().Id))!;
        var target = (await _svc.CreateProductAsync("Copy", null, ScoringModel.Rice)).Id;

        var rows = Csv.Parse(RoadmapExport.ToCsv(source));
        var importRows = rows.Skip(1).Select((cells, i) => new ImportRow(i + 2,
            rows[0].Select((h, c) => (h, c)).Where(x => RoadmapColumns.Guess(x.h) is not null)
                .ToDictionary(x => RoadmapColumns.Guess(x.h)!, x => cells[x.c]))).ToList();
        var plans = ImportPlanner.Plan(importRows, (await _svc.GetRoadmapAsync(target))!);
        Assert.All(plans, p => Assert.True(p.IsValid, string.Join("; ", p.Errors)));

        Assert.Equal(12, await _svc.ImportAsync(target, plans));

        var copy = (await _svc.GetRoadmapAsync(target))!;
        string Shape(ProductRoadmap r) => string.Join("\n", r.Flatten().Select(row =>
            $"{row.Depth}|{row.Item.ItemType}|{row.Item.Title}|{row.Item.Status}|{row.Item.ProgressPct}|{row.Item.StartDate}|{row.Item.EndDate}|{row.Item.Theme?.Name}|{row.Item.Score?.ComputedScore}"));
        Assert.Equal(Shape(source), Shape(copy));
        Assert.Equal(["Growth", "Platform"], copy.Product.Themes.Select(t => t.Name));
    }

    [Fact]
    public async Task Excel_export_reads_back_and_pdf_renders()
    {
        await DbInitializer.SeedSampleAsync(_svc);
        var roadmap = (await _svc.GetRoadmapAsync((await _svc.GetProductsAsync()).Single().Id))!;

        using var xlsx = new MemoryStream(RoadmapExport.ToXlsx(roadmap));
        var rows = TableReader.Read(xlsx, "x.xlsx");
        Assert.Equal(RoadmapColumns.Export, rows[0]);
        Assert.Equal(13, rows.Count);
        var start = Array.IndexOf(RoadmapColumns.Export, RoadmapColumns.Start);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", rows.Skip(1).First(r => r[start].Length > 0)[start]);

        var pdf = RoadmapPdf.Render(roadmap, DateTime.Now);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    private static int PageCount(byte[] pdf) =>
        PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import).PageCount;

    [Fact]
    public async Task Pdf_report_starts_with_a_gantt_page_and_lists_dependencies()
    {
        await DbInitializer.SeedSampleAsync(_svc);
        var pid = (await _svc.GetProductsAsync()).Single().Id;
        var snapshot = await _svc.TakeSnapshotAsync(pid, "Q1 plan", null);

        // Link two overlapping tasks so the conflict styling and the "Starts Nd before" note are exercised.
        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var tasks = roadmap.Items.Where(i => i.ItemType == ItemType.Task && i.StartDate is not null && i.EndDate is not null).ToList();
        var (a, b) = tasks.SelectMany(x => tasks, (x, y) => (x, y))
            .First(p => p.x.Id != p.y.Id && p.y.StartDate > p.x.StartDate && p.y.StartDate <= p.x.EndDate);
        await _svc.AddDependencyAsync(a.Id, b.Id);
        roadmap = (await _svc.GetRoadmapAsync(pid))!;
        Assert.Contains(roadmap.Dependencies, roadmap.HasScheduleConflict);

        var baseline = (await _svc.GetSnapshotAsync(snapshot.Id))!.Value;
        var pdf = RoadmapPdf.Render(roadmap, DateTime.Now,
            new GanttOptions(ItemType.Task, baseline.Items.ToDictionary(i => i.Id), baseline.Snapshot.Name));

        Assert.Equal(2, PageCount(pdf)); // one Gantt page for 12 items, then the tables
    }

    [Fact]
    public async Task Pdf_gantt_spills_onto_more_pages_only_when_rows_would_be_unreadable()
    {
        var pid = (await _svc.CreateProductAsync("Big", null, ScoringModel.Rice)).Id;
        var start = new DateOnly(2026, 1, 5);
        var goal = await Add(pid, ItemType.Goal, null, "Everything");
        var initiative = await Add(pid, ItemType.Initiative, goal, "All work");
        for (var f = 0; f < 6; f++)
        {
            var feature = await Add(pid, ItemType.Feature, initiative, $"Feature {f + 1}");
            for (var t = 0; t < 14; t++)
            {
                var s = start.AddDays(f * 40 + t * 9);
                await Add(pid, ItemType.Task, feature, $"Task {f + 1}.{t + 1} with a fairly long descriptive title", d =>
                {
                    d.StartDate = s;
                    d.EndDate = s.AddDays(20);
                });
            }
        }
        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        Assert.Equal(92, roadmap.Items.Count);

        // The item table is identical in both, so the difference is the extra chart pages.
        var full = RoadmapPdf.Render(roadmap, new DateTime(2026, 3, 1));
        var featuresOnly = RoadmapPdf.Render(roadmap, new DateTime(2026, 3, 1), new GanttOptions(ItemType.Feature));
        Assert.True(PageCount(full) > PageCount(featuresOnly), "92 rows should need more than one chart page; 8 rows fit on one");
    }

    [Fact]
    public async Task Import_skips_invalid_rows_and_creates_missing_themes_and_tags()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var plans = ImportPlanner.Plan(
        [
            new ImportRow(2, new Dictionary<string, string> { ["Title"] = "Goal", ["Theme"] = "New theme", ["Tags"] = "x; y" }),
            new ImportRow(3, new Dictionary<string, string> { ["Title"] = "Init", ["Parent"] = "Goal", ["Progress"] = "50" }),
            new ImportRow(4, new Dictionary<string, string> { ["Title"] = "Broken", ["Parent"] = "Missing" }),
        ], (await _svc.GetRoadmapAsync(pid))!);

        Assert.Equal(2, await _svc.ImportAsync(pid, plans));

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var goal = roadmap.Items.Single(i => i.ItemType == ItemType.Goal);
        Assert.Equal("New theme", goal.Theme?.Name);
        Assert.Equal(["x", "y"], goal.Tags.Select(t => t.Name).Order());
        Assert.Equal(50, goal.ProgressPct); // rolled up after import
        Assert.Equal(goal.Id, roadmap.Items.Single(i => i.Title == "Init").ParentId);
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
