using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Data;

namespace Roadmapper.Tests;

public class ScheduleShiftRulesTests
{
    private static DateOnly D(int month, int day) => new(2027, month, day);

    [Fact]
    public void Days_moved_follow_the_start_then_the_end()
    {
        Assert.Equal(14, ScheduleShift.DaysMoved(D(1, 1), D(1, 31), D(1, 15), D(2, 14)));
        Assert.Equal(-3, ScheduleShift.DaysMoved(D(1, 10), null, D(1, 7), null));
        // Start unchanged: the end's change counts.
        Assert.Equal(5, ScheduleShift.DaysMoved(D(1, 1), D(1, 31), D(1, 1), D(2, 5)));
        Assert.Equal(7, ScheduleShift.DaysMoved(null, D(1, 31), null, D(2, 7)));
        Assert.Equal(0, ScheduleShift.DaysMoved(null, null, D(1, 1), D(1, 2)));
        Assert.Equal(0, ScheduleShift.DaysMoved(D(1, 1), D(1, 31), D(1, 1), D(1, 31)));
    }

    [Fact]
    public void Apply_moves_whichever_dates_an_item_has()
    {
        var both = new RoadmapItem { StartDate = D(1, 1), EndDate = D(1, 10) };
        var endOnly = new RoadmapItem { EndDate = D(3, 1) };
        var none = new RoadmapItem();
        Assert.Equal(2, ScheduleShift.Apply([both, endOnly, none], 30));
        Assert.Equal((D(1, 31), D(2, 9)), (both.StartDate!.Value, both.EndDate!.Value));
        Assert.Equal(D(3, 31), endOnly.EndDate);
        Assert.Null(endOnly.StartDate);
        Assert.Null(none.StartDate);
        Assert.Equal("+1 day", ScheduleShift.Describe(1));
        Assert.Equal("−14 days", ScheduleShift.Describe(-14));
    }

    [Fact]
    public void Descendants_cover_every_level_and_nothing_else()
    {
        RoadmapItem I(int id, int? parent) => new() { Id = id, ParentId = parent };
        var items = new[] { I(1, null), I(2, 1), I(3, 2), I(4, 3), I(5, null), I(6, 5) };
        Assert.Equal([2, 3, 4], ScheduleShift.DescendantsOf(items, 1).Select(i => i.Id).Order());
        Assert.Empty(ScheduleShift.DescendantsOf(items, 4));
    }
}

/// <summary>Moving a parent with "move its sub-items with it", against a real in-memory SQLite database.</summary>
public sealed class ScheduleShiftServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RoadmapService _svc;

    public ScheduleShiftServiceTests()
    {
        _connection.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        _svc = new RoadmapService(factory);
    }

    public void Dispose() => _connection.Dispose();

    private static DateOnly D(int month, int day) => new(2027, month, day);

    private async Task<int> Add(int productId, ItemType type, int? parent, string title, DateOnly? start = null, DateOnly? end = null)
    {
        var d = ItemDraft.New(productId, type, parent);
        d.Title = title;
        d.StartDate = start;
        d.EndDate = end;
        return await _svc.SaveItemAsync(d);
    }

    private async Task<(int Pid, int Goal, int Init, int Feat, int T1, int T2, int T3)> SeedAsync()
    {
        var pid = (await _svc.CreateProductAsync("P", null, ScoringModel.Rice)).Id;
        var goal = await Add(pid, ItemType.Goal, null, "G");
        var init = await Add(pid, ItemType.Initiative, goal, "I");
        var feat = await Add(pid, ItemType.Feature, init, "F");
        var t1 = await Add(pid, ItemType.Task, feat, "T1", D(3, 1), D(3, 10));
        var t2 = await Add(pid, ItemType.Task, feat, "T2", D(3, 15), D(3, 31));
        var t3 = await Add(pid, ItemType.Task, feat, "T3");
        return (pid, goal, init, feat, t1, t2, t3);
    }

    private async Task<ItemDraft> DraftOf(int pid, int id) => ItemDraft.From((await _svc.GetRoadmapAsync(pid))!.Find(id)!);

    [Fact]
    public async Task Moving_a_rolled_up_goal_moves_every_dated_item_below_it()
    {
        var s = await SeedAsync();
        var d = await DraftOf(s.Pid, s.Goal);
        Assert.Equal((D(3, 1), D(3, 31)), (d.StartDate!.Value, d.EndDate!.Value));
        d.StartDate = D(4, 12);
        d.EndDate = D(5, 12);

        await _svc.SaveItemAsync(d, moveSubItems: true);

        var r = (await _svc.GetRoadmapAsync(s.Pid))!;
        Assert.Equal((D(4, 12), D(4, 21)), (r.Find(s.T1)!.StartDate!.Value, r.Find(s.T1)!.EndDate!.Value));
        Assert.Equal((D(4, 26), D(5, 12)), (r.Find(s.T2)!.StartDate!.Value, r.Find(s.T2)!.EndDate!.Value));
        Assert.Null(r.Find(s.T3)!.StartDate);
        // The parents roll up from the moved tasks.
        foreach (var id in new[] { s.Feat, s.Init, s.Goal })
            Assert.Equal((D(4, 12), D(5, 12)), (r.Find(id)!.StartDate!.Value, r.Find(id)!.EndDate!.Value));
        // Each moved task records its own change.
        var history = await _svc.GetActivityAsync(s.Pid, s.T1, take: 20);
        Assert.Contains(history, h => h.Field == "Start date" || h.Field == nameof(RoadmapItem.StartDate));
    }

    [Fact]
    public async Task Moving_a_manual_feature_moves_only_its_own_tasks_and_only_when_asked()
    {
        var s = await SeedAsync();
        var d = await DraftOf(s.Pid, s.Feat);
        d.IsRollupOverridden = true;
        d.StartDate = D(2, 22);
        d.EndDate = D(3, 24);
        await _svc.SaveItemAsync(d); // not asked: tasks stay

        var r = (await _svc.GetRoadmapAsync(s.Pid))!;
        Assert.Equal(D(3, 1), r.Find(s.T1)!.StartDate);

        d = await DraftOf(s.Pid, s.Feat);
        d.StartDate = D(3, 1); // +7 days
        d.EndDate = D(3, 31);
        await _svc.SaveItemAsync(d, moveSubItems: true);

        r = (await _svc.GetRoadmapAsync(s.Pid))!;
        Assert.Equal((D(3, 8), D(3, 17)), (r.Find(s.T1)!.StartDate!.Value, r.Find(s.T1)!.EndDate!.Value));
        Assert.Equal((D(3, 22), D(4, 7)), (r.Find(s.T2)!.StartDate!.Value, r.Find(s.T2)!.EndDate!.Value));
        // The feature keeps the dates it was given; the goal above rolls up from it.
        Assert.Equal((D(3, 1), D(3, 31)), (r.Find(s.Feat)!.StartDate!.Value, r.Find(s.Feat)!.EndDate!.Value));
        Assert.Equal(D(3, 1), r.Find(s.Goal)!.StartDate);
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
