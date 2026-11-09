using System.Globalization;
using System.Security;
using System.Text;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Web.Exports;

/// <summary>
/// What to draw. With <see cref="Focus"/> it's a dependency view (upstream | selected | downstream); otherwise the whole
/// catalog by category, trading partners first. <see cref="HidePartners"/> drops flows to or from trading partners.
/// </summary>
public sealed record MapOptions(NodeRef? Focus = null, int Depth = 2, IntegrationType? Type = null, Criticality? MaxTier = null,
    bool IncludeRetired = false, bool HidePartners = false);

public sealed record MapPicture(string Svg, int NodeCount, int IntegrationCount);

/// <summary>
/// The integration map as a self-contained SVG (inline styles, no external fonts), used on the Map page, the
/// application and partner pages and the /api/portfolio/map.svg download. Boxes and lines are links to their
/// detail pages. Layout comes from <see cref="MapLayout"/>; this class only filters and draws.
/// </summary>
public static class IntegrationMapSvg
{
    private const double TitleHeight = 52, HeaderHeight = 26, LegendHeight = 64, MinWidth = 900;
    private const string FontFamily = "Segoe UI, system-ui, -apple-system, sans-serif";
    private const string Ink = "#1d2330", Muted = "#667085", Line = "#e1e4ea";
    private const string PartnerFill = "#f6f3fd", PartnerStroke = "#b9a6e3";
    /// <summary>How far past the boxes a line that skips a column bows; and the room kept for it.</summary>
    private const double ArcClearance = 14, ArcRoom = 48;

    public static string TypeColor(IntegrationType t) => t switch
    {
        IntegrationType.API => "#3f5bd9",
        IntegrationType.EDI => "#7c4dcc",
        IntegrationType.SFTP => "#128a8a",
        IntegrationType.Batch => "#b06d00",
        _ => "#6b7280",
    };

    public static string TierColor(Criticality c) => c switch
    {
        Criticality.Tier1 => "#c8372d",
        Criticality.Tier2 => "#eda100",
        _ => "#9aa3b2",
    };

    public static string ImportanceColor(PartnerImportance i) => i == PartnerImportance.Key ? "#7c4dcc" : "#c9bce8";

    /// <summary>The integrations the options keep: live ones (unless retired are included) of the chosen type and tier.</summary>
    public static List<Integration> Filter(IEnumerable<Integration> integrations, MapOptions o) => integrations
        .Where(i => o.IncludeRetired || i.Status != LifecycleStatus.Retired)
        .Where(i => o.Type is null || i.Type == o.Type)
        .Where(i => o.MaxTier is null || i.Criticality <= o.MaxTier)
        .Where(i => !o.HidePartners || !i.InvolvesPartner)
        .ToList();

