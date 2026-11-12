namespace Roadmapper.Core.Portfolio;

public enum TodoPriority { Low, Normal, High }

/// <summary>What a to-do can be about.</summary>
public enum TodoLinkKind { Product, RoadmapItem, Application, Decision, Vendor, Contact }

/// <summary>Where a to-do sits on the list: overdue, due today, in the next 7 days, later, no due date, or done.</summary>
public enum TodoBucket { Overdue, Today, ThisWeek, Later, NoDate, Done }

/// <summary>
/// Something you need to do, with an optional deadline (<see cref="DueDate"/>) and a reminder that shows on the dashboards
/// from <see cref="RemindOn"/> (or the due date) until it's done. It can be about one record (<see cref="Link"/>). One
/// about a roadmap item also carries the item's <see cref="ProductId"/>, so it shows on that product's dashboard too.
/// </summary>
public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>The day the reminder starts showing; null means the due date.</summary>
    public DateOnly? RemindOn { get; set; }
    public TodoPriority Priority { get; set; } = TodoPriority.Normal;
    public DateOnly? CompletedOn { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? RoadmapItemId { get; set; }
    public RoadmapItem? RoadmapItem { get; set; }
    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }
    public int? DecisionId { get; set; }
    public Decision? Decision { get; set; }
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDone => CompletedOn is not null;
    public TodoLink? Link => TodoLink.Of(this);

    /// <summary>The linked record's name (needs its navigation loaded).</summary>
    public string? LinkName => RoadmapItem?.Title ?? Application?.Name ?? Decision?.Title ?? Vendor?.Name ?? Contact?.Name ?? Product?.Name;
}

/// <summary>What a to-do is about. <see cref="Key"/> ("p2", "r9", "a3", "x4", "v1", "n7") is used in select lists and URLs.</summary>
public readonly record struct TodoLink(TodoLinkKind Kind, int Id)
{
    private static readonly (TodoLinkKind Kind, string Prefix)[] Prefixes =
    [
        (TodoLinkKind.Product, "p"), (TodoLinkKind.RoadmapItem, "r"), (TodoLinkKind.Application, "a"),
        (TodoLinkKind.Decision, "x"), (TodoLinkKind.Vendor, "v"), (TodoLinkKind.Contact, "n"),
    ];

    public string Key { get { var kind = Kind; return Prefixes.First(p => p.Kind == kind).Prefix + Id; } }

    public static TodoLink? Parse(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var (kind, prefix) in Prefixes)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.AsSpan(prefix.Length), out var id) && id > 0)
                return new TodoLink(kind, id);
        return null;
    }

    /// <summary>The most specific record the to-do is linked to (a roadmap item before its product).</summary>
    public static TodoLink? Of(TodoItem t) =>
        t.RoadmapItemId is { } r ? new(TodoLinkKind.RoadmapItem, r)
        : t.ApplicationId is { } a ? new(TodoLinkKind.Application, a)
        : t.DecisionId is { } x ? new(TodoLinkKind.Decision, x)
        : t.VendorId is { } v ? new(TodoLinkKind.Vendor, v)
        : t.ContactId is { } n ? new(TodoLinkKind.Contact, n)
        : t.ProductId is { } p ? new(TodoLinkKind.Product, p)
        : null;

    /// <summary>
    /// Points the to-do at <paramref name="link"/>, or at nothing. The product of a roadmap item is set by the caller,
    /// who knows it.
    /// </summary>
    public static void Apply(TodoLink? link, TodoItem t)
    {
        (t.ProductId, t.RoadmapItemId, t.ApplicationId, t.DecisionId, t.VendorId, t.ContactId) = (null, null, null, null, null, null);
        if (link is not { } l) return;
        switch (l.Kind)
        {
            case TodoLinkKind.Product: t.ProductId = l.Id; break;
            case TodoLinkKind.RoadmapItem: t.RoadmapItemId = l.Id; break;
            case TodoLinkKind.Application: t.ApplicationId = l.Id; break;
            case TodoLinkKind.Decision: t.DecisionId = l.Id; break;
            case TodoLinkKind.Vendor: t.VendorId = l.Id; break;
            case TodoLinkKind.Contact: t.ContactId = l.Id; break;
        }
    }

    public override string ToString() => Key;
}

/// <summary>Counts for the to-do badge and dashboard tiles.</summary>
public sealed record TodoSummary(int Open, int Overdue, int DueToday, int Reminders);

public static class TodoRules
{
    public const int MaxTitleLength = 200;

