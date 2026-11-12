using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

public class TodoRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Fact]
    public void Reminders_start_on_the_remind_date_or_else_the_due_date()
    {
        Assert.False(TodoRules.NeedsReminder(new TodoItem { DueDate = Today.AddDays(3) }, Today));
        Assert.True(TodoRules.NeedsReminder(new TodoItem { DueDate = Today }, Today));
        Assert.True(TodoRules.NeedsReminder(new TodoItem { DueDate = Today.AddDays(3), RemindOn = Today.AddDays(-1) }, Today));
        // Snoozed past today: hidden even when overdue; done or undated: never.
        Assert.False(TodoRules.NeedsReminder(new TodoItem { DueDate = Today.AddDays(-2), RemindOn = Today.AddDays(1) }, Today));
        Assert.False(TodoRules.NeedsReminder(new TodoItem { DueDate = Today.AddDays(-2), CompletedOn = Today }, Today));
        Assert.False(TodoRules.NeedsReminder(new TodoItem(), Today));
    }

    [Fact]
    public void Buckets_and_order()
    {
        TodoItem T(string title, int? due, TodoPriority p = TodoPriority.Normal, bool done = false) =>
            new() { Title = title, DueDate = due is { } d ? Today.AddDays(d) : null, Priority = p, CompletedOn = done ? Today : null };
        var list = new[] { T("later", 30), T("none", null), T("done", -5, done: true), T("late", -1), T("today low", 0, TodoPriority.Low), T("today high", 0, TodoPriority.High), T("week", 7) };
        Assert.Equal(["late", "today high", "today low", "week", "later", "none", "done"], TodoRules.InOrder(list).Select(t => t.Title));
        Assert.Equal(
            [TodoBucket.Later, TodoBucket.NoDate, TodoBucket.Done, TodoBucket.Overdue, TodoBucket.Today, TodoBucket.Today, TodoBucket.ThisWeek],
            list.Select(t => TodoRules.Bucket(t, Today)));
        Assert.Equal(new TodoSummary(6, 1, 2, 3), TodoRules.Summarize(list, Today));
    }

    [Fact]
    public void Links_round_trip_through_their_keys_and_a_draft_records_completion()
    {
        foreach (var kind in Enum.GetValues<TodoLinkKind>())
            Assert.Equal(new TodoLink(kind, 42), TodoLink.Parse(new TodoLink(kind, 42).Key));
        Assert.Null(TodoLink.Parse("q1"));

        var t = new TodoItem();
        var draft = new TodoDraft { Title = "  Call Kaleris  ", Done = true };
        Assert.Null(draft.Validate());
        draft.ApplyTo(t, Today);
        Assert.Equal(("Call Kaleris", Today), (t.Title, t.CompletedOn!.Value));
        draft.Done = false;
        draft.ApplyTo(t, Today);
        Assert.Null(t.CompletedOn);
        Assert.NotNull(new TodoDraft { Title = " " }.Validate());
    }
}

