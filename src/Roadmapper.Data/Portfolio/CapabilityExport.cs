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

        if (m.AnyPriced) CostSheet(wb, m);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>"Cost": one row per price line, costed for the comparison's site profile, with each application's totals.</summary>
    private static void CostSheet(XLWorkbook wb, CapabilityMatrix m)
    {
        var ws = wb.AddWorksheet("Cost");
        var s = m.Site;
        ws.Cell(1, 1).Value = $"Costed for: {s.Name}";
        ws.Cell(2, 1).Value = string.Join(" · ", new[]
        {
            s.Users is { } u ? $"{u} users" : null, s.Licenses is { } lic ? $"{lic} licenses" : null, s.DockDoors is { } d ? $"{d} dock doors" : null,
            s.Assets is { } a ? $"{a} trailers / assets" : null, s.TransactionsPerMonth is { } t ? $"{t} transactions per month" : null,
        }.Where(x => x is not null));
        ws.Cell(1, 1).Style.Font.Bold = true;

        string[] head = ["Application", "Price", "Basis", "Unit price", "Frequency", "Minimum", "Quantity", "Charge", "Source", "As of", "Note"];
        for (var i = 0; i < head.Length; i++) ws.Cell(4, i + 1).Value = head[i];
        ws.Row(4).Style.Font.Bold = true;
        ws.Row(4).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f3f6");
        var r = 5;
        foreach (var app in m.Apps)
        {
            var cost = m.Cost(app.Id);
            if (!cost.IsPriced) continue;
            foreach (var line in cost.Lines)
            {
                var l = line.Line;
                ws.Cell(r, 1).Value = app.Name;
                ws.Cell(r, 2).Value = l.Label;
                ws.Cell(r, 3).Value = l.Basis == PricingBasis.Other ? $"Per {l.Unit}" : PortfolioText.Label(l.Basis);
                ws.Cell(r, 4).Value = l.UnitPrice;
                ws.Cell(r, 5).Value = PortfolioText.Label(l.Frequency);
                if (l.Minimum is { } min) ws.Cell(r, 6).Value = min;
                if (line.Quantity is { } q) ws.Cell(r, 7).Value = q; else ws.Cell(r, 7).Value = "unknown";
                if (line.Charge is { } ch) ws.Cell(r, 8).Value = ch;
                ws.Cell(r, 9).Value = PortfolioText.Label(l.Source);
                if (l.AsOf is { } asOf) ws.Cell(r, 10).Value = asOf.ToDateTime(TimeOnly.MinValue);
                ws.Cell(r, 11).Value = l.Note;
                r++;
            }
        }
        foreach (var c in new[] { 4, 6, 8 }) ws.Column(c).Style.NumberFormat.Format = "$#,##0.00";
        ws.Column(10).Style.NumberFormat.Format = "yyyy-mm-dd";

        r += 1;
        string[] totals = ["Application", "Monthly", "One-time", "First year", "Complete", "Missing sizes", "Features (Yes)"];
        for (var i = 0; i < totals.Length; i++) ws.Cell(r, i + 1).Value = totals[i];
        ws.Row(r).Style.Font.Bold = true;
        ws.Row(r).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f3f6");
        r++;
        foreach (var app in m.Apps)
        {
            var cost = m.Cost(app.Id);
            ws.Cell(r, 1).Value = app.Name;
            if (cost.IsPriced)
            {
                ws.Cell(r, 2).Value = cost.Monthly;
                ws.Cell(r, 3).Value = cost.OneTime;
                ws.Cell(r, 4).Value = cost.FirstYear;
                ws.Range(r, 2, r, 4).Style.NumberFormat.Format = "$#,##0.00";
                ws.Cell(r, 5).Value = cost.IsComplete ? "Yes" : "No";
                ws.Cell(r, 6).Value = string.Join(", ", cost.Missing);
            }
            else ws.Cell(r, 5).Value = "Not priced";
            ws.Cell(r, 7).Value = m.ByApp[app.Id].Yes;
            r++;
        }
        ws.Column(1).Width = 28;
        ws.Column(2).Width = 24;
        for (var c = 3; c <= 10; c++) ws.Column(c).Width = 14;
        ws.Column(11).Width = 40;
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
