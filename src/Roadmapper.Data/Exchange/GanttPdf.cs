using System.Globalization;
using PdfSharp.Drawing;
using Roadmapper.Core;

namespace Roadmapper.Data.Exchange;

/// <summary>What the report's Gantt shows; mirrors the Timeline tab's "Show down to" and "Compare to" pickers.</summary>
public sealed record GanttOptions(
    ItemType Deepest = ItemType.Task,
    IReadOnlyDictionary<int, SnapshotItem>? Baseline = null,
    string? BaselineName = null);

/// <summary>
/// Draws the timeline as a Gantt chart on landscape Letter pages. The whole date range is scaled to the page
/// width; rows shrink to fit the page height and only spill onto further pages when they'd become unreadable.
/// Each page repeats the title, key and month header. Coordinates are in points (1/72").
/// </summary>
internal sealed class GanttPdf
{
    public const double PageWidth = 792, PageHeight = 612;
    private const double Margin = 42.5; // 1.5 cm, matching the report's MigraDoc margins
    private const double Left = Margin, Right = PageWidth - Margin, Bottom = PageHeight - Margin;
    private const double LabelWidth = 190, ChartLeft = Left + LabelWidth, ChartWidth = Right - ChartLeft;
    private const double HeaderHeight = 24, MinRow = 10, MaxRow = 32;
    private const string Font = "Segoe UI";

    private static readonly XColor Ink = Hex("#1d2330"), Muted = Hex("#667085"), Hairline = Hex("#e1e4ea"),
        RowLine = Hex("#f0f1f4"), HeadFill = Hex("#f1f3f6"), GoalFill = Hex("#fbfaff"),
        Divider = Hex("#c3c2b7"), Early = Hex("#1f8a4c");

    private readonly ProductRoadmap _roadmap;
    private readonly GanttOptions _options;
    private readonly DateOnly _today;
    private readonly List<TreeRow> _rows;
    private readonly Dictionary<int, int> _rowIndex;
    private readonly (DateOnly Start, DateOnly End) _range;
    private readonly double _ptPerDay, _rowHeight, _rowsTop, _legendTop;
    private readonly int _rowsPerPage;
    private readonly List<LegendLine> _legend;

    private sealed record LegendEntry(Action<XGraphics, double, double> Swatch, string Text);
    private sealed record LegendLine(string Heading, List<LegendEntry> Entries, bool Note = false);

    public GanttPdf(ProductRoadmap roadmap, GanttOptions options, DateOnly today)
    {
        _roadmap = roadmap;
        _options = options;
        _today = today;
        _rows = roadmap.Flatten(deepest: options.Deepest);
        _rowIndex = _rows.Select((r, i) => (r.Item.Id, i)).ToDictionary(p => p.Id, p => p.i);
        _range = TimelineLayout.Range(
            _rows.Select(r => TimelineLayout.Span(r.Item))
                .Concat(_rows.Select(r => BaselineOf(r.Item) is { } b ? TimelineLayout.Span(b.StartDate, b.EndDate) : null))
                .OfType<(DateOnly Start, DateOnly End)>(),
            today);
        _ptPerDay = ChartWidth / (_range.End.DayNumber - _range.Start.DayNumber);

        _legend = BuildLegend();
        _legendTop = Margin + 34;
        var legendHeight = MeasureLegend();
        _rowsTop = _legendTop + legendHeight + 6 + HeaderHeight;

        var available = Bottom - _rowsTop;
        _rowHeight = Math.Clamp(available / Math.Max(_rows.Count, 1), MinRow, MaxRow);
        _rowsPerPage = Math.Max(1, (int)Math.Floor(available / _rowHeight));
        PageCount = Math.Max(1, (int)Math.Ceiling(_rows.Count / (double)_rowsPerPage));
    }

    public int PageCount { get; }

