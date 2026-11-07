namespace Roadmapper.Core;

public record TreeRow(RoadmapItem Item, int Depth, bool HasChildren);

/// <summary>An in-memory, read-only view of one product's items and dependencies, shared by all roadmap views.</summary>
public class ProductRoadmap
{
    private readonly Dictionary<int, RoadmapItem> _byId;
    private readonly ILookup<int?, RoadmapItem> _byParent;

    public ProductRoadmap(Product product, IReadOnlyList<RoadmapItem> items, IReadOnlyList<Dependency> dependencies)
    {
        Product = product;
        Items = items;
        Dependencies = dependencies;
        _byId = items.ToDictionary(i => i.Id);
        _byParent = items.ToLookup(i => i.ParentId);
    }

    public Product Product { get; }
    public IReadOnlyList<RoadmapItem> Items { get; }
    public IReadOnlyList<Dependency> Dependencies { get; }
    public IScoringStrategy Strategy => Scoring.For(Product.ScoringModel);
    /// <summary>The product's WSJF scoring rubric (empty when it has none).</summary>
    public ScoringRubric Rubric { get; init; } = ScoringRubric.Empty;

    public RoadmapItem? Find(int id) => _byId.GetValueOrDefault(id);

    public bool HasChildren(int id) => _byParent.Contains(id);

    public IEnumerable<RoadmapItem> ChildrenOf(int? parentId, bool byPriority = false)
    {
        var children = _byParent[parentId];
        return byPriority ? children.InRankOrder() : children.OrderBy(i => i.SortOrder).ThenBy(i => i.Id);
    }

    public IEnumerable<RoadmapItem> Ancestors(RoadmapItem item)
    {
        for (var p = item.ParentId is { } id ? Find(id) : null; p != null; p = p.ParentId is { } pid ? Find(pid) : null)
            yield return p;
    }

    /// <summary>Ancestor titles from the top down, e.g. "Grow revenue › Self-serve checkout".</summary>
    public string PathOf(RoadmapItem item) => string.Join(" › ", Ancestors(item).Reverse().Select(a => a.Title));

    /// <summary>Depth-first rows; children are included only when <paramref name="expand"/> returns true for their parent.</summary>
    public List<TreeRow> Flatten(Func<RoadmapItem, bool>? expand = null, bool byPriority = false, ItemType deepest = ItemType.Task)
    {
        var rows = new List<TreeRow>();
        void Visit(int? parentId, int depth)
        {
            foreach (var item in ChildrenOf(parentId, byPriority))
            {
                if (item.ItemType > deepest) continue;
                var hasChildren = item.ItemType < deepest && HasChildren(item.Id);
                rows.Add(new TreeRow(item, depth, hasChildren));
                if (hasChildren && (expand?.Invoke(item) ?? true)) Visit(item.Id, depth + 1);
            }
        }
        Visit(null, 0);
        return rows;
    }

    public IEnumerable<Dependency> BlockedBy(int itemId) => Dependencies.Where(d => d.SuccessorId == itemId);

    public IEnumerable<Dependency> Blocks(int itemId) => Dependencies.Where(d => d.PredecessorId == itemId);

    public bool HasScheduleConflict(Dependency d) =>
        Find(d.PredecessorId) is { } pred && Find(d.SuccessorId) is { } succ && DependencyRules.IsScheduleConflict(pred, succ);

    public IEnumerable<RoadmapItem> Descendants(int itemId) =>
        _byParent[itemId].SelectMany(c => Descendants(c.Id).Prepend(c));

    public static bool IsOpen(RoadmapItem i) => i.Status is not (ItemStatus.Done or ItemStatus.Cancelled);

    public static bool IsOverdue(RoadmapItem i, DateOnly today) => IsOpen(i) && i.EndDate is { } end && end < today;

    /// <summary>True when any unfinished predecessor exists.</summary>
    public bool IsBlocked(int itemId) =>
        BlockedBy(itemId).Any(d => Find(d.PredecessorId) is { } p && IsOpen(p));
}
