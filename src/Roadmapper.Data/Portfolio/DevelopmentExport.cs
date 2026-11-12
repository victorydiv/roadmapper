using ClosedXML.Excel;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>
/// A product's development pipeline as a workbook: "Projects" (one row per project, with its cost by category and its
/// documents) and "Cost lines" (every line of every project's breakdown).
/// </summary>
public static class DevelopmentExport
{
    private const string Money = "$#,##0.00";

    public static byte[] ToXlsx(string productName, IReadOnlyList<DevProject> projects, IReadOnlyList<DevCostCategory> categories,
        IReadOnlyDictionary<int, List<PortfolioDocument>> documents)
    {
        using var wb = new XLWorkbook();
        var used = categories.Where(c => projects.Any(p => p.CostLines.Any(l => l.CategoryId == c.Id))).ToList();

        var ws = wb.AddWorksheet("Projects");
        string[] head = ["Project", "Status", "Reference", "Customer", "Application", "Vendor", "Roadmap item", "Target date", "Completed"];
        var col = 1;
        foreach (var h in head) ws.Cell(1, col++).Value = h;
        var firstCost = col;
        foreach (var c in used) ws.Cell(1, col++).Value = c.Name;
        ws.Cell(1, col).Value = "Hours";
        ws.Cell(1, col + 1).Value = "Total";
        ws.Cell(1, col + 2).Value = "Documents";
        ws.Cell(1, col + 3).Value = "Description";
        var last = col + 3;

        var r = 2;
        foreach (var p in projects)
        {
            ws.Cell(r, 1).Value = p.Name;
            ws.Cell(r, 2).Value = PortfolioText.Label(p.Status);
            ws.Cell(r, 3).Value = p.Reference;
            ws.Cell(r, 4).Value = p.Customer?.Name;
            ws.Cell(r, 5).Value = p.Application?.Name;
            ws.Cell(r, 6).Value = p.Vendor?.Name;
            ws.Cell(r, 7).Value = p.RoadmapItem?.Title;
            if (p.TargetDate is { } t) ws.Cell(r, 8).Value = t.ToDateTime(TimeOnly.MinValue);
            if (p.CompletedOn is { } d) ws.Cell(r, 9).Value = d.ToDateTime(TimeOnly.MinValue);
            for (var n = 0; n < used.Count; n++)
                if (p.CostLines.Any(l => l.CategoryId == used[n].Id)) ws.Cell(r, firstCost + n).Value = p.CostOf(used[n].Id);
            ws.Cell(r, col).Value = p.TotalHours;
            ws.Cell(r, col + 1).Value = p.TotalCost;
            var docs = documents.GetValueOrDefault(p.Id) ?? [];
            if (docs.Count > 0)
            {
                var cell = ws.Cell(r, col + 2);
                cell.Value = string.Join("; ", docs.Select(x => x.Title));
                if (docs[0].Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)) cell.SetHyperlink(new XLHyperlink(uri));
            }
            ws.Cell(r, col + 3).Value = p.Description;
            r++;
        }
        if (projects.Count > 0)
        {
            ws.Cell(r, 1).Value = "Total";
            for (var c = firstCost; c <= col + 1; c++)
                ws.Cell(r, c).FormulaA1 = $"SUM({ws.Cell(2, c).Address}:{ws.Cell(r - 1, c).Address})";
            ws.Row(r).Style.Font.Bold = true;
        }
        ws.Range(2, firstCost, Math.Max(r, 2), col - 1).Style.NumberFormat.Format = Money;
        ws.Range(2, col + 1, Math.Max(r, 2), col + 1).Style.NumberFormat.Format = Money;
        ws.Range(2, 8, Math.Max(r, 2), 9).Style.NumberFormat.Format = "yyyy-mm-dd";
        Finish(ws, last);

        var lines = wb.AddWorksheet("Cost lines");
        string[] lineHead = ["Project", "Status", "Category", "Hours", "Rate", "Amount", "Note"];
        for (var c = 0; c < lineHead.Length; c++) lines.Cell(1, c + 1).Value = lineHead[c];
        r = 2;
        foreach (var p in projects)
            foreach (var l in p.CostLines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id))
            {
                lines.Cell(r, 1).Value = p.Name;
                lines.Cell(r, 2).Value = PortfolioText.Label(p.Status);
                lines.Cell(r, 3).Value = l.Category?.Name ?? categories.FirstOrDefault(c => c.Id == l.CategoryId)?.Name;
                if (l.Hours is { } h) lines.Cell(r, 4).Value = h;
                if (l.Hours is not null && l.Rate is { } rate) lines.Cell(r, 5).Value = rate;
                lines.Cell(r, 6).Value = l.Amount;
                lines.Cell(r, 7).Value = l.Note;
                r++;
            }
        lines.Range(2, 5, Math.Max(r, 2), 6).Style.NumberFormat.Format = Money;
        Finish(lines, lineHead.Length);

        wb.Properties.Title = $"{productName}: development pipeline";
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Finish(IXLWorksheet ws, int lastColumn)
    {
        var header = ws.Range(1, 1, 1, lastColumn);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#eef1f6");
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, lastColumn).AdjustToContents(1, 200, 8, 60);
    }
}
