using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Data;
using Roadmapper.Data.Exchange;

namespace Roadmapper.Tests;

/// <summary>Task templates, one test per acceptance check, against a real in-memory SQLite database.</summary>
public sealed class TaskTemplateTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Factory _factory;
    private readonly RoadmapService _svc;

    public TaskTemplateTests()
    {
        _connection.Open();
        _factory = new Factory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        _svc = new RoadmapService(_factory);
    }

    public void Dispose() => _connection.Dispose();

    // ---- helpers ----

    private void Migrate(string? target = null)
    {
        using var db = _factory.CreateDbContext();
        db.GetService<IMigrator>().Migrate(target);
    }

    private async Task<int> Add(int productId, ItemType type, int? parent, string title, Action<ItemDraft>? configure = null)
    {
        var d = ItemDraft.New(productId, type, parent);
        d.Title = title;
        configure?.Invoke(d);
        return await _svc.SaveItemAsync(d);
    }

    /// <summary>A product with Goal › Initiative › one Feature per title.</summary>
    private async Task<(int ProductId, int[] Features)> ProductWithFeatures(params string[] featureTitles)
    {
        Migrate();
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var goal = await Add(pid, ItemType.Goal, null, "Goal");
        var init = await Add(pid, ItemType.Initiative, goal, "Initiative");
        var features = new List<int>();
        foreach (var t in featureTitles) features.Add(await Add(pid, ItemType.Feature, init, t));
        return (pid, features.ToArray());
    }

    /// <summary>Another Feature in the same product, starting on <paramref name="start"/>.</summary>
    private async Task<int> ProductWithFeaturesIn(int pid, DateOnly start)
    {
        var initiative = (await _svc.GetRoadmapAsync(pid))!.Items.First(i => i.ItemType == ItemType.Initiative).Id;
        return await Add(pid, ItemType.Feature, initiative, "Another", d => d.StartDate = start);
    }

    private async Task<TaskTemplate> Template(int pid, string name) =>
        (await _svc.GetTemplatesAsync(pid)).Single(t => t.Name == name);

    private async Task<List<PlannedTask>> Plan(int pid, string templateName, params int[] featureIds)
    {
        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var template = await Template(pid, templateName);
        return featureIds.SelectMany(f => TemplateRules.Plan(template, roadmap.Find(f)!, roadmap.ChildrenOf(f))).ToList();
    }

    private async Task<List<RoadmapItem>> TasksOf(int pid, int featureId) =>
        (await _svc.GetRoadmapAsync(pid))!.ChildrenOf(featureId).ToList();

    private static TemplateDraft Draft(int pid, string name, params (string Title, int? Offset, int? Duration)[] lines) => new()
    {
        ProductId = pid,
        Name = name,
        Lines = lines.Select(l => new TemplateLineDraft { Title = l.Title, StartOffsetDays = l.Offset, DurationDays = l.Duration }).ToList(),
    };

    private static readonly string[] Edi = StarterTemplates.EdiMigrationTasks;

    // ---- acceptance checks ----

    [Fact]
    public async Task Check1_create_a_six_task_template_reorder_two_and_save()
    {
        var (pid, _) = await ProductWithFeatures();
        var draft = Draft(pid, "Six step", ("A", null, null), ("B", null, null), ("C", null, null), ("D", null, null), ("E", null, null), ("F", null, null));
        (draft.Lines[1], draft.Lines[2]) = (draft.Lines[2], draft.Lines[1]); // reorder B and C
        var id = await _svc.SaveTemplateAsync(draft);

        var saved = await Template(pid, "Six step");
        Assert.Equal(id, saved.Id);
        Assert.Equal(["A", "C", "B", "D", "E", "F"], saved.Lines.Select(l => l.Title));

        // Names are unique per product, ignoring case.
        var clash = Draft(pid, "six STEP", ("X", null, null));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveTemplateAsync(clash));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task Check2_apply_to_a_feature_without_start_date_creates_tasks_in_order_with_blank_dates()
    {
        var (pid, f) = await ProductWithFeatures("Customer A migration");
        await _svc.ApplyTemplateAsync((await Template(pid, StarterTemplates.EdiMigrationName)).Id, await Plan(pid, StarterTemplates.EdiMigrationName, f[0]));

        var tasks = await TasksOf(pid, f[0]);
        Assert.Equal(Edi, tasks.Select(t => t.Title));
        Assert.All(tasks, t =>
        {
            Assert.Equal(ItemType.Task, t.ItemType);
            Assert.Equal(ItemStatus.Idea, t.Status);
            Assert.Equal(0, t.ProgressPct);
            Assert.Null(t.StartDate);
            Assert.Null(t.EndDate);
            Assert.Equal(StarterTemplates.EdiMigrationName, t.SourceTemplateName);
            Assert.NotNull(t.SourceTemplateAppliedAt);
        });
    }

    [Fact]
    public async Task Check3_apply_to_a_feature_with_start_date_calculates_task_dates()
    {
        var (pid, f) = await ProductWithFeatures("Dated feature");
        var featureStart = new DateOnly(2026, 3, 2); // Monday
        var feature = ItemDraft.From((await _svc.GetRoadmapAsync(pid))!.Find(f[0])!);
        feature.StartDate = featureStart;
        feature.EndDate = new DateOnly(2026, 3, 13);
        await _svc.SaveItemAsync(feature);

        // Each task follows the previous one; the first follows the Feature's start.
        var draft = Draft(pid, "Dated", ("Kickoff", null, 5), ("Build", 2, 3), ("Review", null, null), ("Sign-off", null, 1), ("Launch", -1, 2));
        var tid = await _svc.SaveTemplateAsync(draft);
        await _svc.ApplyTemplateAsync(tid, await Plan(pid, "Dated", f[0]));

        var tasks = (await TasksOf(pid, f[0])).ToDictionary(t => t.Title);
        (DateOnly?, DateOnly?) Dates(string title) => (tasks[title].StartDate, tasks[title].EndDate);
        (DateOnly?, DateOnly?) Mar(int start, int? end) => (new DateOnly(2026, 3, start), end is { } e ? new DateOnly(2026, 3, e) : null);
        Assert.Equal(Mar(2, 6), Dates("Kickoff"));    // Feature start, 5 days: Mon–Fri
        Assert.Equal(Mar(9, 11), Dates("Build"));     // day after Kickoff (Sat 7) + 2-day gap, 3 days
        Assert.Equal(Mar(12, null), Dates("Review")); // day after Build; no duration, so no end
        Assert.Equal(Mar(12, 12), Dates("Sign-off")); // no end before it, so same day as Review
        Assert.Equal(Mar(12, 13), Dates("Launch"));   // day after Sign-off − 1 (overlap), 2 days

        // A template with no gaps or durations (the starter) puts every task on the Feature's start date.
        var others = await ProductWithFeaturesIn(pid, featureStart);
        await _svc.ApplyTemplateAsync((await Template(pid, StarterTemplates.EdiMigrationName)).Id, await Plan(pid, StarterTemplates.EdiMigrationName, others));
        Assert.All(await TasksOf(pid, others), t => Assert.Equal((featureStart, (DateOnly?)null), (t.StartDate!.Value, t.EndDate)));

        // Applying never writes the Feature's own start; roll-up keeps it since no task starts earlier.
        Assert.Equal(featureStart, (await _svc.GetRoadmapAsync(pid))!.Find(f[0])!.StartDate);
    }

    [Fact]
    public async Task Check4_unchecking_two_tasks_creates_only_four()
    {
        var (pid, f) = await ProductWithFeatures("F");
        var plan = await Plan(pid, StarterTemplates.EdiMigrationName, f[0]);
        plan[1].Include = false;
        plan[4].Include = false;

        var batch = await _svc.ApplyTemplateAsync((await Template(pid, StarterTemplates.EdiMigrationName)).Id, plan);

        Assert.Equal(4, batch.TaskCount);
        Assert.Equal(["Build BRD", "Develop EDI docs", "Test EDI docs", "Implementation"], (await TasksOf(pid, f[0])).Select(t => t.Title));
    }

    [Fact]
    public async Task Check5_existing_task_title_is_flagged_and_skipped_by_default()
    {
        var (pid, f) = await ProductWithFeatures("F");
        await Add(pid, ItemType.Task, f[0], "build brd"); // same title, different case

        var plan = await Plan(pid, StarterTemplates.EdiMigrationName, f[0]);
        var dupe = plan.Single(p => p.Title == "Build BRD");
        Assert.True(dupe.IsDuplicate);
        Assert.False(dupe.Include);
        Assert.Equal(5, plan.Count(p => p.Include));

        await _svc.ApplyTemplateAsync((await Template(pid, StarterTemplates.EdiMigrationName)).Id, plan);
        var titles = (await TasksOf(pid, f[0])).Select(t => t.Title).ToList();
        Assert.Equal(6, titles.Count);
        Assert.Single(titles, t => TemplateRules.SameTitle(t, "Build BRD"));
    }

    [Fact]
    public async Task Check6_apply_to_three_features_gives_each_its_own_tasks()
    {
        var (pid, f) = await ProductWithFeatures("A", "B", "C");
        var batch = await _svc.ApplyTemplateAsync((await Template(pid, StarterTemplates.EdiMigrationName)).Id,
            await Plan(pid, StarterTemplates.EdiMigrationName, f));

        Assert.Equal((18, 3), (batch.TaskCount, batch.FeatureCount));
        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        foreach (var feature in f) Assert.Equal(Edi, roadmap.ChildrenOf(feature).Select(t => t.Title));
        Assert.Equal(18, roadmap.Items.Where(i => i.ItemType == ItemType.Task).Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public async Task Check7_undo_removes_the_whole_batch_and_nothing_else()
    {
        var (pid, f) = await ProductWithFeatures("A", "B");
        await Add(pid, ItemType.Task, f[0], "Hand-made task");
        var tid = (await Template(pid, StarterTemplates.EdiMigrationName)).Id;
        var keep = await _svc.ApplyTemplateAsync(tid, await Plan(pid, StarterTemplates.EdiMigrationName, f[1]));
        var undo = await _svc.ApplyTemplateAsync(tid, await Plan(pid, StarterTemplates.EdiMigrationName, f));

        Assert.Equal(12 - 6, undo.TaskCount); // Feature B's six are duplicates now, so only A's six are checked
        Assert.Equal(6, await _svc.UndoTemplateBatchAsync(undo.BatchId));

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        Assert.Equal(["Hand-made task"], roadmap.ChildrenOf(f[0]).Select(t => t.Title));
        Assert.Equal(6, roadmap.ChildrenOf(f[1]).Count(t => t.TemplateBatchId == keep.BatchId));
    }

    [Fact]
    public async Task Check8_editing_the_template_afterwards_leaves_created_tasks_unchanged()
    {
        var (pid, f) = await ProductWithFeatures("F");
        var template = await Template(pid, StarterTemplates.EdiMigrationName);
        await _svc.ApplyTemplateAsync(template.Id, await Plan(pid, StarterTemplates.EdiMigrationName, f[0]));

        var edit = TemplateDraft.From(template);
        edit.Name = "EDI v2";
        edit.Lines[0].Title = "Write the BRD";
        edit.Lines.RemoveAt(5);
        await _svc.SaveTemplateAsync(edit);

        var tasks = await TasksOf(pid, f[0]);
        Assert.Equal(Edi, tasks.Select(t => t.Title));
        Assert.All(tasks, t => Assert.Equal(StarterTemplates.EdiMigrationName, t.SourceTemplateName));
    }

    [Fact]
    public async Task Check9_deleting_the_template_keeps_created_tasks()
    {
        var (pid, f) = await ProductWithFeatures("F");
        var template = await Template(pid, StarterTemplates.EdiMigrationName);
        await _svc.ApplyTemplateAsync(template.Id, await Plan(pid, StarterTemplates.EdiMigrationName, f[0]));

        await _svc.DeleteTemplateAsync(template.Id);

        Assert.Empty(await _svc.GetTemplatesAsync(pid));
        var tasks = await TasksOf(pid, f[0]);
        Assert.Equal(Edi, tasks.Select(t => t.Title));
        Assert.All(tasks, t => Assert.Equal(StarterTemplates.EdiMigrationName, t.SourceTemplateName));
    }

    [Fact]
    public async Task Check10_save_tasks_as_template_matches_the_feature()
    {
        var (pid, f) = await ProductWithFeatures("Source");
        await _svc.AddTagAsync(pid, "edi");
        var tagId = (await _svc.GetRoadmapAsync(pid))!.Product.Tags.Single().Id;
        var feature = ItemDraft.From((await _svc.GetRoadmapAsync(pid))!.Find(f[0])!);
        feature.StartDate = new DateOnly(2026, 4, 1);
        feature.IsRollupOverridden = true; // keep the Feature's own start as the reference point
        await _svc.SaveItemAsync(feature);
        await Add(pid, ItemType.Task, f[0], "Second", d => { d.Owner = "Dev"; d.StartDate = new DateOnly(2026, 4, 6); d.EndDate = new DateOnly(2026, 4, 8); });
        await Add(pid, ItemType.Task, f[0], "First", d => { d.Description = "Kick off"; d.TagIds = [tagId]; });
        var first = (await TasksOf(pid, f[0])).Single(t => t.Title == "First").Id;
        await _svc.MoveItemAsync(first, -1);

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var draft = TemplateRules.FromFeature(roadmap.Find(f[0])!, roadmap.ChildrenOf(f[0]));
        draft.Name = "From source";
        await _svc.SaveTemplateAsync(draft);

        var t = await Template(pid, "From source");
        Assert.Equal(["First", "Second"], t.Lines.Select(l => l.Title));
        Assert.Equal("Kick off", t.Lines[0].Description);
        Assert.Equal(["edi"], t.Lines[0].Tags.Select(x => x.Name));
        Assert.Null(t.Lines[0].StartOffsetDays);
        Assert.Equal(("Dev", 5, 3), (t.Lines[1].Owner, t.Lines[1].StartOffsetDays, t.Lines[1].DurationDays)!);

        // Applying it to a Feature with the same start reproduces the dated task's dates.
        var target = new RoadmapItem { Id = 99, ItemType = ItemType.Feature, StartDate = new DateOnly(2026, 4, 1) };
        var replay = TemplateRules.Plan(t, target, []);
        Assert.Equal((new DateOnly(2026, 4, 6), new DateOnly(2026, 4, 8)), (replay[1].StartDate!.Value, replay[1].EndDate!.Value));
    }

    [Fact]
    public async Task Save_as_template_measures_gaps_between_chained_tasks()
    {
        var (pid, f) = await ProductWithFeatures("Chained");
        await Add(pid, ItemType.Task, f[0], "A", d => { d.StartDate = new DateOnly(2026, 5, 4); d.EndDate = new DateOnly(2026, 5, 8); });
        await Add(pid, ItemType.Task, f[0], "B", d => { d.StartDate = new DateOnly(2026, 5, 11); d.EndDate = new DateOnly(2026, 5, 12); });
        await Add(pid, ItemType.Task, f[0], "C", d => { d.StartDate = new DateOnly(2026, 5, 13); d.EndDate = new DateOnly(2026, 5, 15); });

        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var feature = roadmap.Find(f[0])!; // rolled up: starts May 4
        var draft = TemplateRules.FromFeature(feature, roadmap.ChildrenOf(f[0]));

        Assert.Equal([(null, 5), (2, 2), (null, 3)], draft.Lines.Select(l => (l.StartOffsetDays, l.DurationDays!.Value)));
        var target = new RoadmapItem { Id = 99, ItemType = ItemType.Feature, StartDate = new DateOnly(2026, 5, 4) };
        var replay = TemplateRules.Plan(new TaskTemplate
        {
            Lines = draft.Lines.Select((l, i) => new TaskTemplateLine { SortOrder = i, Title = l.Title, StartOffsetDays = l.StartOffsetDays, DurationDays = l.DurationDays }).ToList(),
        }, target, []);
        Assert.Equal(roadmap.ChildrenOf(f[0]).Select(c => (c.StartDate, c.EndDate)), replay.Select(p => (p.StartDate, p.EndDate)));
    }

    [Fact]
    public void Preview_rechains_after_unchecking_and_keeps_hand_edited_dates()
    {
        var start = new DateOnly(2026, 3, 2);
        PlannedTask Line(string title, int days) => new() { FeatureId = 1, Title = title, DurationDays = days, Include = true };
        var tasks = new List<PlannedTask> { Line("A", 5), Line("B", 3), Line("C", 2) };
        TemplateRules.Schedule(tasks, start);
        Assert.Equal(new DateOnly(2026, 3, 10), tasks[2].StartDate); // A Mar 2–6, B Mar 7–9, C from Mar 10

        tasks[1].Include = false; // C now follows A directly; B gets no dates
        TemplateRules.Schedule(tasks, start);
        Assert.Null(tasks[1].StartDate);
        Assert.Equal((new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 8)), (tasks[2].StartDate!.Value, tasks[2].EndDate!.Value));

        tasks[0].EndDate = new DateOnly(2026, 3, 13); // user stretches A by a week
        tasks[0].DatesEdited = true;
        TemplateRules.Schedule(tasks, start);
        Assert.Equal(new DateOnly(2026, 3, 13), tasks[0].EndDate); // kept
        Assert.Equal(new DateOnly(2026, 3, 14), tasks[2].StartDate); // C follows the edited end

        tasks[0].DatesEdited = false;
        TemplateRules.Schedule(tasks, null);
        Assert.All(tasks, t => Assert.Null(t.StartDate)); // no Feature start and nothing typed in: all undated
    }

    [Fact]
    public async Task Check11a_templates_and_template_references_survive_export_and_import()
    {
        var (pid, f) = await ProductWithFeatures("F");
        await _svc.AddTagAsync(pid, "edi");
        var tagId = (await _svc.GetRoadmapAsync(pid))!.Product.Tags.Single().Id;
        var custom = Draft(pid, "Custom", ("One", 0, 2), ("Two", 3, null));
        custom.Description = "Mine";
        custom.Lines[1].Owner = "Ops";
        custom.Lines[1].TagIds = [tagId];
        await _svc.SaveTemplateAsync(custom);
        await _svc.ApplyTemplateAsync((await Template(pid, StarterTemplates.EdiMigrationName)).Id, await Plan(pid, StarterTemplates.EdiMigrationName, f[0]));

        var xlsx = RoadmapExport.ToXlsx((await _svc.GetRoadmapAsync(pid))!, await _svc.GetTemplatesAsync(pid));

        // Restore into a fresh product, the way the Import page does.
        var restored = (await _svc.CreateProductAsync("Restored", null, ScoringModel.Rice)).Id;
        using var stream = new MemoryStream(xlsx);
        var templates = TemplateSheet.Parse(TableReader.ReadSheet(stream, "backup.xlsx", TemplateSheet.SheetName)!);
        var (created, skipped) = await _svc.ImportTemplatesAsync(restored, templates);
        Assert.Equal(1, created);
        Assert.Equal([StarterTemplates.EdiMigrationName], skipped); // the new product already has the starter

        stream.Position = 0;
        var rows = TableReader.Read(stream, "backup.xlsx");
        var importRows = rows.Skip(1).Select((r, i) => new ImportRow(i + 2,
            rows[0].Select((h, c) => (h, v: r[c])).Where(x => RoadmapColumns.Guess(x.h) is not null).ToDictionary(x => RoadmapColumns.Guess(x.h)!, x => x.v))).ToList();
        await _svc.ImportAsync(restored, ImportPlanner.Plan(importRows, (await _svc.GetRoadmapAsync(restored))!));

        var t = await Template(restored, "Custom");
        Assert.Equal("Mine", t.Description);
        Assert.Equal(["One", "Two"], t.Lines.Select(l => l.Title));
        Assert.Equal((0, 2), (t.Lines[0].StartOffsetDays!.Value, t.Lines[0].DurationDays!.Value));
        Assert.Equal(("Ops", 3), (t.Lines[1].Owner, t.Lines[1].StartOffsetDays!.Value));
        Assert.Null(t.Lines[1].DurationDays);
        Assert.Equal(["edi"], t.Lines[1].Tags.Select(x => x.Name));

        var tasks = (await _svc.GetRoadmapAsync(restored))!.Items.Where(i => i.ItemType == ItemType.Task).ToList();
        Assert.Equal(Edi, tasks.OrderBy(x => x.SortOrder).Select(x => x.Title));
        Assert.All(tasks, x => Assert.Equal(StarterTemplates.EdiMigrationName, x.SourceTemplateName));
        Assert.All(tasks, x => Assert.NotNull(x.SourceTemplateAppliedAt));
    }

    [Fact]
    public async Task Check11b_a_database_from_before_templates_migrates_and_loads()
    {
        Migrate("20261104224919_AddSnapshots"); // the last migration before task templates
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Products (Id, Name, ScoringModel, CreatedAt) VALUES (1, 'Old', 'Rice', '2026-01-01 00:00:00');
                INSERT INTO Items (Id, ProductId, ParentId, ItemType, Title, Status, ProgressPct, SortOrder, IsRollupOverridden, CreatedAt, UpdatedAt)
                VALUES (1, 1, NULL, 'Goal', 'Old goal', 'Idea', 0, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
                """);
        }

        Migrate(); // apply AddTaskTemplates to the existing data

        var roadmap = (await _svc.GetRoadmapAsync(1))!;
        var goal = Assert.Single(roadmap.Items);
        Assert.Equal("Old goal", goal.Title);
        Assert.Null(goal.SourceTemplateName);
        var starter = Assert.Single(await _svc.GetTemplatesAsync(1));
        Assert.Equal(StarterTemplates.EdiMigrationName, starter.Name);
        Assert.Equal(Edi, starter.Lines.Select(l => l.Title));
        Assert.NotNull(await _svc.TakeSnapshotAsync(1, "After upgrade", null)); // snapshots still work
    }

    // ---- other rules ----

    [Fact]
    public async Task Duplicate_template_gets_a_copy_name_and_the_same_lines()
    {
        var (pid, _) = await ProductWithFeatures();
        var source = await Template(pid, StarterTemplates.EdiMigrationName);
        await _svc.DuplicateTemplateAsync(source.Id);
        await _svc.DuplicateTemplateAsync(source.Id);

        var names = (await _svc.GetTemplatesAsync(pid)).Select(t => t.Name).ToList();
        Assert.Contains("EDI Migration Standard (copy)", names);
        Assert.Contains("EDI Migration Standard (copy 2)", names);
        Assert.Equal(Edi, (await Template(pid, "EDI Migration Standard (copy)")).Lines.Select(l => l.Title));
    }

    [Fact]
    public async Task Applying_only_to_features_and_validating_inputs()
    {
        var (pid, f) = await ProductWithFeatures("F");
        var tid = (await Template(pid, StarterTemplates.EdiMigrationName)).Id;
        var roadmap = (await _svc.GetRoadmapAsync(pid))!;
        var initiative = roadmap.Items.Single(i => i.ItemType == ItemType.Initiative);

        var wrongLevel = new PlannedTask { FeatureId = initiative.Id, Title = "X", Include = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ApplyTemplateAsync(tid, [wrongLevel]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ApplyTemplateAsync(tid, [new PlannedTask { FeatureId = f[0], Title = "X" }]));
        var badDates = new PlannedTask { FeatureId = f[0], Title = "X", Include = true, StartDate = new DateOnly(2026, 2, 2), EndDate = new DateOnly(2026, 2, 1) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ApplyTemplateAsync(tid, [badDates]));
        Assert.DoesNotContain((await _svc.GetRoadmapAsync(pid))!.Items, i => i.ItemType == ItemType.Task);
    }

    [Fact]
    public void Rollup_preview_reports_the_features_new_end_and_progress()
    {
        var feature = new RoadmapItem { Id = 1, ItemType = ItemType.Feature, StartDate = new DateOnly(2026, 3, 2), EndDate = new DateOnly(2026, 3, 13), ProgressPct = 50 };
        var existing = new[] { new RoadmapItem { ParentId = 1, StartDate = new DateOnly(2026, 3, 2), EndDate = new DateOnly(2026, 3, 13), ProgressPct = 50 } };
        var added = new[] { new PlannedTask { FeatureId = 1, Title = "Late", StartDate = new DateOnly(2026, 3, 16), EndDate = new DateOnly(2026, 3, 20), Include = true } };

        Assert.Equal((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 20), 25), TemplateRules.RollupAfter(feature, existing, added));
        feature.IsRollupOverridden = true;
        Assert.Null(TemplateRules.RollupAfter(feature, existing, added));
    }

    private sealed class Factory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