    public static MapPicture Render(IReadOnlyList<Application> apps, IReadOnlyList<TradingPartner> partners,
        IReadOnlyList<Integration> integrations, MapOptions o, DateOnly today, string title)
    {
        var appById = apps.ToDictionary(a => a.Id);
        var partnerById = partners.ToDictionary(p => p.Id);
        bool Known(NodeRef n) => n.IsPartner ? partnerById.ContainsKey(n.Id) : appById.ContainsKey(n.Id);
        string NameOf(NodeRef n) => n.IsPartner ? partnerById[n.Id].Name : appById[n.Id].Name;
        var kept = Filter(integrations, o);

        // Columns: distance from the focus, or category (partners first).
        Dictionary<NodeRef, int> columns;
        var focus = o.Focus is { } f && Known(f) ? f : (NodeRef?)null;
        if (focus is { } fn)
            columns = MapLayout.FocusColumns(PortfolioGraph.From(kept), fn, Math.Clamp(o.Depth, 1, 5));
        else
            columns = kept.SelectMany(Ends).Distinct().Where(Known)
                .ToDictionary(n => n, n => n.IsPartner ? MapLayout.PartnerColumn : MapLayout.CategoryColumn(appById[n.Id].Category));
        columns = columns.Where(c => Known(c.Key)).ToDictionary(c => c.Key, c => c.Value);

        // Only integrations whose source and target are both on the picture; a hop that isn't is skipped.
        var shown = kept.Where(i => columns.ContainsKey(i.SourceNode) && columns.ContainsKey(i.TargetNode)).ToList();

        var nodes = columns.Select(c => new MapNode(c.Key, NameOf(c.Key), c.Value)).ToList();
        var segments = shown.SelectMany(i => Segments(i, columns)).ToList();
        var layout = MapLayout.Layout(nodes, segments.Select(s => new MapEdge(s.From, s.To)));

        // Lines between boxes in the same column loop out to the right; leave room for them.
        var loops = segments.Where(s => layout.Boxes[s.From].ColumnIndex == layout.Boxes[s.To].ColumnIndex).ToList();
        var loopRoom = loops.Count == 0 ? 0 : 56 + 8 * loops.Max(s => Math.Abs(layout.Boxes[s.From].Row - layout.Boxes[s.To].Row));
        var width = Math.Max(layout.Width + loopRoom, MinWidth);
        var focusMode = focus is not null && nodes.Count > 0;
        // Lines that skip a column arc above or below the boxes; keep room for them.
        // (forward flows bow above, backward flows below).
        var arcsAbove = segments.Any(s => layout.Boxes[s.To].ColumnIndex - layout.Boxes[s.From].ColumnIndex > 1);
        var arcsBelow = segments.Any(s => layout.Boxes[s.From].ColumnIndex - layout.Boxes[s.To].ColumnIndex > 1);
        var top = TitleHeight + HeaderHeight + (arcsAbove ? ArcRoom : 0);
        var height = top + Math.Max(layout.Height, 90) + (arcsBelow ? ArcRoom : 0) + LegendHeight;

        var svg = new StringBuilder();
        svg.Append($"""<svg xmlns="http://www.w3.org/2000/svg" class="integration-map" width="{F(width)}" height="{F(height)}" viewBox="0 0 {F(width)} {F(height)}" font-family="{FontFamily}" font-size="12">""");
        svg.Append("<style>.im-link:hover .im-edge{stroke-width:3.5}.im-link:hover .im-box{stroke:#3f5bd9;stroke-width:2}</style>");
        svg.Append("<defs>");
        foreach (var t in Enum.GetValues<IntegrationType>())
            svg.Append($"""<marker id="im-{t}" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L8,4 L0,8 z" fill="{TypeColor(t)}"/></marker>""");
        svg.Append("</defs>");
        svg.Append($"""<rect width="{F(width)}" height="{F(height)}" fill="#ffffff"/>""");

        svg.Append($"""<text x="{F(MapLayout.Margin)}" y="24" font-size="17" font-weight="600" fill="{Ink}">{E(title)}</text>""");
        var appCount = nodes.Count(n => !n.Id.IsPartner);
        var partnerCount = nodes.Count - appCount;
        static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        var subtitle = Count(appCount, "application", "applications") + (partnerCount > 0 ? $" · {Count(partnerCount, "trading partner", "trading partners")}" : "")
            + $" · {Count(shown.Count, "integration", "integrations")} · {today.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
            + (focusMode ? " · flows run left to right in their recorded direction" : "");
        svg.Append($"""<text x="{F(MapLayout.Margin)}" y="42" fill="{Muted}">{E(subtitle)}</text>""");

        if (nodes.Count == 0)
        {
            svg.Append($"""<text x="{F(MapLayout.Margin)}" y="{F(top + 40)}" fill="{Muted}">No integrations match these filters.</text>""");
            svg.Append("</svg>");
            return new MapPicture(svg.ToString(), 0, 0);
        }

        // Column headings
        for (var c = 0; c < layout.ColumnKeys.Count; c++)
        {
            var key = layout.ColumnKeys[c];
            var heading = focusMode ? MapLayout.FocusColumnTitle(key) : MapLayout.CategoryColumnTitle(key);
            svg.Append($"""<text x="{F(layout.ColumnX(c) + MapLayout.NodeWidth / 2)}" y="{F(TitleHeight + 16)}" text-anchor="middle" font-size="11" font-weight="600" fill="{Muted}">{E(heading.ToUpperInvariant())}</text>""");
        }

        // Lines: parallel segments between the same two boxes fan out so each stays visible.
        var lanes = segments.GroupBy(s => s.From.CompareTo(s.To) < 0 ? (s.From, s.To) : (s.To, s.From))
            .SelectMany(g => g.Select((s, k) => (s, offset: (k - (g.Count() - 1) / 2.0) * 7)))
            .ToDictionary(x => x.s, x => x.offset);
        var risks = new List<(double X, double Y, RiskLevel Level, Integration I)>();
        var boxExtent = (Top: top + layout.Boxes.Values.Min(b => b.Y), Bottom: top + layout.Boxes.Values.Max(b => b.Y) + MapLayout.NodeHeight);
        foreach (var i in shown)
        {
            var color = TypeColor(i.Type);
            var dashed = i.Type == IntegrationType.Manual || i.Status == LifecycleStatus.Planned;
            var opacity = i.Status switch { LifecycleStatus.Retired => 0.3, LifecycleStatus.Retiring => 0.6, _ => 1.0 };
            var level = PortfolioRules.RiskLevel(i, today);
            svg.Append($"""<a class="im-link" href="/portfolio/integrations/{i.Id}"><g opacity="{F(opacity)}"><title>{E(Tooltip(i, level))}</title>""");
            (double X, double Y)? mid = null;
            foreach (var s in segments.Where(s => s.IntegrationId == i.Id))
            {
                var (a, b) = (layout.Boxes[s.From], layout.Boxes[s.To]);
                var (path, m) = Curve(a, b, top, lanes[s], boxExtent);
                // In a dependency view, flows between two neighbors of the same rank are context, not the path to or from the selected node: fade them.
                var side = focusMode && a.ColumnIndex == b.ColumnIndex && s.From != focus && s.To != focus;
                if (!side) mid ??= m;
                var start = i.Direction == FlowDirection.Bidirectional ? $""" marker-start="url(#im-{i.Type})" """ : " ";
                svg.Append($"""<path class="im-edge" d="{path}" fill="none" stroke="{color}" stroke-width="2"{(side ? " opacity=\"0.3\"" : "")}{(dashed ? " stroke-dasharray=\"6 4\"" : "")}{start}marker-end="url(#im-{i.Type})"/>""");
                // A wide invisible stroke makes thin lines easy to hover and click.
                svg.Append($"""<path d="{path}" fill="none" stroke="transparent" stroke-width="10"/>""");
            }
            svg.Append("</g></a>");
            if (level != RiskLevel.Low && mid is { } point) risks.Add((point.X, point.Y, level, i));
        }

        foreach (var (x, y, level, i) in risks)
        {
            var fill = level == RiskLevel.High ? "#c8372d" : "#eda100";
            svg.Append($"""<a href="/portfolio/integrations/{i.Id}"><circle cx="{F(x)}" cy="{F(y)}" r="7" fill="{fill}" stroke="#ffffff" stroke-width="1.5"><title>{E($"{i.Name}: {PortfolioText.Label(level)}")}</title></circle>""");
            svg.Append($"""<text x="{F(x)}" y="{F(y + 4)}" text-anchor="middle" font-size="10" font-weight="700" fill="#ffffff" pointer-events="none">!</text></a>""");
        }

        // Boxes
        foreach (var box in layout.Boxes.Values)
        {
            var (x, y) = (box.X, box.Y + top);
            var isFocus = box.Id == focus;
            if (box.Id.IsPartner) PartnerBox(svg, partnerById[box.Id.Id], x, y, isFocus);
            else AppBox(svg, appById[box.Id.Id], x, y, isFocus);
        }

        Legend(svg, height - LegendHeight + 12, width);
        svg.Append("</svg>");
        return new MapPicture(svg.ToString(), nodes.Count, shown.Count);
    }