    public void Draw(XGraphics g, int page)
    {
        var first = page * _rowsPerPage;
        var last = Math.Min(first + _rowsPerPage, _rows.Count); // exclusive
        var rowsBottom = _rowsTop + Math.Max(last - first, 1) * _rowHeight;

        DrawTitle(g, page);
        DrawLegend(g);
        DrawHeader(g, rowsBottom);
        for (var r = first; r < last; r++) DrawRow(g, r, _rowsTop + (r - first) * _rowHeight);
        DrawDependencies(g, first, last, rowsBottom);

        if (_today >= _range.Start && _today < _range.End)
        {
            var x = X(_today);
            g.DrawLine(new XPen(XColor.FromArgb(150, Hex(RoadmapColors.Conflict)), 1.3), x, _rowsTop - HeaderHeight, x, rowsBottom);
            Triangle(g, Hex(RoadmapColors.Conflict), x, _rowsTop - 1, pointing: Direction.Down, size: 3.5);
        }

        g.DrawLine(new XPen(Divider, 0.6), ChartLeft, _rowsTop - HeaderHeight, ChartLeft, rowsBottom);
        g.DrawRectangle(new XPen(Hairline, 0.6), Left, _rowsTop - HeaderHeight, Right - Left, rowsBottom - _rowsTop + HeaderHeight);

        if (_rows.Count == 0)
            g.DrawString("No items to show.", new XFont(Font, 9), new XSolidBrush(Muted), Left + 6, _rowsTop + 14);
    }

    // ---- page furniture -------------------------------------------------------------------------------------

    private void DrawTitle(XGraphics g, int page)
    {
        g.DrawString(_roadmap.Product.Name, new XFont(Font, 15, XFontStyleEx.Bold), new XSolidBrush(Ink), Left, Margin + 13);
        var parts = new List<string>
        {
            "Timeline",
            $"{_range.Start:MMM yyyy} – {_range.End.AddDays(-1):MMM yyyy}",
            $"as of {_today:MMM d, yyyy}",
            $"showing down to {_options.Deepest}s",
        };
        if (_options.BaselineName is { } b) parts.Add($"baseline “{b}”");
        if (PageCount > 1) parts.Add($"chart page {page + 1} of {PageCount}");
        g.DrawString(string.Join("  ·  ", parts), new XFont(Font, 8), new XSolidBrush(Muted), Left, Margin + 26);
    }

