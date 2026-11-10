using ClosedXML.Excel;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>
/// A category's comparison as a workbook: "Comparison" (Yes / Partial / No per cell, with coverage) and "Heat map"
/// (strength per cell, colored like the heat map). Notes go in as cell comments.
/// </summary>
public static class CapabilityExport
{
    private static readonly Dictionary<FeatureStrength, string> Fill = new()
    {
        [FeatureStrength.High] = "#d7191c", [FeatureStrength.Medium] = "#f39c12",
        [FeatureStrength.Low] = "#f7d417", [FeatureStrength.None] = "#d3d3d3",
    };

    private static readonly Dictionary<FeatureAvailability, string> AvailabilityFill = new()
    {
        [FeatureAvailability.Yes] = "#d6efe2", [FeatureAvailability.Partial] = "#fde7c4", [FeatureAvailability.No] = "#eeeeee",
    };

    public static byte[] ToXlsx(CapabilityMatrix m)
    {
        using var wb = new XLWorkbook();
        var label = PortfolioText.Label(m.Category);

        var cmp = wb.AddWorksheet("Comparison");
        Header(cmp, m, label, extra: ["Have it (Yes)", "Partial", "Gap", "Overlap"]);
        var r = 2;
        foreach (var f in m.Features)
        {
            cmp.Cell(r, 1).Value = f.Name;
            for (var c = 0; c < m.Apps.Count; c++)
            {
                var a = m.Cell(f.Id, m.Apps[c].Id);
                var cell = cmp.Cell(r, c + 2);
                if (a?.Availability is { } av)
                {
                    cell.Value = PortfolioText.Label(av);
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(AvailabilityFill[av]);
                }
                Note(cell, a);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            var cover = m.ByFeature[f.Id];
            var x = m.Apps.Count + 2;
            cmp.Cell(r, x).Value = cover.Yes;
            cmp.Cell(r, x + 1).Value = cover.Partial;
            cmp.Cell(r, x + 2).Value = cover.IsGap ? "Gap" : "";
            cmp.Cell(r, x + 3).Value = cover.IsOverlap ? "Overlap" : "";
            r++;
        }
        cmp.Cell(r, 1).Value = "Has it (Yes)";
        cmp.Cell(r + 1, 1).Value = "% rated";
        for (var c = 0; c < m.Apps.Count; c++)
        {
            var cv = m.ByApp[m.Apps[c].Id];
            cmp.Cell(r, c + 2).Value = cv.Yes;
            cmp.Cell(r + 1, c + 2).Value = cv.AssessedPct / 100.0;
            cmp.Cell(r + 1, c + 2).Style.NumberFormat.Format = "0%";
        }
        cmp.Range(r, 1, r + 1, m.Apps.Count + 1).Style.Font.Bold = true;
        Finish(cmp, m.Apps.Count + 5);

        var heat = wb.AddWorksheet("Heat map");
        Header(heat, m, label, extra: []);
        r = 2;
        foreach (var f in m.Features)
        {
            heat.Cell(r, 1).Value = f.Name;
            for (var c = 0; c < m.Apps.Count; c++)
            {
                var a = m.Cell(f.Id, m.Apps[c].Id);
                var cell = heat.Cell(r, c + 2);
                if (a?.Strength is { } s)
                {
                    cell.Value = PortfolioText.Label(s);
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(Fill[s]);
                    if (s == FeatureStrength.High) cell.Style.Font.FontColor = XLColor.White;
                }
                Note(cell, a);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            r++;
        }
        Finish(heat, m.Apps.Count + 1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Header(IXLWorksheet ws, CapabilityMatrix m, string label, string[] extra)
    {
        ws.Cell(1, 1).Value = $"{label} feature";
        for (var c = 0; c < m.Apps.Count; c++)
        {
            var a = m.Apps[c].Application;
            ws.Cell(1, c + 2).Value = a.Status == LifecycleStatus.Active ? a.Name : $"{a.Name} ({PortfolioText.Label(a.Status)})";
        }
        for (var i = 0; i < extra.Length; i++) ws.Cell(1, m.Apps.Count + 2 + i).Value = extra[i];
        var header = ws.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f3f6");
        header.Style.Alignment.WrapText = true;
        ws.SheetView.Freeze(1, 1);
    }

    private static void Note(IXLCell cell, FeatureAssessment? a)
    {
        if (a?.Note is { Length: > 0 } note) cell.CreateComment().AddText(note);
    }

    private static void Finish(IXLWorksheet ws, int columns)
    {
        ws.Column(1).Width = 32;
        for (var c = 2; c <= columns; c++) ws.Column(c).Width = 16;
    }
}
