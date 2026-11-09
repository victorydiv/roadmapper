namespace Roadmapper.Core.Portfolio;

/// <summary>One integration as the graph sees it: application ids only.</summary>
public sealed record GraphEdge(int IntegrationId, int SourceId, int TargetId, int? ViaId = null, bool Bidirectional = false)
{
    public static GraphEdge From(Integration i) =>
        new(i.Id, i.SourceApplicationId, i.TargetApplicationId, i.ViaApplicationId, i.Direction == FlowDirection.Bidirectional);
}

/// <summary>An application reached from the start, <see cref="Distance"/> integrations away, through <see cref="IntegrationIds"/>.</summary>
public sealed record Reach(int ApplicationId, int Distance, IReadOnlyList<int> IntegrationIds);

/// <summary>
/// Applications and the integrations between them, for dependency questions: what feeds this application
/// (upstream) and what it feeds (downstream). Each integration is one step. An integration that runs through
/// a middle hop links source → via, via → target and source → target, so the source's direct dependents are
/// both the hop and the target. Bidirectional integrations count in both directions. Cycles are fine.
/// </summary>
public sealed class PortfolioGraph
{
    private readonly Dictionary<int, List<(int To, int IntegrationId)>> _forward = new();
    private readonly Dictionary<int, List<(int To, int IntegrationId)>> _backward = new();
    private readonly Dictionary<int, List<int>> _touching = new();

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
            foreach (var app in new[] { e.SourceId, e.TargetId }.Concat(e.ViaId is { } v ? [v] : Array.Empty<int>()))
            {
                var list = _touching.TryGetValue(app, out var l) ? l : _touching[app] = new();
                if (!list.Contains(e.IntegrationId)) list.Add(e.IntegrationId);
            }
        }
    }

    public static PortfolioGraph From(IEnumerable<Integration> integrations) => new(integrations.Select(GraphEdge.From));

    /// <summary>The application-to-application steps one integration makes.</summary>
    public static IEnumerable<(int From, int To)> Hops(GraphEdge e)
    {
        yield return (e.SourceId, e.TargetId);
        if (e.ViaId is { } via)
        {
            yield return (e.SourceId, via);
            yield return (via, e.TargetId);
        }
    }

    /// <summary>Every integration in which the application is the source, target or middle hop.</summary>
    public IReadOnlyList<int> IntegrationsTouching(int applicationId) =>
        _touching.TryGetValue(applicationId, out var list) ? list : [];

    /// <summary>Applications that receive data from <paramref name="applicationId"/>, directly or through others, up to <paramref name="maxDepth"/> steps.</summary>
    public IReadOnlyList<Reach> Downstream(int applicationId, int maxDepth) => Walk(_forward, applicationId, maxDepth);

    /// <summary>Applications that send data to <paramref name="applicationId"/>, directly or through others, up to <paramref name="maxDepth"/> steps.</summary>
    public IReadOnlyList<Reach> Upstream(int applicationId, int maxDepth) => Walk(_backward, applicationId, maxDepth);

    private static IReadOnlyList<Reach> Walk(Dictionary<int, List<(int To, int IntegrationId)>> adjacency, int start, int maxDepth)
    {
        var result = new List<Reach>();
        var seen = new HashSet<int> { start };
        var frontier = new List<int> { start };
        for (var distance = 1; distance <= maxDepth && frontier.Count > 0; distance++)
        {
            // Every integration that reaches a new application from the previous layer, in a stable order.
            var reached = new SortedDictionary<int, SortedSet<int>>();
            foreach (var from in frontier)
            {
                if (!adjacency.TryGetValue(from, out var next)) continue;
                foreach (var (to, integrationId) in next)
                {
                    if (seen.Contains(to)) continue;
                    (reached.TryGetValue(to, out var ids) ? ids : reached[to] = new()).Add(integrationId);
                }
            }
            foreach (var (app, ids) in reached)
            {
                seen.Add(app);
                result.Add(new Reach(app, distance, ids.ToList()));
            }
            frontier = reached.Keys.ToList();
        }
        return result;
    }

    private static void Add(Dictionary<int, List<(int, int)>> map, int from, int to, int integrationId)
    {
        if (from == to) return;
        var list = map.TryGetValue(from, out var l) ? l : map[from] = new();
        if (!list.Contains((to, integrationId))) list.Add((to, integrationId));
    }
}