    private List<LegendLine> BuildLegend()
    {
        var neutral = Hex("#667085");
        var key = new List<LegendEntry>
        {
            new((g, x, y) => Bar(g, neutral, x, y - 1.5, 20, 3, 1), "Parent (spans its children)"),
            new((g, x, y) => { Bar(g, neutral, x, y - 3.5, 20, 7, 2); Bar(g, XColor.FromArgb(90, XColors.White), x, y - 3.5, 11, 7, 2); },
                "Item (lighter part = progress)"),
        };
        // Symbols other than bars only appear in the key when the chart uses them.
        var items = _rows.Select(r => r.Item).ToList();
        var shownDeps = _roadmap.Dependencies.Where(d => _rowIndex.ContainsKey(d.PredecessorId) && _rowIndex.ContainsKey(d.SuccessorId)).ToList();
        if (_options.Baseline is not null)
            key.Add(new((g, x, y) => Bar(g, Hex(RoadmapColors.Baseline), x, y - 1, 20, 2, 0.5), $"Baseline “{_options.BaselineName}”"));
        if (_today >= _range.Start && _today < _range.End)
            key.Add(new((g, x, y) => g.DrawLine(new XPen(XColor.FromArgb(150, Hex(RoadmapColors.Conflict)), 1.3), x + 10, y - 5, x + 10, y + 5), "Today"));
        if (shownDeps.Any(d => !_roadmap.HasScheduleConflict(d)))
            key.Add(new((g, x, y) => Arrow(g, x, y, x + 20, y, conflict: false), "Dependency (predecessor → successor)"));
        if (shownDeps.Any(_roadmap.HasScheduleConflict))
            key.Add(new((g, x, y) => Arrow(g, x, y, x + 20, y, conflict: true), "Dependency where the successor starts before the predecessor ends"));
        if (items.Any(i => ProductRoadmap.IsOpen(i) && _roadmap.IsBlocked(i.Id)))
            key.Add(new((g, x, y) => { Bar(g, neutral, x, y - 3.5, 14, 7, 2); BlockedOutline(g, x, y - 3.5, 14, 7); NoEntry(g, x + 18.5, y, 3.2); },
                "Blocked (waiting on an unfinished predecessor)"));
        if (_options.Baseline is not null)
            key.Add(new((g, x, y) => g.DrawString("+5d", new XFont(Font, 6.5), new XSolidBrush(Hex(RoadmapColors.Blocked)), x + 4, y + 2.5),
                "End date moved vs. baseline"));
        if (items.Any(i => i.Status == ItemStatus.Cancelled))
            key.Add(new((g, x, y) => Bar(g, XColor.FromArgb(90, neutral), x, y - 3.5, 20, 7, 2), "Cancelled"));

        // Colors actually used: themes first, then the type colors of items without a theme.
        var colors = new List<LegendEntry>();
        foreach (var theme in _rows.Select(r => r.Item.Theme).OfType<Theme>().DistinctBy(t => t.Id).OrderBy(t => t.Name))
            colors.Add(new((g, x, y) => Bar(g, Hex(theme.Color), x, y - 3.5, 20, 7, 2), theme.Name));
        foreach (var type in _rows.Where(r => r.Item.Theme is null).Select(r => r.Item.ItemType).Distinct().Order())
            colors.Add(new((g, x, y) => Bar(g, Hex(RoadmapColors.TypeColor(type)), x, y - 3.5, 20, 7, 2), $"{type} (no theme)"));

        var types = Enum.GetValues<ItemType>().Where(t => t <= _options.Deepest)
            .Select(t => new LegendEntry((g, x, y) => g.DrawEllipse(new XSolidBrush(Hex(RoadmapColors.TypeColor(t))), x + 7, y - 3, 6, 6), t.ToString()))
            .ToList();

        var lines = new List<LegendLine> { new("Key", key) };
        if (colors.Count > 0) lines.Add(new("Bar colors", colors));
        lines.Add(new("Item type dots", types));
        return lines;
    }

    private const double LegendLineHeight = 11, LegendHeadingWidth = 62, SwatchWidth = 22;
    private static readonly XFont LegendFont = new(Font, 7);
    private static readonly XFont LegendHeadingFont = new(Font, 7, XFontStyleEx.Bold);

    /// <summary>Lays out the legend as wrapped lines; returns each entry's position (or just the height when measuring).</summary>
    private IEnumerable<(LegendEntry Entry, double X, double Y, string? Heading)> LayoutLegend(XGraphics g)
    {
        var y = _legendTop;
        foreach (var line in _legend)
        {
            var x = Left + LegendHeadingWidth;
            yield return (line.Entries.FirstOrDefault()!, Left, y, line.Heading);
            foreach (var e in line.Entries)
            {
                var w = SwatchWidth + 4 + g.MeasureString(e.Text, LegendFont).Width + 12;
                if (x + w > Right && x > Left + LegendHeadingWidth) { x = Left + LegendHeadingWidth; y += LegendLineHeight; }
                yield return (e, x, y, null);
                x += w;
            }
            y += LegendLineHeight;
        }
        yield return (null!, 0, y, null); // sentinel: y is the bottom
    }

    private double MeasureLegend()
    {
        var g = XGraphics.CreateMeasureContext(new XSize(PageWidth, PageHeight), XGraphicsUnit.Point, XPageDirection.Downwards);
        return LayoutLegend(g).Last().Y - _legendTop;
    }

