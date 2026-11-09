namespace Roadmapper.Core.Portfolio;

/// <summary>A box to place: <see cref="Column"/> is any sortable key (a category column, or a signed distance from the focus).</summary>
public sealed record MapNode(NodeRef Id, string Label, int Column);

public sealed record MapEdge(NodeRef FromId, NodeRef ToId);

public sealed record NodeBox(NodeRef Id, double X, double Y, int ColumnIndex, int Row);

/// <summary>Where every node goes. <see cref="ColumnKeys"/> are the non-empty columns, left to right.</summary>
public sealed record MapLayoutResult(double Width, double Height, IReadOnlyDictionary<NodeRef, NodeBox> Boxes, IReadOnlyList<int> ColumnKeys)
{
    public double ColumnX(int columnIndex) => MapLayout.Margin + columnIndex * (MapLayout.NodeWidth + MapLayout.ColumnGap);
}

/// <summary>
/// A small layered layout for the integration map: nodes sit in columns, and within a column they're ordered to
/// sit near the nodes they connect to (two barycenter sweeps), which keeps crossings down. Deterministic: the
/// same input always gives the same picture, so the map doesn't jump around between visits.
/// </summary>
public static class MapLayout
{
    public const double NodeWidth = 176, NodeHeight = 50, ColumnGap = 100, RowGap = 22, Margin = 24;

    /// <summary>Trading partners sit in the first column of the catalog map.</summary>
    public const int PartnerColumn = 0;

    /// <summary>Catalog columns, left to right, roughly following an order's path through a 3PL; partners come first.</summary>
    public static int CategoryColumn(AppCategory c) => c switch
    {
        AppCategory.EDI or AppCategory.IntegrationPlatform => 1,
        AppCategory.CustomerPortal => 2,
        AppCategory.WMS or AppCategory.TMS => 3,
        AppCategory.ERP or AppCategory.Finance => 4,
        _ => 5,
    };

    public static string CategoryColumnTitle(int column) => column switch
    {
        0 => "Trading partners",
        1 => "EDI & integration",
        2 => "Portals",
        3 => "Warehouse & transport",
        4 => "ERP & finance",
        _ => "Reporting & other",
    };

    /// <summary>Headings for a dependency view. Columns follow the data's path, so they're "further", not a count of integrations.</summary>
    public static string FocusColumnTitle(int column) => column switch
    {
        0 => "Selected",
        -1 => "Upstream (feeds it)",
        1 => "Downstream (it feeds)",
        < 0 => "Further upstream",
        _ => "Further downstream",
    };

    /// <summary>
    /// Columns for a dependency view. Which nodes appear comes from the dependency walk (up to <paramref name="depth"/>
    /// integrations away); where they sit follows the path the data takes, so an integration routed through a middle
    /// hop draws as source → hop → target in a line. Upstream nodes get negative columns, downstream positive; a node
    /// that is both (it sends to and receives from the focus, by any route) is always drawn downstream, so a two-way
    /// path stays in one line and the other flows curve back to it.
    /// </summary>
    public static Dictionary<NodeRef, int> FocusColumns(PortfolioGraph graph, NodeRef focus, int depth)
    {
        var down = graph.Downstream(focus, depth).ToDictionary(r => r.Node, r => r.Distance);
        var up = graph.Upstream(focus, depth).ToDictionary(r => r.Node, r => r.Distance);
        var downPath = graph.PathDistances(focus, downstream: true, down.Keys.ToHashSet());
        var upPath = graph.PathDistances(focus, downstream: false, up.Keys.ToHashSet());

        var columns = new Dictionary<NodeRef, int> { [focus] = 0 };
        foreach (var (node, distance) in down) columns[node] = downPath.GetValueOrDefault(node, distance);
        foreach (var (node, distance) in up)
            if (!columns.ContainsKey(node)) columns[node] = -upPath.GetValueOrDefault(node, distance);
        return columns;
    }

    public static MapLayoutResult Layout(IEnumerable<MapNode> nodes, IEnumerable<MapEdge> edges)
    {
        var nodeList = nodes.DistinctBy(n => n.Id).ToList();
        var keys = nodeList.Select(n => n.Column).Distinct().Order().ToList();
        var columnIndex = keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);

        // Start alphabetical, then let neighbors pull nodes into place.
        var columns = keys.Select(k => nodeList.Where(n => n.Column == k)
                .OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Id).Select(n => n.Id).ToList())
            .ToList();
        var colOf = nodeList.ToDictionary(n => n.Id, n => columnIndex[n.Column]);

        var neighbors = nodeList.ToDictionary(n => n.Id, _ => new List<NodeRef>());
        foreach (var e in edges)
        {
            if (e.FromId == e.ToId || !neighbors.ContainsKey(e.FromId) || !neighbors.ContainsKey(e.ToId)) continue;
            neighbors[e.FromId].Add(e.ToId);
            neighbors[e.ToId].Add(e.FromId);
        }

        // Position within a column as 0–1, so columns of different heights compare fairly.
        Dictionary<NodeRef, double> Positions() => columns
            .SelectMany(col => col.Select((id, row) => (id, pos: (row + 0.5) / col.Count)))
            .ToDictionary(x => x.id, x => x.pos);

        void Sweep(IEnumerable<int> order, Func<int, int, bool> isReference)
        {
            foreach (var c in order)
            {
                var pos = Positions();
                columns[c] = columns[c]
                    .OrderBy(id =>
                    {
                        var refs = neighbors[id].Where(n => isReference(colOf[n], c)).ToList();
                        return refs.Count == 0 ? pos[id] : refs.Average(n => pos[n]);
                    })
                    .ToList(); // OrderBy is stable, so ties keep the previous order.
            }
        }

        var all = Enumerable.Range(0, columns.Count).ToList();
        Sweep(all.Skip(1), (nc, c) => nc < c);
        Sweep(Enumerable.Reverse(all).Skip(1), (nc, c) => nc > c);
        Sweep(all.Skip(1), (nc, c) => nc != c);

        var maxRows = columns.Count == 0 ? 0 : columns.Max(c => c.Count);
        var rowStep = NodeHeight + RowGap;
        var boxes = new Dictionary<NodeRef, NodeBox>();
        for (var c = 0; c < columns.Count; c++)
        {
            var offset = (maxRows - columns[c].Count) * rowStep / 2;
            for (var r = 0; r < columns[c].Count; r++)
                boxes[columns[c][r]] = new NodeBox(columns[c][r], Margin + c * (NodeWidth + ColumnGap), Margin + offset + r * rowStep, c, r);
        }

        var width = columns.Count == 0 ? 2 * Margin : 2 * Margin + columns.Count * NodeWidth + (columns.Count - 1) * ColumnGap;
        var height = maxRows == 0 ? 2 * Margin : 2 * Margin + maxRows * NodeHeight + (maxRows - 1) * RowGap;
        return new MapLayoutResult(width, height, boxes, keys);
    }
}
