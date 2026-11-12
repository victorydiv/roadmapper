using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>A record a to-do can be linked to, for the editor's picker. <see cref="Detail"/>: e.g. an item's product and type.</summary>
public record TodoLinkOption(TodoLink Link, string Name, string? Detail = null);

/// <summary>Which to-dos to list.</summary>
public sealed record TodoQuery
{
    /// <summary>Only those about this product: linked to it or to one of its roadmap items.</summary>
    public int? ProductId { get; init; }
    /// <summary>Only those linked to this record.</summary>
    public TodoLink? Link { get; init; }
    public bool IncludeDone { get; init; }
}

public partial class PortfolioService
{
    private static IQueryable<TodoItem> WithLinks(IQueryable<TodoItem> q) => q
        .Include(t => t.Product).Include(t => t.RoadmapItem).Include(t => t.Application)
        .Include(t => t.Decision).Include(t => t.Vendor).Include(t => t.Contact);

    /// <summary>To-dos in list order (<see cref="TodoRules.InOrder"/>), with what they're linked to.</summary>
    public async Task<List<TodoItem>> GetTodosAsync(TodoQuery? query = null)
    {
        query ??= new TodoQuery();
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = WithLinks(db.Todos.AsNoTracking());
        if (!query.IncludeDone) q = q.Where(t => t.CompletedOn == null);
        if (query.ProductId is { } pid) q = q.Where(t => t.ProductId == pid);
        if (query.Link is { } l)
            q = l.Kind switch
            {
                TodoLinkKind.Product => q.Where(t => t.ProductId == l.Id),
                TodoLinkKind.RoadmapItem => q.Where(t => t.RoadmapItemId == l.Id),
                TodoLinkKind.Application => q.Where(t => t.ApplicationId == l.Id),
                TodoLinkKind.Decision => q.Where(t => t.DecisionId == l.Id),
                TodoLinkKind.Vendor => q.Where(t => t.VendorId == l.Id),
                _ => q.Where(t => t.ContactId == l.Id),
            };
        return TodoRules.InOrder(await q.ToListAsync()).ToList();
    }

    /// <summary>Open to-dos whose reminder day has come (overdue first), everywhere or about one product.</summary>
    public async Task<List<TodoItem>> GetRemindersAsync(int? productId = null)
    {
        var today = Today;
        return (await GetTodosAsync(new TodoQuery { ProductId = productId })).Where(t => TodoRules.NeedsReminder(t, today)).ToList();
    }

    public async Task<TodoSummary> GetTodoSummaryAsync(int? productId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.Todos.AsNoTracking().Where(t => t.CompletedOn == null);
        if (productId is { } pid) q = q.Where(t => t.ProductId == pid);
        return TodoRules.Summarize(await q.ToListAsync(), Today);
    }

    public async Task<TodoItem?> GetTodoAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithLinks(db.Todos.AsNoTracking()).FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>Everything a to-do can be about: products, their roadmap items, applications, decisions, vendors and contacts.</summary>
    public async Task<List<TodoLinkOption>> GetTodoLinkOptionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cmp = StringComparer.OrdinalIgnoreCase;
        var products = await db.Products.AsNoTracking().Where(p => p.ArchivedAt == null).Select(p => new { p.Id, p.Name }).ToListAsync();
        var productNames = products.ToDictionary(p => p.Id, p => p.Name);
        var options = new List<TodoLinkOption>();
        options.AddRange(products.OrderBy(p => p.Name, cmp).Select(p => new TodoLinkOption(new(TodoLinkKind.Product, p.Id), p.Name)));
        options.AddRange((await db.Items.AsNoTracking().Select(i => new { i.Id, i.Title, i.ItemType, i.ProductId }).ToListAsync())
            .Where(i => productNames.ContainsKey(i.ProductId))
            .OrderBy(i => productNames[i.ProductId], cmp).ThenBy(i => i.ItemType).ThenBy(i => i.Title, cmp)
            .Select(i => new TodoLinkOption(new(TodoLinkKind.RoadmapItem, i.Id), i.Title, $"{productNames[i.ProductId]} · {i.ItemType}")));
        options.AddRange((await db.Applications.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync())
            .OrderBy(a => a.Name, cmp).Select(a => new TodoLinkOption(new(TodoLinkKind.Application, a.Id), a.Name)));
        options.AddRange((await db.Decisions.AsNoTracking().Select(d => new { d.Id, d.Title, d.DecidedOn }).ToListAsync())
            .OrderByDescending(d => d.DecidedOn).ThenBy(d => d.Title, cmp)
            .Select(d => new TodoLinkOption(new(TodoLinkKind.Decision, d.Id), d.Title, d.DecidedOn.ToString("MMM d, yyyy"))));
        options.AddRange((await db.Vendors.AsNoTracking().Select(v => new { v.Id, v.Name }).ToListAsync())
            .OrderBy(v => v.Name, cmp).Select(v => new TodoLinkOption(new(TodoLinkKind.Vendor, v.Id), v.Name)));
        options.AddRange((await db.Contacts.AsNoTracking().Select(c => new { c.Id, c.Name, Company = c.Vendor != null ? c.Vendor.Name : null }).ToListAsync())
            .OrderBy(c => c.Name, cmp).Select(c => new TodoLinkOption(new(TodoLinkKind.Contact, c.Id), c.Name, c.Company)));
        return options;
    }

    /// <summary>Creates or updates a to-do. One linked to a roadmap item also gets the item's product. Returns its id.</summary>
    public async Task<int> SaveTodoAsync(TodoDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        int? itemProduct = null;
        if (draft.Link is { } l)
        {
            var exists = l.Kind switch
            {
                TodoLinkKind.Product => await db.Products.AnyAsync(x => x.Id == l.Id),
                TodoLinkKind.RoadmapItem => (itemProduct = await db.Items.Where(x => x.Id == l.Id).Select(x => (int?)x.ProductId).FirstOrDefaultAsync()) is not null,
                TodoLinkKind.Application => await db.Applications.AnyAsync(x => x.Id == l.Id),
                TodoLinkKind.Decision => await db.Decisions.AnyAsync(x => x.Id == l.Id),
                TodoLinkKind.Vendor => await db.Vendors.AnyAsync(x => x.Id == l.Id),
                _ => await db.Contacts.AnyAsync(x => x.Id == l.Id),
            };
            if (!exists) throw NotFound(TodoRules.Label(l.Kind));
        }

        TodoItem todo;
        if (draft.IsNew)
        {
            todo = new TodoItem();
            db.Todos.Add(todo);
        }
        else todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == draft.Id) ?? throw NotFound("To-do");

        draft.ApplyTo(todo, Today);
        TodoLink.Apply(draft.Link, todo);
        if (itemProduct is not null) todo.ProductId = itemProduct;
        todo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return todo.Id;
    }

    /// <summary>Ticks a to-do off (done today) or reopens it.</summary>
    public async Task SetTodoDoneAsync(int id, bool done)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id) ?? throw NotFound("To-do");
        if (todo.IsDone == done) return;
        todo.CompletedOn = done ? Today : null;
        todo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Hides a to-do's reminder for <paramref name="days"/> days (it comes back then, even if it's overdue).</summary>
    public async Task SnoozeTodoAsync(int id, int days)
    {
        if (days < 1) throw new InvalidOperationException("Snooze for at least a day.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id) ?? throw NotFound("To-do");
        todo.RemindOn = Today.AddDays(days);
        todo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteTodoAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Todos.Where(t => t.Id == id).ExecuteDeleteAsync();
    }
}