    private static void AppBox(StringBuilder svg, Application a, double x, double y, bool isFocus)
    {
        var dim = a.Status is LifecycleStatus.Retiring or LifecycleStatus.Retired ? " opacity=\"0.65\"" : "";
        var dash = a.Status == LifecycleStatus.Planned ? " stroke-dasharray=\"5 3\"" : "";
        svg.Append($"""<a class="im-link" href="/portfolio/applications/{a.Id}"><g{dim}><title>{E(NodeTooltip(a))}</title>""");
        svg.Append($"""<rect class="im-box" x="{F(x)}" y="{F(y)}" width="{F(MapLayout.NodeWidth)}" height="{F(MapLayout.NodeHeight)}" rx="8" fill="{(isFocus ? "#e8ecfd" : "#ffffff")}" stroke="{(isFocus ? "#3f5bd9" : "#c3c8d2")}" stroke-width="{(isFocus ? 2 : 1)}"{dash}/>""");
        svg.Append($"""<rect x="{F(x)}" y="{F(y)}" width="5" height="{F(MapLayout.NodeHeight)}" rx="2" fill="{TierColor(a.Criticality)}"/>""");
        svg.Append($"""<text x="{F(x + 14)}" y="{F(y + 21)}" font-weight="600" fill="{Ink}">{E(Truncate(a.Name, 23))}</text>""");
        var caption = $"{PortfolioText.Label(a.Category)} · {PortfolioText.Label(a.Criticality)}" + (a.Status == LifecycleStatus.Active ? "" : $" · {PortfolioText.Label(a.Status)}");
        svg.Append($"""<text x="{F(x + 14)}" y="{F(y + 38)}" font-size="11" fill="{Muted}">{E(Truncate(caption, 30))}</text>""");
        svg.Append("</g></a>");
    }