    private void DrawLegend(XGraphics g)
    {
        var ink = new XSolidBrush(Ink);
        foreach (var (entry, x, y, heading) in LayoutLegend(g))
        {
            if (entry is null) break;
            var mid = y + LegendLineHeight / 2;
            if (heading is not null)
            {
                g.DrawString(heading, LegendHeadingFont, new XSolidBrush(Muted), x, mid + 2.5);
                continue;
            }
            entry.Swatch(g, x, mid);
            g.DrawString(entry.Text, LegendFont, ink, x + SwatchWidth + 4, mid + 2.5);
        }
    }

    private void DrawHeader(XGraphics g, double rowsBottom)
    {
        var top = _rowsTop - HeaderHeight;
        g.DrawRectangle(new XSolidBrush(HeadFill), Left, top, Right - Left, HeaderHeight);
        g.DrawString("ITEM", new XFont(Font, 7, XFontStyleEx.Bold), new XSolidBrush(Muted), Left + 5, top + 15);

        // Label density follows the space each month gets on the page.
        var monthWidth = 30.4 * _ptPerDay;
        var bold = new XFont(Font, 7, XFontStyleEx.Bold);
        var small = new XFont(Font, 6.5);
        var grid = new XPen(Hairline, 0.5);
        var quarterGrid = new XPen(Hex("#cfd4dc"), 0.6);
        for (var m = _range.Start; m < _range.End; m = m.AddMonths(1))
        {
            var x = X(m);
            var isQuarter = m.Month % 3 == 1;
            var showMonth = monthWidth >= 26;
            var showQuarter = !showMonth && monthWidth >= 9 && isQuarter;

            if (monthWidth >= 6 || isQuarter)
                g.DrawLine(isQuarter && !showMonth ? quarterGrid : grid, x, top, x, rowsBottom);

            string? line1 = showMonth ? m.ToString(monthWidth >= 44 ? "MMMM" : "MMM", CultureInfo.InvariantCulture)
                : showQuarter ? $"Q{(m.Month + 2) / 3}"
                : monthWidth < 9 && m.Month == 1 ? m.Year.ToString() : null;
            if (line1 is not null && x + 3 < Right - 8)
                g.DrawString(line1, bold, new XSolidBrush(Ink), x + 2.5, top + 10);

            string? line2 = showMonth ? TimelineLayout.MonthCaption(m, _range.Start, quarterScale: monthWidth < 44)
                : showQuarter && (m.Month == 1 || m == _range.Start) ? m.Year.ToString()
                : null;
            if (line2 is { Length: > 0 } && x + 3 < Right - 8)
                g.DrawString(line2, small, new XSolidBrush(Muted), x + 2.5, top + 20);
        }
    }

    // ---- rows ---------------------------------------------------------------------------------------------------

