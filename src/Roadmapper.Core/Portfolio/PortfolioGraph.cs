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

    /// <summary>The trading partner at one end, if any: the integration carries that partner's data.</summary>
    public NodeRef? Partner => Source.IsPartner ? Source : Target.IsPartner ? Target : null;
}

/// <summary>An application or partner reached from the start, <see cref="Distance"/> integrations away, through <see cref="IntegrationIds"/>.</summary>
public sealed record Reach(NodeRef Node, int Distance, IReadOnlyList<int> IntegrationIds);

/// <summary>
/// Applications, trading partners and the integrations between them, for dependency questions: what feeds a node
/// (upstream) and what it feeds (downstream).
/// <list type="bullet">
/// <item>Each integration is one step. One routed through a middle hop links source → via, via → target and
/// source → target, so the source's direct dependents are both the hop and the target. Bidirectional integrations
/// count both ways. Cycles are fine.</item>
/// <item>Data stays with its partner. An integration with a trading partner at one end carries only that partner's
/// data, so a walk that has followed one partner's integration never continues along another partner's: Synapse
/// passing PCP's data to Cleo is no dependency of Morton Salt, or of Boomi, which only carries Morton's.
/// Integrations between your own applications carry everyone's data and are always followed.</item>
/// <item>Partners are end points: a walk arrives at a partner but never continues through one.</item>
/// </list>
/// </summary>
public sealed class PortfolioGraph
{
    private sealed record Step(NodeRef To, int IntegrationId, NodeRef? Partner);

    private sealed class Adjacency
    {
        public readonly Dictionary<NodeRef, List<Step>> Forward = new();
        public readonly Dictionary<NodeRef, List<Step>> Backward = new();

        public void Add(NodeRef from, NodeRef to, GraphEdge e, bool bothWays)
        {
            Put(Forward, from, new Step(to, e.IntegrationId, e.Partner));
            Put(Backward, to, new Step(from, e.IntegrationId, e.Partner));
            if (!bothWays) return;
            Put(Forward, to, new Step(from, e.IntegrationId, e.Partner));
            Put(Backward, from, new Step(to, e.IntegrationId, e.Partner));
        }

        private static void Put(Dictionary<NodeRef, List<Step>> map, NodeRef from, Step step)
        {
            if (from == step.To) return;
            var list = map.TryGetValue(from, out var l) ? l : map[from] = new();
            if (!list.Contains(step)) list.Add(step);
        }
    }

    // Dependency steps (with the source → target shortcut of a routed integration), both directions for two-way flows.
    private readonly Adjacency _deps = new();
    private readonly Dictionary<NodeRef, List<int>> _touching = new();
    private readonly Dictionary<int, GraphEdge> _edges = new();

    public PortfolioGraph(IEnumerable<GraphEdge> edges)
    {
        foreach (var e in edges)
        {
            foreach (var (from, to) in Hops(e)) _deps.Add(from, to, e, e.Bidirectional);
            _edges[e.IntegrationId] = e;
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

    /// <summary>The node-to-node dependency steps one integration makes (including the source → target shortcut).</summary>
    public static IEnumerable<(NodeRef From, NodeRef To)> Hops(GraphEdge e)
    {
        yield return (e.Source, e.Target);
        if (e.Via is { } via)
        {
            yield return (e.Source, via);
            yield return (via, e.Target);
        }
    }

    /// <summary>The steps along the path an integration's data travels: source → via → target, or source → target.</summary>
    public static IEnumerable<(NodeRef From, NodeRef To)> PathHops(GraphEdge e)
    {
        if (e.Via is { } via)
        {
            yield return (e.Source, via);
            yield return (via, e.Target);
        }
        else yield return (e.Source, e.Target);
    }

    /// <summary>The integration with this id, as the graph holds it.</summary>
    public GraphEdge Edge(int integrationId) => _edges[integrationId];

    /// <summary>The nodes an integration's data passes, in its recorded direction: source, via (if any), target.</summary>
    public static IReadOnlyList<NodeRef> PathOf(GraphEdge e) => e.Via is { } v ? [e.Source, v, e.Target] : [e.Source, e.Target];

    /// <summary>Every integration in which the node is the source, target or middle hop.</summary>
    public IReadOnlyList<int> IntegrationsTouching(NodeRef node) => _touching.TryGetValue(node, out var list) ? list : [];

    /// <summary>Nodes that receive data from <paramref name="start"/>, directly or through others, up to <paramref name="maxDepth"/> integrations away.</summary>
    public IReadOnlyList<Reach> Downstream(NodeRef start, int maxDepth) => Walk(_deps.Forward, start, maxDepth);

    /// <summary>Nodes that send data to <paramref name="start"/>, directly or through others, up to <paramref name="maxDepth"/> integrations away.</summary>
    public IReadOnlyList<Reach> Upstream(NodeRef start, int maxDepth) => Walk(_deps.Backward, start, maxDepth);

    /// <summary>
    /// Breadth-first over (node, whose data) states, so a node reached carrying one partner's data can still be
    /// reached again carrying another's or everyone's. Each node is reported once, at its shortest distance, with
    /// every integration that reaches it at that distance.
    /// </summary>
    private static List<Reach> Walk(Dictionary<NodeRef, List<Step>> adjacency, NodeRef start, int maxDepth)
    {
        var startState = (Node: start, Carrying: start.IsPartner ? start : (NodeRef?)null);
        var seenStates = new HashSet<(NodeRef, NodeRef?)> { startState };
        var found = new Dictionary<NodeRef, (int Distance, SortedSet<int> Ids)>();
        var frontier = new List<(NodeRef Node, NodeRef? Carrying)> { startState };

        for (var distance = 1; distance <= maxDepth && frontier.Count > 0; distance++)
        {
            var next = new List<(NodeRef, NodeRef?)>();
            foreach (var (node, carrying) in frontier)
            {
                // Partners are end points; only the start may be one.
                if (node.IsPartner && node != start) continue;
                if (!adjacency.TryGetValue(node, out var steps)) continue;
                foreach (var step in steps.OrderBy(s => s.To).ThenBy(s => s.IntegrationId))
                {
                    if (step.To == start) continue;
                    // An integration with a partner carries only that partner's data.
                    if (step.Partner is { } p && carrying is { } c && p != c) continue;
                    var nowCarrying = step.Partner ?? carrying;

                    if (!found.TryGetValue(step.To, out var f)) found[step.To] = (distance, new SortedSet<int> { step.IntegrationId });
                    else if (f.Distance == distance) f.Ids.Add(step.IntegrationId);

                    if (seenStates.Add((step.To, nowCarrying))) next.Add((step.To, nowCarrying));
                }
            }
            frontier = next;
        }

        return found.OrderBy(f => f.Value.Distance).ThenBy(f => f.Key)
            .Select(f => new Reach(f.Key, f.Value.Distance, f.Value.Ids.ToList()))
            .ToList();
    }
}