    /// <summary>Partners are drawn as lilac pills, so they read as outside companies rather than your systems.</summary>
    private static void PartnerBox(StringBuilder svg, TradingPartner p, double x, double y, bool isFocus)
    {
        var dim = p.Status == PartnerStatus.Inactive ? " opacity=\"0.65\"" : "";
        var dash = p.Status == PartnerStatus.Onboarding ? " stroke-dasharray=\"5 3\"" : "";
        var h = MapLayout.NodeHeight;
        svg.Append($"""<a class="im-link" href="/portfolio/partners/{p.Id}"><g{dim}><title>{E(PartnerTooltip(p))}</title>""");
        svg.Append($"""<rect class="im-box" x="{F(x)}" y="{F(y)}" width="{F(MapLayout.NodeWidth)}" height="{F(h)}" rx="{F(h / 2)}" fill="{(isFocus ? "#e8ecfd" : PartnerFill)}" stroke="{(isFocus ? "#3f5bd9" : PartnerStroke)}" stroke-width="{(isFocus ? 2 : 1)}"{dash}/>""");
        svg.Append($"""<circle cx="{F(x + 16)}" cy="{F(y + h / 2)}" r="5" fill="{ImportanceColor(p.Importance)}"/>""");
        svg.Append($"""<text x="{F(x + 28)}" y="{F(y + 21)}" font-weight="600" fill="{Ink}">{E(Truncate(p.Name, 21))}</text>""");
        var caption = PortfolioText.Label(p.Type) + (p.Importance == PartnerImportance.Key ? " · Key" : "")
            + (p.Status == PartnerStatus.Active ? "" : $" · {PortfolioText.Label(p.Status)}");
        svg.Append($"""<text x="{F(x + 28)}" y="{F(y + 38)}" font-size="11" fill="{Muted}">{E(Truncate(caption, 28))}</text>""");
        svg.Append("</g></a>");
    }

    private sealed record Segment(int IntegrationId, NodeRef From, NodeRef To);

    private static IEnumerable<NodeRef> Ends(Integration i) =>
        i.ViaNode is { } via ? [i.SourceNode, via, i.TargetNode] : [i.SourceNode, i.TargetNode];

    /// <summary>Source → via → target when the hop is on the picture, otherwise straight source → target.</summary>
    private static IEnumerable<Segment> Segments(Integration i, IReadOnlyDictionary<NodeRef, int> columns)
    {
        if (i.ViaNode is { } via && columns.ContainsKey(via))
        {
            yield return new Segment(i.Id, i.SourceNode, via);
            yield return new Segment(i.Id, via, i.TargetNode);
        }
        else yield return new Segment(i.Id, i.SourceNode, i.TargetNode);
    }

    /// <summary>A smooth curve between the facing sides of two boxes (or out and back on the right when they share a column), and its midpoint.</summary>
    private static (string Path, (double X, double Y) Mid) Curve(NodeBox a, NodeBox b, double top, double lane, (double Top, double Bottom) boxes)
    {
        var (w, h) = (MapLayout.NodeWidth, MapLayout.NodeHeight);
        var ay = a.Y + top + h / 2 + lane;
        var by = b.Y + top + h / 2 + lane;
        double x0, x1, c0, c1;
        double cy0 = ay, cy1 = by;
        var span = Math.Abs(b.ColumnIndex - a.ColumnIndex);
        if (b.ColumnIndex > a.ColumnIndex) { x0 = a.X + w; x1 = b.X - 2; c0 = x0 + (x1 - x0) / 2; c1 = x1 - (x1 - x0) / 2; }
        else if (b.ColumnIndex < a.ColumnIndex) { x0 = a.X; x1 = b.X + w + 2; c0 = x0 - (x0 - x1) / 2; c1 = x1 + (x0 - x1) / 2; }
        else
        {
            x0 = a.X + w; x1 = b.X + w + 2;
            var bulge = 40 + 8 * Math.Abs(a.Row - b.Row);
            c0 = x0 + bulge; c1 = x1 + bulge;
        }
        if (span > 1)
        {
            // Skipping a column: arc clear of every box (forward flows above, backward flows below) instead of through them.
            // A cubic with both control points at cy peaks at (ay + by) / 8 + 0.75 * cy; solve for a peak just past the boxes.
            var clearance = ArcClearance + 6 * (span - 2) + Math.Abs(lane);
            var peak = b.ColumnIndex > a.ColumnIndex ? boxes.Top - clearance : boxes.Bottom + clearance;
            cy0 = cy1 = (peak - (ay + by) / 8) / 0.75;
            (c0, c1) = (x0 + (x1 - x0) / 4, x1 - (x1 - x0) / 4);
        }
        var mid = ((x0 + 3 * c0 + 3 * c1 + x1) / 8, (ay + 3 * cy0 + 3 * cy1 + by) / 8);
        return ($"M{F(x0)},{F(ay)} C{F(c0)},{F(cy0)} {F(c1)},{F(cy1)} {F(x1)},{F(by)}", mid);
    }

