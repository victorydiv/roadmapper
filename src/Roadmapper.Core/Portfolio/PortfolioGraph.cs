namespace Roadmapper.Core.Portfolio;

public enum NodeKind { Application, Partner }

/// <summary>One end of an integration: an application or a trading partner. Sorts applications first, then by id.</summary>
public readonly record struct NodeRef(NodeKind Kind, int Id) : IComparable<NodeRef>
{
    public static NodeRef App(int id) => new(NodeKind.Application, id);
    public static NodeRef Partner(int id) => new(NodeKind.Partner, id);

    /// <summary>The partner when <paramref name="partnerId"/> is set, otherwise the application.</summary>
    public static NodeRef Of(int? applicationId, int? partnerId) => partnerId is { } p ? Partner(p) : App(applicationId ?? 0);

    public bool IsPartner => Kind == NodeKind.Partner;

    /// <summary>"a12" or "p3": a compact form for select lists and URLs.</summary>
    public string Key => (IsPartner ? "p" : "a") + Id;

    public static NodeRef? Parse(string? key) =>
        key is { Length: > 1 } && int.TryParse(key.AsSpan(1), out var id) && id > 0
            ? key[0] switch { 'a' => App(id), 'p' => Partner(id), _ => null }
            : null;

    public int CompareTo(NodeRef other) => Kind != other.Kind ? Kind.CompareTo(other.Kind) : Id.CompareTo(other.Id);

    public override string ToString() => Key;
}

/// <summary>One integration as the graph sees it.</summary>
public sealed record GraphEdge(int IntegrationId, NodeRef Source, NodeRef Target, NodeRef? Via = null, bool Bidirectional = false)
{
    public static GraphEdge From(Integration i) =>
        new(i.Id, i.SourceNode, i.TargetNode, i.ViaNode, i.Direction == FlowDirection.Bidirectional);
}

/// <summary>An application or partner reached from the start, <see cref="Distance"/> integrations away, through <see cref="IntegrationIds"/>.</summary>
public sealed record Reach(NodeRef Node, int Distance, IReadOnlyList<int> IntegrationIds);

/// <summary>
/// Applications, trading partners and the integrations between them, for dependency questions: what feeds this
/// node (upstream) and what it feeds (downstream). Each integration is one step. An integration that runs through
/// a middle hop links source → via, via → target and source → target, so the source's direct dependents are
/// both the hop and the target. Bidirectional integrations count in both directions. Cycles are fine. Trading
/// partners are end points: walks stop at them, and a partner's dependencies never include other partners.
/// </summary>
public sealed class PortfolioGraph
{
    private readonly Dictionary<NodeRef, List<(NodeRef To, int IntegrationId)>> _forward = new();
    private readonly Dictionary<NodeRef, List<(NodeRef To, int IntegrationId)>> _backward = new();
    private readonly Dictionary<NodeRef, List<int>> _touching = new();

    public PortfolioGraph(IEnumerable<GraphEdge> edges)
    {
        foreach (var e in edges)
        {
            foreach (var (from, to) in Hops(e))
            {
                Add(_forward, from, to, e.IntegrationId);
                Add(_backward, to, from, e.IntegrationId);
                if (e.Bidirectional)
                {
                    Add(_forward, to, from, e.IntegrationId);
                    Add(_backward, from, to, e.IntegrationId);
                }
            }
            var ends = new List<NodeRef> { e.Source, e.Target };
            if (e.Via is { } v) ends.Add(v);
            foreach (var node in ends)
            {
                var list = _touching.TryGetValue(node, out var l) ? l : _touching[node] = new();
                if (!list.Contains(e.IntegrationId)) list.Add(e.IntegrationId);
            }
        }
    }

    public static PortfolioGraph From(IEnumerable<Integration> integrations) => new(integrations.Select(GraphEdge.From));

    /// <summary>The node-to-node steps one integration makes.</summary>
    public static IEnumerable<(NodeRef From, NodeRef To)> Hops(GraphEdge e)
    {
        yield return (e.Source, e.Target);
        if (e.Via is { } via)
        {
            yield return (e.Source, via);
            yield return (via, e.Target);
        }
    }

    /// <summary>Every integration in which the node is the source, target or middle hop.</summary>
    public IReadOnlyList<int> IntegrationsTouching(NodeRef node) => _touching.TryGetValue(node, out var list) ? list : [];

    /// <summary>Nodes that receive data from <paramref name="start"/>, directly or through others, up to <paramref name="maxDepth"/> steps.</summary>
    public IReadOnlyList<Reach> Downstream(NodeRef start, int maxDepth) => Walk(_forward, start, maxDepth);

    /// <summary>Nodes that send data to <paramref name="start"/>, directly or through others, up to <paramref name="maxDepth"/> steps.</summary>
    public IReadOnlyList<Reach> Upstream(NodeRef start, int maxDepth) => Walk(_backward, start, maxDepth);

    /// <summary>
    /// Breadth-first walk. Trading partners are end points: the walk can arrive at a partner but never carries on
    /// through one, and a walk that starts at a partner never reports other partners. Two customers that use the
    /// same WMS depend on the WMS, not on each other.
    /// </summary>
    private static IReadOnlyList<Reach> Walk(Dictionary<NodeRef, List<(NodeRef To, int IntegrationId)>> adjacency, NodeRef start, int maxDepth)
    {
        var result = new List<Reach>();
        var seen = new HashSet<NodeRef> { start };
        var frontier = new List<NodeRef> { start };
        for (var distance = 1; distance <= maxDepth && frontier.Count > 0; distance++)
        {
            // Every integration that reaches a new node from the previous layer, in a stable order.
            var reached = new SortedDictionary<NodeRef, SortedSet<int>>();
            foreach (var from in frontier)
            {
                if (!adjacency.TryGetValue(from, out var next)) continue;
                foreach (var (to, integrationId) in next)
                {
                    if (seen.Contains(to) || (start.IsPartner && to.IsPartner)) continue;
                    (reached.TryGetValue(to, out var ids) ? ids : reached[to] = new()).Add(integrationId);
                }
            }
            foreach (var (node, ids) in reached)
            {
                seen.Add(node);
                result.Add(new Reach(node, distance, ids.ToList()));
            }
            frontier = reached.Keys.Where(n => !n.IsPartner).ToList();
        }
        return result;
    }

    private static void Add(Dictionary<NodeRef, List<(NodeRef, int)>> map, NodeRef from, NodeRef to, int integrationId)
    {
        if (from == to) return;
        var list = map.TryGetValue(from, out var l) ? l : map[from] = new();
        if (!list.Contains((to, integrationId))) list.Add((to, integrationId));
    }
}
