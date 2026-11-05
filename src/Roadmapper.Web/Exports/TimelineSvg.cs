using System.Globalization;
using System.Security;
using System.Text;
using Roadmapper.Core;
using Roadmapper.Web.Components.Shared;

namespace Roadmapper.Web.Exports;

/// <summary>
/// A self-contained SVG of the timeline (no external CSS or fonts), suitable for download or for
/// rasterizing to PNG in the browser. Mirrors the on-screen timeline's layout.
/// </summary>
public static class TimelineSvg
{
    private const double LabelWidth = 300, TitleHeight = 52, HeaderHeight = 44, RowHeight = 34;
    private const string FontFamily = "Segoe UI, system-ui, -apple-system, sans-serif";

    public static string Render(ProductRoadmap roadmap, bool quarterScale, ItemType deepest, DateOnly today,
        IReadOnlyDictionary<int, SnapshotItem>? baseline = null, string? baselineName = null)
    {
        var rows = roadmap.Flatten(deepest: deepest);
        var pxPerDay = quarterScale ? TimelineLayout.QuarterPxPerDay : TimelineLayout.MonthPxPerDay;
        var spans = rows.Select(r => TimelineLayout.Span(r.Item))
            .Concat(rows.Select(r => baseline?.GetValueOrDefault(r.Item.Id) is { } b ? TimelineLayout.Span(b.StartDate, b.EndDate) : null))
            .OfType<(DateOnly Start, DateOnly End)>();
        var range = TimelineLayout.Range(spans, today);
        var chartWidth = (range.End.DayNumber - range.Start.DayNumber) * pxPerDay;
        var width = LabelWidth + chartWidth;
        var top = TitleHeight + HeaderHeight;
        var height = top + Math.Max(rows.Count, 1) * RowHeight + 12;
        double X(DateOnly d) => LabelWidth + (d.DayNumber - range.Start.DayNumber) * pxPerDay;
        double RowY(int index) => top + index * RowHeight;

        var svg = new StringBuilder();
        svg.Append($"""<svg xmlns="http://www.w3.org/2000/svg" width="{F(width)}" height="{F(height)}" viewBox="0 0 {F(width)} {F(height)}" font-family="{FontFamily}" font-size="12">""");
        svg.Append("""<defs><marker id="a" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L8,4 L0,8 z" fill="#9aa3b2"/></marker>""");
        svg.Append("""<marker id="b" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L8,4 L0,8 z" fill="#c8372d"/></marker></defs>""");
        svg.Append($"""<rect width="{F(width)}" height="{F(height)}" fill="#ffffff"/>""");

        // Title
        svg.Append($"""<text x="12" y="24" font-size="17" font-weight="600" fill="#1d2330">{E(roadmap.Product.Name)}</text>""");
        var subtitle = $"Timeline · {today:MMM d, yyyy}" + (baselineName is null ? "" : $" · thin gray bars = baseline “{baselineName}”");
        svg.Append($"""<text x="12" y="42" fill="#667085">{E(subtitle)}</text>""");

        // Month header and gridlines
        svg.Append($"""<rect x="0" y="{F(TitleHeight)}" width="{F(width)}" height="{F(HeaderHeight)}" fill="#f1f3f6"/>""");
        svg.Append($"""<text x="12" y="{F(TitleHeight + 30)}" font-size="11" font-weight="600" fill="#667085">ITEM</text>""");
        for (var m = range.Start; m < range.End; m = m.AddMonths(1))
        {
            var x = X(m);
            svg.Append($"""<line x1="{F(x)}" x2="{F(x)}" y1="{F(TitleHeight)}" y2="{F(height)}" stroke="#e1e4ea"/>""");
            svg.Append($"""<text x="{F(x + 6)}" y="{F(TitleHeight + 18)}" font-size="11" font-weight="600" fill="#1d2330">{m.ToString(quarterScale ? "MMM" : "MMMM", CultureInfo.InvariantCulture)}</text>""");
            if (TimelineLayout.MonthCaption(m, range.Start, quarterScale) is { Length: > 0 } caption)
                svg.Append($"""<text x="{F(x + 6)}" y="{F(TitleHeight + 34)}" font-size="11" fill="#667085">{caption}</text>""");
        }
        svg.Append($"""<line x1="{F(LabelWidth)}" x2="{F(LabelWidth)}" y1="{F(TitleHeight)}" y2="{F(height)}" stroke="#c3c2b7"/>""");

        // Rows: label, separator, baseline, bar
        for (var r = 0; r < rows.Count; r++)
        {
            var (item, depth, hasChildren) = (rows[r].Item, rows[r].Depth, rows[r].HasChildren);
            var y = RowY(r);
            svg.Append($"""<line x1="0" x2="{F(width)}" y1="{F(y + RowHeight)}" y2="{F(y + RowHeight)}" stroke="#f0f1f4"/>""");
            var lx = 10 + depth * 16;
            svg.Append($"""<circle cx="{F(lx + 4)}" cy="{F(y + 17)}" r="4" fill="{Ui.TypeColor(item.ItemType)}"/>""");
            svg.Append($"""<text x="{F(lx + 14)}" y="{F(y + 21)}" fill="#1d2330"{(depth == 0 ? " font-weight=\"600\"" : "")}>{E(Truncate(item.Title, (int)((LabelWidth - lx - 24) / 6.5)))}</text>""");

            if (baseline?.GetValueOrDefault(item.Id) is { } b && TimelineLayout.Span(b.StartDate, b.EndDate) is { } bs)
                svg.Append($"""<rect x="{F(X(bs.Start))}" y="{F(y + 28)}" width="{F((bs.End.DayNumber - bs.Start.DayNumber + 1) * pxPerDay)}" height="4" rx="1" fill="#9aa3b2"/>""");

            if (TimelineLayout.Span(item) is not { } span) continue;
            var x = X(span.Start);
            var w = Math.Max((span.End.DayNumber - span.Start.DayNumber + 1) * pxPerDay, 3);
            var (barY, barH) = hasChildren ? (y + 12, 10.0) : (y + 8, 18.0);
            var opacity = item.Status == ItemStatus.Cancelled ? " opacity=\"0.35\"" : "";
            svg.Append($"""<rect x="{F(x)}" y="{F(barY)}" width="{F(w)}" height="{F(barH)}" rx="{(hasChildren ? 2 : 4)}" fill="{Ui.ColorOf(item)}"{opacity}/>""");
            if (item.ProgressPct > 0)
                svg.Append($"""<rect x="{F(x)}" y="{F(barY)}" width="{F(w * item.ProgressPct / 100.0)}" height="{F(barH)}" rx="{(hasChildren ? 2 : 4)}" fill="#ffffff" fill-opacity="0.3"/>""");
            if (!hasChildren)
                svg.Append($"""<text x="{F(x + w + 6)}" y="{F(y + 21)}" font-size="11" fill="#52514e">{E(Truncate(item.Title, 40))}</text>""");
        }

        // Dependencies
        var rowIndex = rows.Select((row, i) => (row.Item.Id, i)).ToDictionary(p => p.Id, p => p.i);
        foreach (var dep in roadmap.Dependencies)
        {
            if (!rowIndex.TryGetValue(dep.PredecessorId, out var pi) || !rowIndex.TryGetValue(dep.SuccessorId, out var si)) continue;
            if (TimelineLayout.Span(rows[pi].Item) is not { } ps || TimelineLayout.Span(rows[si].Item) is not { } ss) continue;
            var x1 = X(ps.End) + pxPerDay;
            var x2 = X(ss.Start);
            var y1 = RowY(pi) + RowHeight / 2;
            var y2 = RowY(si) + RowHeight / 2;
            var bad = roadmap.HasScheduleConflict(dep);
            svg.Append($"""<path d="M {F(x1)} {F(y1)} C {F(x1 + 28)} {F(y1)}, {F(x2 - 28)} {F(y2)}, {F(x2)} {F(y2)}" fill="none" stroke="{(bad ? "#c8372d" : "#9aa3b2")}" stroke-width="1.5"{(bad ? " stroke-dasharray=\"4 3\"" : "")} marker-end="url(#{(bad ? "b" : "a")})"/>""");
        }

        if (today >= range.Start && today < range.End)
            svg.Append($"""<line x1="{F(X(today))}" x2="{F(X(today))}" y1="{F(TitleHeight)}" y2="{F(height)}" stroke="#c8372d" stroke-width="2" stroke-opacity="0.5"/>""");

        if (rows.Count == 0)
            svg.Append($"""<text x="12" y="{F(top + 22)}" fill="#667085">No items to show.</text>""");

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string E(string s) => SecurityElement.Escape(s) ?? "";
    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..Math.Max(n - 1, 1)] + "…";
}