    private static string Tooltip(Integration i, RiskLevel level)
    {
        var route = $"{i.SourceName} {(i.Direction == FlowDirection.Bidirectional ? "⇄" : "→")} {i.TargetName}"
            + (i.Via is { } via ? $" (via {via.Name})" : "");
        var facts = string.Join(" · ", new[] { PortfolioText.Label(i.Type), i.MessageFormat, PortfolioText.Label(i.Frequency), PortfolioText.Label(i.Criticality) }
            .Where(s => !string.IsNullOrEmpty(s)));
        return $"{i.Name}\n{route}\n{facts}\n{i.BusinessPurpose}" + (level == RiskLevel.Low ? "" : $"\n{PortfolioText.Label(level)}");
    }

    private static string NodeTooltip(Application a) =>
        $"{a.Name}\n{PortfolioText.Label(a.Category)} · {PortfolioText.Label(a.Criticality)} · {PortfolioText.Label(a.Status)}"
        + (a.Vendor is { } v ? $"\n{v.Name}" : "");

    private static string PartnerTooltip(TradingPartner p) =>
        $"{p.Name}\n{PortfolioText.Label(p.Type)} · {PortfolioText.Label(p.Importance)} · {PortfolioText.Label(p.Status)}"
        + (p.EdiAddress is { } edi ? $"\nEDI {edi}" : "");

    private static void Legend(StringBuilder svg, double y, double width)
    {
        svg.Append($"""<line x1="0" x2="{F(width)}" y1="{F(y - 10)}" y2="{F(y - 10)}" stroke="{Line}"/>""");
        var x = MapLayout.Margin;
        foreach (var t in Enum.GetValues<IntegrationType>())
        {
            var dash = t == IntegrationType.Manual ? " stroke-dasharray=\"6 4\"" : "";
            svg.Append($"""<line x1="{F(x)}" x2="{F(x + 26)}" y1="{F(y + 6)}" y2="{F(y + 6)}" stroke="{TypeColor(t)}" stroke-width="2"{dash} marker-end="url(#im-{t})"/>""");
            svg.Append($"""<text x="{F(x + 34)}" y="{F(y + 10)}" fill="{Ink}">{E(PortfolioText.Label(t))}</text>""");
            x += 34 + 8 * PortfolioText.Label(t).Length + 22;
        }
        svg.Append($"""<text x="{F(x)}" y="{F(y + 10)}" fill="{Muted}">Dashed: manual, planned or onboarding · arrows at both ends: two-way</text>""");

        var y2 = y + 28;
        x = MapLayout.Margin;
        foreach (var c in Enum.GetValues<Criticality>())
        {
            svg.Append($"""<rect x="{F(x)}" y="{F(y2)}" width="5" height="14" rx="2" fill="{TierColor(c)}"/>""");
            svg.Append($"""<text x="{F(x + 11)}" y="{F(y2 + 11)}" fill="{Ink}">{PortfolioText.Label(c)}</text>""");
            x += 70;
        }
        svg.Append($"""<rect x="{F(x)}" y="{F(y2)}" width="34" height="14" rx="7" fill="{PartnerFill}" stroke="{PartnerStroke}"/><circle cx="{F(x + 8)}" cy="{F(y2 + 7)}" r="3.5" fill="{ImportanceColor(PartnerImportance.Key)}"/>""");
        svg.Append($"""<text x="{F(x + 42)}" y="{F(y2 + 11)}" fill="{Ink}">Trading partner (dark dot: key)</text>""");
        x += 42 + 7 * 30 + 24;
        foreach (var (label, fill) in new[] { ("High risk", "#c8372d"), ("Medium risk or review overdue", "#eda100") })
        {
            svg.Append($"""<circle cx="{F(x + 7)}" cy="{F(y2 + 7)}" r="7" fill="{fill}"/><text x="{F(x + 7)}" y="{F(y2 + 11)}" text-anchor="middle" font-size="10" font-weight="700" fill="#ffffff">!</text>""");
            svg.Append($"""<text x="{F(x + 20)}" y="{F(y2 + 11)}" fill="{Ink}">{label}</text>""");
            x += 20 + 7 * label.Length + 24;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    private static string E(string s) => SecurityElement.Escape(s) ?? "";
    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