    /// <summary>The day the reminder starts: <see cref="TodoItem.RemindOn"/>, else the due date; null when there's neither.</summary>
    public static DateOnly? ReminderDate(TodoItem t) => t.RemindOn ?? t.DueDate;

    /// <summary>Open, and its reminder day has come (overdue to-dos included, unless snoozed past today).</summary>
    public static bool NeedsReminder(TodoItem t, DateOnly today) => !t.IsDone && ReminderDate(t) is { } r && r <= today;

    public static bool IsOverdue(TodoItem t, DateOnly today) => !t.IsDone && t.DueDate is { } d && d < today;

    public static TodoBucket Bucket(TodoItem t, DateOnly today) => t switch
    {
        { IsDone: true } => TodoBucket.Done,
        { DueDate: null } => TodoBucket.NoDate,
        { DueDate: { } d } when d < today => TodoBucket.Overdue,
        { DueDate: { } d } when d == today => TodoBucket.Today,
        { DueDate: { } d } when d.DayNumber - today.DayNumber <= 7 => TodoBucket.ThisWeek,
        _ => TodoBucket.Later,
    };

    /// <summary>Open first; then by due date (none last), higher priority first, then title. Done: most recently completed first.</summary>
    public static IEnumerable<TodoItem> InOrder(IEnumerable<TodoItem> todos) => todos
        .OrderBy(t => t.IsDone)
        .ThenByDescending(t => t.IsDone ? t.CompletedOn : null)
        .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
        .ThenByDescending(t => t.Priority)
        .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase);

    /// <summary>Open to-dos for a dashboard: those whose reminder is due first, then the rest; each part in list order.</summary>
    public static IEnumerable<TodoItem> ForDashboard(IEnumerable<TodoItem> todos, DateOnly today)
    {
        var open = InOrder(todos.Where(t => !t.IsDone)).ToList();
        return open.Where(t => NeedsReminder(t, today)).Concat(open.Where(t => !NeedsReminder(t, today)));
    }

    public static TodoSummary Summarize(IEnumerable<TodoItem> todos, DateOnly today)
    {
        var open = todos.Where(t => !t.IsDone).ToList();
        return new TodoSummary(open.Count, open.Count(t => IsOverdue(t, today)), open.Count(t => t.DueDate == today),
            open.Count(t => NeedsReminder(t, today)));
    }

    /// <summary>"Overdue", "Today", "Next 7 days"…</summary>
    public static string Label(TodoBucket b) => b switch
    {
        TodoBucket.Today => "Due today",
        TodoBucket.ThisWeek => "Next 7 days",
        TodoBucket.NoDate => "No due date",
        _ => b.ToString(),
    };

    public static string Label(TodoLinkKind k) => k switch
    {
        TodoLinkKind.RoadmapItem => "Roadmap item",
        _ => k.ToString(),
    };
}

/// <summary>A to-do's editable fields.</summary>
public class TodoDraft
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? RemindOn { get; set; }
    public TodoPriority Priority { get; set; } = TodoPriority.Normal;
    public bool Done { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public TodoLink? Link { get; set; }

    public bool IsNew => Id == 0;

    public static TodoDraft New(TodoLink? link = null, DateOnly? due = null) => new() { Link = link, DueDate = due };

    public static TodoDraft From(TodoItem t) => new()
    {
        Id = t.Id, Title = t.Title, Notes = t.Notes, DueDate = t.DueDate, RemindOn = t.RemindOn, Priority = t.Priority,
        Done = t.IsDone, CompletedOn = t.CompletedOn, Link = t.Link,
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title)) return "Say what needs doing.";
        if (Title.Trim().Length > TodoRules.MaxTitleLength) return $"A to-do can be at most {TodoRules.MaxTitleLength} characters; put the rest in the notes.";
        if (Notes?.Trim().Length > PortfolioRules.MaxNotesLength) return $"Notes can be at most {PortfolioRules.MaxNotesLength} characters.";
        return null;
    }

    /// <summary>
    /// Copies the fields, but not the link (the service sets it, with a roadmap item's product). Marking it done
    /// records <paramref name="today"/> unless it was already done; reopening clears the date.
    /// </summary>
    public void ApplyTo(TodoItem t, DateOnly today)
    {
        t.Title = Title.Trim();
        t.Notes = DraftText.Clean(Notes);
        t.DueDate = DueDate;
        t.RemindOn = RemindOn;
        t.Priority = Priority;
        t.CompletedOn = Done ? t.CompletedOn ?? CompletedOn ?? today : null;
    }
}