    private void DrawRow(XGraphics g, int r, double y)
    {
        var (item, depth, hasChildren) = (_rows[r].Item, _rows[r].Depth, _rows[r].HasChildren);
        var fontSize = Math.Clamp(_rowHeight * 0.5, 5.5, 9);
        var mid = y + _rowHeight / 2;

        if (depth == 0) g.DrawRectangle(new XSolidBrush(GoalFill), Left, y, Right - Left, _rowHeight);
        g.DrawLine(new XPen(RowLine, 0.5), Left, y + _rowHeight, Right, y + _rowHeight);

        // Label column: type dot, title, then blocked marker and baseline slip at the right edge.
        var blocked = ProductRoadmap.IsOpen(item) && _roadmap.IsBlocked(item.Id);
        var labelRight = ChartLeft - 4;
        var smallFont = new XFont(Font, Math.Max(fontSize - 1, 5));
        if (SlipDays(item) is { } slip && slip != 0)
        {
            var text = (slip > 0 ? "+" : "−") + Math.Abs(slip) + "d";
            var w = g.MeasureString(text, smallFont).Width;
            g.DrawString(text, smallFont, new XSolidBrush(slip > 0 ? Hex(RoadmapColors.Blocked) : Early), labelRight - w, mid + fontSize * 0.35);
            labelRight -= w + 3;
        }
        if (blocked)
        {
            var radius = Math.Min(3.4, _rowHeight * 0.3);
            NoEntry(g, labelRight - radius, mid, radius);
            labelRight -= radius * 2 + 3;
        }

        var lx = Left + 5 + depth * 9;
        var dot = Math.Min(2.6, _rowHeight * 0.2);
        g.DrawEllipse(new XSolidBrush(Hex(RoadmapColors.TypeColor(item.ItemType))), lx, mid - dot, dot * 2, dot * 2);
        var font = new XFont(Font, fontSize, depth == 0 ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        var color = item.Status == ItemStatus.Cancelled ? Muted : Ink;
        g.DrawString(Fit(g, item.Title, font, labelRight - (lx + dot * 2 + 4)), font, new XSolidBrush(color), lx + dot * 2 + 4, mid + fontSize * 0.35);

        // Chart: baseline, bar, progress, outline, label.
        if (BaselineOf(item) is { } b && TimelineLayout.Span(b.StartDate, b.EndDate) is { } bs)
            Bar(g, Hex(RoadmapColors.Baseline), X(bs.Start), y + _rowHeight - 3, Width(bs), Math.Min(2, _rowHeight * 0.15), 0.5);

        if (TimelineLayout.Span(item) is not { } span)
        {
            g.DrawString("no dates", new XFont(Font, Math.Max(fontSize - 1, 5), XFontStyleEx.Italic), new XSolidBrush(Muted), ChartLeft + 4, mid + fontSize * 0.35);
            return;
        }

        var x = X(span.Start);
        var bw = Math.Max(Width(span), 1.5);
        var bh = Math.Min(hasChildren ? _rowHeight * 0.28 : _rowHeight * 0.56, hasChildren ? 6 : 13);
        var by = mid - bh / 2;
        var radiusBar = hasChildren ? 1 : Math.Min(2.5, bh / 3);
        var fill = Hex(RoadmapColors.ColorOf(item));
        if (item.Status == ItemStatus.Cancelled) fill = XColor.FromArgb(90, fill);
        Bar(g, fill, x, by, bw, bh, radiusBar);
        if (item.ProgressPct > 0)
            Bar(g, XColor.FromArgb(77, XColors.White), x, by, bw * item.ProgressPct / 100.0, bh, radiusBar);
        if (blocked) BlockedOutline(g, x, by, bw, bh);

        if (!hasChildren)
        {
            var labelFont = new XFont(Font, Math.Max(fontSize - 1, 5));
            var text = item.Title + (item.ProgressPct is > 0 and < 100 ? $"  {item.ProgressPct}%" : "");
            var tw = g.MeasureString(text, labelFont).Width;
            var tx = x + bw + 3;
            if (tx + tw > Right - 2) tx = x - tw - 3;           // no room on the right: put it before the bar
            if (tx >= ChartLeft + 2)
                g.DrawString(text, labelFont, new XSolidBrush(Hex("#52514e")), tx, mid + fontSize * 0.3);
        }
    }

    /// <summary>
    /// Arrows from each predecessor's end to its successor's start. When one end is on another chart page the
    /// arrow enters or leaves through the top or bottom edge of the rows, so the link stays visible.
    /// </summary>
    private void DrawDependencies(XGraphics g, int first, int last, double rowsBottom)
    {
        foreach (var dep in _roadmap.Dependencies)
        {
            if (!_rowIndex.TryGetValue(dep.PredecessorId, out var pi) || !_rowIndex.TryGetValue(dep.SuccessorId, out var si)) continue;
            var predHere = pi >= first && pi < last;
            var succHere = si >= first && si < last;
            if (!predHere && !succHere) continue;
            if (TimelineLayout.Span(_rows[pi].Item) is not { } ps || TimelineLayout.Span(_rows[si].Item) is not { } ss) continue;

            double RowMid(int index, bool here) =>
                here ? _rowsTop + (index - first + 0.5) * _rowHeight : index < first ? _rowsTop : rowsBottom;

            var x1 = X(ps.End) + _ptPerDay;
            var x2 = X(ss.Start);
            Arrow(g, x1, RowMid(pi, predHere), x2, RowMid(si, succHere), _roadmap.HasScheduleConflict(dep), head: succHere);
        }
    }

    // ---- drawing helpers -------------------------------------------------------------------------------------

    private enum Direction { Right, Down }

    private static void Arrow(XGraphics g, double x1, double y1, double x2, double y2, bool conflict, bool head = true)
    {
        var color = Hex(conflict ? RoadmapColors.Conflict : RoadmapColors.Dependency);
        var pen = new XPen(color, 0.9) { DashStyle = conflict ? XDashStyle.Dash : XDashStyle.Solid };
        var c = Math.Clamp(Math.Abs(x2 - x1) / 2, 6, 14);
        var end = head ? x2 - 3 : x2; // stop the line where the arrowhead begins
        g.DrawBezier(pen, new XPoint(x1, y1), new XPoint(x1 + c, y1), new XPoint(end - c, y2), new XPoint(end, y2));
        if (head) Triangle(g, color, x2, y2, Direction.Right, 3.2);
    }

    private static void Triangle(XGraphics g, XColor color, double x, double y, Direction pointing, double size)
    {
        XPoint[] pts = pointing == Direction.Right
            ? [new(x, y), new(x - size, y - size * 0.75), new(x - size, y + size * 0.75)]
            : [new(x, y), new(x - size * 0.75, y - size), new(x + size * 0.75, y - size)];
        g.DrawPolygon(new XSolidBrush(color), pts, XFillMode.Winding);
    }

    private static void Bar(XGraphics g, XColor color, double x, double y, double w, double h, double radius) =>
        g.DrawRoundedRectangle(new XSolidBrush(color), x, y, w, h, radius * 2, radius * 2);

    private static void BlockedOutline(XGraphics g, double x, double y, double w, double h) =>
        g.DrawRectangle(new XPen(Hex(RoadmapColors.Blocked), 1.1), x - 0.8, y - 0.8, w + 1.6, h + 1.6);

    /// <summary>A "no entry" sign: red disc with a white bar.</summary>
    private static void NoEntry(XGraphics g, double cx, double cy, double r)
    {
        g.DrawEllipse(new XSolidBrush(Hex(RoadmapColors.Blocked)), cx - r, cy - r, r * 2, r * 2);
        g.DrawRectangle(XBrushes.White, cx - r * 0.6, cy - r * 0.18, r * 1.2, r * 0.36);
    }

    private double X(DateOnly d) => ChartLeft + (d.DayNumber - _range.Start.DayNumber) * _ptPerDay;

    private double Width((DateOnly Start, DateOnly End) s) => (s.End.DayNumber - s.Start.DayNumber + 1) * _ptPerDay;

    private SnapshotItem? BaselineOf(RoadmapItem item) => _options.Baseline?.GetValueOrDefault(item.Id);

    private int? SlipDays(RoadmapItem item) =>
        BaselineOf(item) is { EndDate: { } before } && item.EndDate is { } after ? after.DayNumber - before.DayNumber : null;

    /// <summary>Truncates with an ellipsis to fit <paramref name="width"/> points.</summary>
    private static string Fit(XGraphics g, string text, XFont font, double width)
    {
        if (g.MeasureString(text, font).Width <= width) return text;
        var n = text.Length;
        while (n > 1 && g.MeasureString(text[..n] + "…", font).Width > width) n--;
        return text[..n] + "…";
    }

    private static XColor Hex(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        return h.Length == 6 && int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? XColor.FromArgb((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff)
            : XColor.FromArgb(0x6b, 0x72, 0x80);
    }
}