public sealed class TodoServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 12);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;
    private readonly RoadmapService _roadmaps;

    public TodoServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        _svc = new PortfolioService(_factory, clock);
        _roadmaps = new RoadmapService(_factory, clock);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<int> ItemAsync(int productId, string title)
    {
        var d = ItemDraft.New(productId, ItemType.Goal, null);
        d.Title = title;
        return await _roadmaps.SaveItemAsync(d);
    }

    [Fact]
    public async Task To_dos_link_to_records_and_show_on_their_products_dashboard()
    {
        var wms = (await _roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var yms = (await _roadmaps.CreateProductAsync("YMS", null, ScoringModel.Rice)).Id;
        var goal = await ItemAsync(wms, "Go live with BY");
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Kaleris" });

        var a = await _svc.SaveTodoAsync(new TodoDraft { Title = "Sign SOW", DueDate = Today, Link = new TodoLink(TodoLinkKind.RoadmapItem, goal) });
        await _svc.SaveTodoAsync(new TodoDraft { Title = "Renewal call", DueDate = Today.AddDays(10), Link = new TodoLink(TodoLinkKind.Vendor, vendor) });
        await _svc.SaveTodoAsync(new TodoDraft { Title = "YMS demo", DueDate = Today.AddDays(-1), Link = new TodoLink(TodoLinkKind.Product, yms) });
        await _svc.SaveTodoAsync(new TodoDraft { Title = "Expense report" });

        var sign = (await _svc.GetTodoAsync(a))!;
        Assert.Equal((wms, "Go live with BY"), (sign.ProductId!.Value, sign.LinkName));
        Assert.Equal(new TodoLink(TodoLinkKind.RoadmapItem, goal), sign.Link);

        Assert.Equal(["Sign SOW"], (await _svc.GetTodosAsync(new TodoQuery { ProductId = wms })).Select(t => t.Title));
        Assert.Equal(["Renewal call"], (await _svc.GetTodosAsync(new TodoQuery { Link = new TodoLink(TodoLinkKind.Vendor, vendor) })).Select(t => t.Title));
        Assert.Equal(["YMS demo", "Sign SOW"], (await _svc.GetRemindersAsync()).Select(t => t.Title));
        Assert.Equal(new TodoSummary(4, 1, 1, 2), await _svc.GetTodoSummaryAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveTodoAsync(new TodoDraft { Title = "x", Link = new TodoLink(TodoLinkKind.Application, 999) }));
    }

    [Fact]
    public async Task Done_snooze_and_deleting_the_linked_record()
    {
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Kaleris" });
        var id = await _svc.SaveTodoAsync(new TodoDraft { Title = "Chase quote", DueDate = Today.AddDays(-3), Link = new TodoLink(TodoLinkKind.Vendor, vendor) });

        await _svc.SnoozeTodoAsync(id, 2);
        Assert.Empty(await _svc.GetRemindersAsync());
        Assert.Equal(Today.AddDays(2), (await _svc.GetTodoAsync(id))!.RemindOn);

        await _svc.SetTodoDoneAsync(id, true);
        Assert.Empty(await _svc.GetTodosAsync());
        Assert.Equal(Today, (await _svc.GetTodosAsync(new TodoQuery { IncludeDone = true })).Single().CompletedOn);

        // Deleting the vendor keeps the to-do, unlinked.
        await _svc.DeleteVendorAsync(vendor);
        var t = (await _svc.GetTodoAsync(id))!;
        Assert.Null(t.Link);

        await _svc.DeleteTodoAsync(id);
        Assert.Null(await _svc.GetTodoAsync(id));
    }

    [Fact]
    public async Task To_dos_travel_in_the_portfolio_file()
    {
        var wms = (await _roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var goal = await ItemAsync(wms, "Go live with BY");
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Kaleris" });
        await _svc.SaveTodoAsync(new TodoDraft { Title = "Sign SOW", DueDate = Today, Priority = TodoPriority.High, Link = new TodoLink(TodoLinkKind.RoadmapItem, goal) });
        await _svc.SaveTodoAsync(new TodoDraft { Title = "Renewal call", DueDate = Today.AddDays(10), RemindOn = Today.AddDays(3), Link = new TodoLink(TodoLinkKind.Vendor, vendor) });
        await _svc.SaveTodoAsync(new TodoDraft { Title = "Plan", Link = new TodoLink(TodoLinkKind.Product, wms) });

        using var buffer = new MemoryStream((await _svc.ExportPortfolioAsync()).ToBytes());
        var file = PortfolioFile.Read(buffer);
        Assert.Equal(3, file.Todos.Count);

        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(other).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var target = new PortfolioService(factory);
        var roadmaps = new RoadmapService(factory);
        var targetProduct = (await roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var d = ItemDraft.New(targetProduct, ItemType.Goal, null);
        d.Title = "Go live with BY";
        var targetGoal = await roadmaps.SaveItemAsync(d);

        var result = await target.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal(3, result.Added["to-dos"]);
        var todos = await target.GetTodosAsync();
        var sign = todos.Single(t => t.Title == "Sign SOW");
        Assert.Equal((new TodoLink(TodoLinkKind.RoadmapItem, targetGoal), targetProduct, TodoPriority.High), (sign.Link!.Value, sign.ProductId!.Value, sign.Priority));
        var renewal = todos.Single(t => t.Title == "Renewal call");
        Assert.Equal(("Kaleris", Today.AddDays(3)), (renewal.LinkName, renewal.RemindOn!.Value));
        Assert.Equal(new TodoLink(TodoLinkKind.Product, targetProduct), todos.Single(t => t.Title == "Plan").Link);

        var again = await target.ImportPortfolioAsync(file, PortfolioImportMode.Merge);
        Assert.Equal(0, again.Added.GetValueOrDefault("to-dos"));
        Assert.Equal(3, (await target.GetTodosAsync()).Count);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
