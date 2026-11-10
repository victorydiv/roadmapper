using System.Globalization;
using System.Security;
using System.Text;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Web.Exports;

/// <summary>
/// A category's feature heat map as SVG: applications × features, each cell colored by strength (None grey, Low yellow,
/// Medium orange, High red; not rated is white and hatched), feature names slanted under the grid and a legend on the
/// right. <c>transpose</c> puts features down the side instead. Self-contained, so it can go straight into PowerPoint.
/// </summary>
public static class CapabilityHeatmapSvg
{
    private const string FontFamily = "Segoe UI, system-ui, -apple-system, sans-serif";
    private const double Cell = 34, CharWidth = 6.6, Pad = 16, TitleHeight = 34, LegendWidth = 96;

    public static string StrengthColor(FeatureStrength? s) => s switch
    {
        FeatureStrength.High => "#d7191c",
        FeatureStrength.Medium => "#f39c12",
        FeatureStrength.Low => "#f7d417",
        FeatureStrength.None => "#d3d3d3",
        _ => "#ffffff",
    };

    public static string Render(CapabilityMatrix m, string title, bool transpose = false)
    {
        // Rows down the side, columns along the bottom.
        var rows = transpose ? m.Features.Select(f => (f.Id, f.Name)).ToList() : m.Apps.Select(a => (a.Id, a.Name)).ToList();
        var cols = transpose ? m.Apps.Select(a => (a.Id, a.Name)).ToList() : m.Features.Select(f => (f.Id, f.Name)).ToList();
        FeatureAssessment? At(int row, int col) => transpose ? m.Cell(row, col) : m.Cell(col, row);
        string Tip(int row, int col)
        {
            var (feature, app) = transpose ? (rows.First(r => r.Id == row).Name, cols.First(c => c.Id == col).Name) : (cols.First(c => c.Id == col).Name, rows.First(r => r.Id == row).Name);
            var a = At(row, col);
            var rating = a is null ? "not rated"
                : string.Join(" · ", new[] { a.Strength is { } s ? $"strength {s}" : null, a.Availability is { } av ? $"available: {av}" : null }.Where(x => x is not null));
            return $"{app} – {feature}: {rating}{(a?.Note is { } n ? $". {n}" : "")}";
        }

        var labelWidth = Math.Min(260, rows.Select(r => r.Name.Length).DefaultIfEmpty(8).Max() * CharWidth + 12);
        var slant = Math.Min(240, cols.Select(c => c.Name.Length).DefaultIfEmpty(8).Max() * CharWidth) * 0.71 + 18;
        // Slanted names run down and to the left; leave room so the first ones don't run off the edge.
        var firstSlant = cols.Select((c, i) => Math.Min(240, c.Name.Length * CharWidth) * 0.71 - i * Cell - Cell / 2).DefaultIfEmpty(0).Max();
        var gridX = Pad + Math.Max(labelWidth, firstSlant);
        var gridY = Pad + TitleHeight;
        var gridW = Math.Max(cols.Count, 1) * Cell;
        var gridH = Math.Max(rows.Count, 1) * Cell;
        var legendX = gridX + gridW + 36;
        var width = Math.Max(legendX + LegendWidth, gridX + gridW + slant * 0.2) + Pad;
        var height = Math.Max(gridY + gridH + slant, gridY + 4 * 44 + 30) + Pad;

        var svg = new StringBuilder();
        svg.Append($"""<svg xmlns="http://www.w3.org/2000/svg" class="heatmap" width="{F(width)}" height="{F(height)}" viewBox="0 0 {F(width)} {F(height)}" font-family="{FontFamily}" font-size="12">""");
        svg.Append("""<defs><pattern id="hm-unrated" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="6" fill="#ffffff"/><line x1="0" y1="0" x2="0" y2="6" stroke="#e3e6eb" stroke-width="2"/></pattern></defs>""");
        svg.Append($"""<rect width="{F(width)}" height="{F(height)}" fill="#ffffff"/>""");
        svg.Append($"""<text x="{F(Pad)}" y="{F(Pad + 16)}" font-size="16" font-weight="600" fill="#1f2937">{E(title)}</text>""");

        if (rows.Count == 0 || cols.Count == 0)
        {
            svg.Append($"""<text x="{F(Pad)}" y="{F(gridY + 20)}" fill="#6b7280">{(m.Features.Count == 0 ? "No features yet." : "No applications to compare yet.")}</text></svg>""");
            return svg.ToString();
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var y = gridY + r * Cell;
            svg.Append($"""<text x="{F(gridX - 8)}" y="{F(y + Cell / 2 + 4)}" text-anchor="end" fill="#1f2937">{E(Clip(rows[r].Name, labelWidth))}</text>""");
            for (var c = 0; c < cols.Count; c++)
            {
                var a = At(rows[r].Id, cols[c].Id);
                var fill = a?.Strength is { } s ? StrengthColor(s) : "url(#hm-unrated)";
                svg.Append($"""<rect class="hm-cell" x="{F(gridX + c * Cell)}" y="{F(y)}" width="{F(Cell)}" height="{F(Cell)}" fill="{fill}" stroke="#ffffff" stroke-width="1"><title>{E(Tip(rows[r].Id, cols[c].Id))}</title></rect>""");
            }
        }
        svg.Append($"""<rect x="{F(gridX)}" y="{F(gridY)}" width="{F(gridW)}" height="{F(gridH)}" fill="none" stroke="#9aa3b2" stroke-width="1"/>""");

        // Column names, slanted down to the left under each column.
        for (var c = 0; c < cols.Count; c++)
        {
            var x = gridX + c * Cell + Cell / 2 + 4;
            var y = gridY + gridH + 10;
            svg.Append($"""<text x="{F(x)}" y="{F(y)}" text-anchor="end" transform="rotate(-45 {F(x)} {F(y)})" fill="#1f2937">{E(Clip(cols[c].Name, 240))}</text>""");
        }

        // Legend: High at the top, None at the bottom, plus "not rated".
        var levels = new[] { FeatureStrength.High, FeatureStrength.Medium, FeatureStrength.Low, FeatureStrength.None };
        for (var i = 0; i < levels.Length; i++)
        {
            var y = gridY + i * 44;
            svg.Append($"""<rect x="{F(legendX)}" y="{F(y)}" width="20" height="44" fill="{StrengthColor(levels[i])}" stroke="#ffffff"/>""");
            svg.Append($"""<text x="{F(legendX + 28)}" y="{F(y + 26)}" fill="#1f2937">{levels[i]}</text>""");
        }
        var ny = gridY + 4 * 44 + 10;
        svg.Append($"""<rect x="{F(legendX)}" y="{F(ny)}" width="20" height="16" fill="url(#hm-unrated)" stroke="#d3d3d3"/>""");
        svg.Append($"""<text x="{F(legendX + 28)}" y="{F(ny + 12)}" fill="#6b7280">Not rated</text>""");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Clip(string s, double width)
    {
        var max = Math.Max(4, (int)(width / CharWidth));
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    private static string E(string s) => SecurityElement.Escape(s) ?? "";
    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
