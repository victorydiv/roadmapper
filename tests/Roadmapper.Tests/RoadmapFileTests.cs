using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Data;

namespace Roadmapper.Tests;

/// <summary>Roadmapper (.rmp) files: export a whole product, open it as a new product, and reject bad files.</summary>
public sealed class RoadmapFileTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RoadmapService _svc;

    public RoadmapFileTests()
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

    /// <summary>A product using every kind of data a file carries. Returns its id.</summary>
    private async Task<int> BuildRichProductAsync()
    {
        var pid = (await _svc.CreateProductAsync("Portal", "Customer portal", ScoringModel.Wsjf)).Id;
        await _svc.AddThemeAsync(pid, "Growth", "#2f9e6e");
        await _svc.AddTagAsync(pid, "ux");
        await _svc.AddTagAsync(pid, "api");
        var r = (await _svc.GetRoadmapAsync(pid))!;
        var growth = r.Product.Themes.Single().Id;
        var ux = r.Product.Tags.Single(t => t.Name == "ux").Id;

        var goal = await Add(pid, ItemType.Goal, null, "Grow");
        var init = await Add(pid, ItemType.Initiative, goal, "Onboarding", d => { d.ThemeId = growth; d.Horizon = Horizon.Now; });
        var signup = await Add(pid, ItemType.Feature, init, "Sign-up", d =>
        {
            d.Score = new ItemScore { BusinessValue = 8, TimeCriticality = 5, RiskReduction = 3, JobSize = 4 };
            d.TagIds.Add(ux);
            d.ManualRank = 2;
            d.Owner = "Dana";
        });
        var billing = await Add(pid, ItemType.Feature, init, "Billing", d =>
        {
            d.IsRollupOverridden = true;
            d.StartDate = new DateOnly(2026, 6, 1);
            d.EndDate = new DateOnly(2026, 6, 30);
        });
        await Add(pid, ItemType.Task, signup, "Design form", d =>
        {
            d.StartDate = new DateOnly(2026, 3, 2);
            d.EndDate = new DateOnly(2026, 3, 13);
            d.Status = ItemStatus.InProgress;
            d.ProgressPct = 40;
        });
        var doomed = await Add(pid, ItemType.Task, signup, "Spike (deleted later)");
        await _svc.AddDependencyAsync(signup, billing);

        await _svc.SaveTemplateAsync(new TemplateDraft
        {
            ProductId = pid, Name = "Launch kit", Description = "Standard launch",
            Lines = [new TemplateLineDraft { Title = "Announce", DurationDays = 2, TagIds = [ux] }, new TemplateLineDraft { Title = "Train support", StartOffsetDays = 1 }],
        });
        var template = (await _svc.GetTemplatesAsync(pid)).Single(t => t.Name == "Launch kit");
        r = (await _svc.GetRoadmapAsync(pid))!;
        await _svc.ApplyTemplateAsync(template.Id, TemplateRules.Plan(template, r.Find(billing)!, r.ChildrenOf(billing)));

        await _svc.TakeSnapshotAsync(pid, "Plan of record", "agreed");
        await _svc.DeleteItemAsync(doomed);
        r = (await _svc.GetRoadmapAsync(pid))!;
        var design = ItemDraft.From(r.Items.Single(i => i.Title == "Design form"));
        design.EndDate = new DateOnly(2026, 3, 20);
        await _svc.SaveItemAsync(design);
        return pid;
    }

    private async Task<Product> RoundTripAsync(int productId)
    {
        var file = (await _svc.ExportFileAsync(productId))!;
        using var stream = new MemoryStream(file.ToBytes());
        return await _svc.ImportFileAsync(RoadmapFile.Read(stream));
    }

    private static RoadmapFile Parse(string json) => RoadmapFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public async Task Round_trip_recreates_the_whole_product()
    {
        var pid = await BuildRichProductAsync();
        var copy = await RoundTripAsync(pid);

        Assert.NotEqual(pid, copy.Id);
        Assert.Equal("Portal (imported)", copy.Name);
        var before = (await _svc.GetRoadmapAsync(pid))!;
        var after = (await _svc.GetRoadmapAsync(copy.Id))!;

        Assert.Equal("Customer portal", after.Product.Description);
        Assert.Equal(ScoringModel.Wsjf, after.Product.ScoringModel);
        Assert.Equal(before.Product.Themes.Select(t => (t.Name, t.Color)), after.Product.Themes.Select(t => (t.Name, t.Color)));
        Assert.Equal(before.Product.Tags.Select(t => t.Name), after.Product.Tags.Select(t => t.Name));

        // Same tree, in the same order, with the same field values.
        static object Shape(ProductRoadmap r, RoadmapItem i) => new
        {
            Path = r.PathOf(i), i.ItemType, i.Title, i.Status, i.ProgressPct, i.StartDate, i.EndDate, i.Horizon, i.Owner,
            Theme = i.Theme?.Name, Tags = string.Join(",", i.Tags.Select(t => t.Name).Order()), i.ManualRank, i.IsRollupOverridden,
            i.Score?.ComputedScore, i.SourceTemplateName, i.SourceTemplateAppliedAt, InBatch = i.TemplateBatchId is not null, i.CreatedAt, i.UpdatedAt,
        };
        Assert.Equal(before.Flatten().Select(x => Shape(before, x.Item)), after.Flatten().Select(x => Shape(after, x.Item)));
        // Template batches keep their grouping but get new ids, so undo in one product can't reach the other.
        var oldBatches = before.Items.Where(i => i.TemplateBatchId is not null).Select(i => i.TemplateBatchId).ToHashSet();
        var newBatches = after.Items.Where(i => i.TemplateBatchId is not null).Select(i => i.TemplateBatchId).ToHashSet();
        Assert.Single(newBatches);
        Assert.Empty(oldBatches.Intersect(newBatches));

        var dep = Assert.Single(after.Dependencies);
        Assert.Equal("Sign-up", after.Find(dep.PredecessorId)!.Title);
        Assert.Equal("Billing", after.Find(dep.SuccessorId)!.Title);
        Assert.True(after.IsBlocked(dep.SuccessorId));

        var templates = await _svc.GetTemplatesAsync(copy.Id);
        Assert.Equal((await _svc.GetTemplatesAsync(pid)).Select(t => t.Name), templates.Select(t => t.Name));
        var kit = templates.Single(t => t.Name == "Launch kit");
        Assert.Equal("Standard launch", kit.Description);
        Assert.Equal(["Announce", "Train support"], kit.Lines.Select(l => l.Title));
        Assert.Equal([null, 1], kit.Lines.Select(l => l.StartOffsetDays));
        Assert.Equal([2, null], kit.Lines.Select(l => l.DurationDays));
        Assert.Equal("ux", Assert.Single(kit.Lines[0].Tags).Name);

        // The snapshot still compares the same way: one item removed since, and a task (plus its Feature) slipped.
        var oldSnap = await _svc.GetSnapshotAsync(Assert.Single(await _svc.GetSnapshotsAsync(pid)).Id);
        var newSnapRow = Assert.Single(await _svc.GetSnapshotsAsync(copy.Id));
        Assert.Equal(("Plan of record", "agreed"), (newSnapRow.Name, newSnapRow.Note));
        var newSnap = await _svc.GetSnapshotAsync(newSnapRow.Id);
        var oldDiff = SnapshotComparer.Compare(oldSnap!.Value.Items, before);
        var newDiff = SnapshotComparer.Compare(newSnap!.Value.Items, after);
        Assert.Equal((1, 2, 0), (newDiff.Removed, newDiff.Slipped, newDiff.Added)); // the task and its rolled-up Feature slipped
        Assert.Equal(oldDiff.Items.Select(d => (d.Title, d.Kind, d.EndSlipDays)), newDiff.Items.Select(d => (d.Title, d.Kind, d.EndSlipDays)));

        // History comes across as it was, linked to the new items, and the import adds none of its own.
        var oldHistory = await _svc.GetActivityAsync(pid, take: 1000);
        var newHistory = await _svc.GetActivityAsync(copy.Id, take: 1000);
        Assert.Equal(oldHistory.Select(h => (h.ChangedAt, h.Action, h.ItemTitle, h.Field, h.NewValue)),
                     newHistory.Select(h => (h.ChangedAt, h.Action, h.ItemTitle, h.Field, h.NewValue)));
        var newDesign = after.Items.Single(i => i.Title == "Design form").Id;
        Assert.Contains(await _svc.GetActivityAsync(copy.Id, newDesign), h => h.Field == "End date");
    }

    [Fact]
    public async Task Imported_template_batches_can_still_be_undone_without_touching_the_original()
    {
        var pid = await BuildRichProductAsync();
        var copy = await RoundTripAsync(pid);
        var batch = (await _svc.GetRoadmapAsync(copy.Id))!.Items.First(i => i.TemplateBatchId is not null).TemplateBatchId!;

        // Undo in the copy must only remove the copy's tasks.
        await _svc.UndoTemplateBatchAsync(batch);
        Assert.Contains((await _svc.GetRoadmapAsync(pid))!.Items, i => i.Title == "Announce");
        Assert.DoesNotContain((await _svc.GetRoadmapAsync(copy.Id))!.Items, i => i.Title == "Announce");
    }

    [Fact]
    public async Task Opening_the_same_file_again_makes_another_product_with_a_new_name()
    {
        var pid = (await _svc.CreateProductAsync("Solo", null, ScoringModel.Rice)).Id;
        Assert.Equal("Solo (imported)", (await RoundTripAsync(pid)).Name);
        Assert.Equal("Solo (imported 2)", (await RoundTripAsync(pid)).Name);
        Assert.Equal(3, (await _svc.GetProductsAsync()).Count);
    }

    [Fact]
    public async Task An_empty_product_round_trips_without_gaining_the_starter_template()
    {
        var pid = (await _svc.CreateProductAsync("Bare", null, ScoringModel.Moscow)).Id;
        foreach (var t in await _svc.GetTemplatesAsync(pid)) await _svc.DeleteTemplateAsync(t.Id);
        var copy = await RoundTripAsync(pid);
        Assert.Empty(await _svc.GetTemplatesAsync(copy.Id));
        Assert.Empty((await _svc.GetRoadmapAsync(copy.Id))!.Items);
        Assert.Equal(ScoringModel.Moscow, copy.ScoringModel);
    }

    [Fact]
    public void The_file_is_readable_json_with_a_format_marker()
    {
        var json = Encoding.UTF8.GetString(new RoadmapFile { Product = new FileProduct { Name = "X" } }.ToBytes());
        Assert.Contains("\"format\": \"roadmapper\"", json);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"scoringModel\": \"Rice\"", json);
    }

    [Theory]
    [InlineData("not json at all", "isn't a readable Roadmapper file")]
    [InlineData("""{"format":"something-else","version":1,"product":{"name":"X"}}""", "isn't a Roadmapper file")]
    [InlineData("""{"format":"roadmapper","version":99,"product":{"name":"X"}}""", "newer version")]
    [InlineData("""{"format":"roadmapper","version":1,"product":{"name":" "}}""", "no name")]
    [InlineData("""{"format":"roadmapper","version":1,"product":{"name":"X"},"items":[{"id":1,"type":"Feature","title":"F"}]}""", "must belong to a Initiative")]
    [InlineData("""{"format":"roadmapper","version":1,"product":{"name":"X"},"items":[{"id":1,"parentId":7,"type":"Initiative","title":"I"}]}""", "parent (7) isn't in the file")]
    [InlineData("""{"format":"roadmapper","version":1,"product":{"name":"X"},"items":[{"id":1,"type":"Goal","title":"G","tagIds":[5]}]}""", "tag 5")]
    [InlineData("""{"format":"roadmapper","version":1,"product":{"name":"X"},"items":[{"id":1,"type":"Goal","title":"A"},{"id":2,"type":"Goal","title":"B"}],"dependencies":[{"predecessorId":1,"successorId":2},{"predecessorId":2,"successorId":1}]}""", "would create a loop")]
    [InlineData("""{"format":"roadmapper","version":1,"product":{"name":"X"},"items":[{"id":1,"type":"Goal","title":"A","startDate":"2026-05-02","endDate":"2026-05-01"}]}""", "ends before it starts")]
    [InlineData("""{"format":"roadmapper","version":1,"product":{"name":"X"},"templates":[{"name":"T","lines":[]}]}""", "has no tasks")]
    public void Bad_files_are_rejected_with_a_reason(string json, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task A_rejected_file_creates_nothing()
    {
        var file = new RoadmapFile { Product = new FileProduct { Name = "Broken" }, Items = [new FileItem { Id = 1, Type = ItemType.Task, Title = "Orphan" }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ImportFileAsync(file));
        Assert.Empty(await _svc.GetProductsAsync());
    }

    [Fact]
    public async Task A_hand_written_minimal_file_opens()
    {
        var file = Parse("""
            {
              "format": "roadmapper", "version": 1,
              "product": { "name": "From scratch" },
              "items": [
                { "id": 10, "type": "Goal", "title": "Goal" },
                { "id": 11, "parentId": 10, "type": "Initiative", "title": "Init", "status": "Done" }
              ]
            }
            """);
        var product = await _svc.ImportFileAsync(file);
        var r = (await _svc.GetRoadmapAsync(product.Id))!;
        Assert.Equal(["Goal", "Init"], r.Flatten().Select(x => x.Item.Title));
        Assert.Equal(100, r.Items.Single(i => i.Title == "Goal").ProgressPct); // rolled up from the Done initiative
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
