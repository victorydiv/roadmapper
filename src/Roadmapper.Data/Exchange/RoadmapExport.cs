using System.Globalization;
using ClosedXML.Excel;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;

namespace Roadmapper.Data.Exchange;

/// <summary>Flat spreadsheet exports in the <see cref="RoadmapColumns"/> format, parents before children.</summary>
public static class RoadmapExport
{
    /// <summary>One row per item. Cells are string, double, int or DateOnly (null = blank) so Excel gets real types.</summary>
    public static List<object?[]> Rows(ProductRoadmap roadmap)
    {
        var rows = new List<object?[]>();
        foreach (var row in roadmap.Flatten())
        {
            var i = row.Item;
            var s = i.Score ?? new ItemScore();
            var cells = new Dictionary<string, object?>
            {
                [RoadmapColumns.Type] = i.ItemType.ToString(),
                [RoadmapColumns.Title] = i.Title,
                [RoadmapColumns.Parent] = roadmap.PathOf(i),
                [RoadmapColumns.Description] = i.Description,
                [RoadmapColumns.Status] = i.Status == ItemStatus.InProgress ? "In progress" : i.Status.ToString(),
                [RoadmapColumns.Progress] = i.ProgressPct,
                [RoadmapColumns.Start] = i.StartDate,
                [RoadmapColumns.End] = i.EndDate,
                [RoadmapColumns.Horizon] = i.Horizon?.ToString(),
                [RoadmapColumns.Owner] = i.Owner,
                [RoadmapColumns.Theme] = i.Theme?.Name,
                [RoadmapColumns.Tags] = i.Tags.Count == 0 ? null : string.Join("; ", i.Tags.Select(t => t.Name).Order()),
                [RoadmapColumns.Reach] = s.Reach,
                [RoadmapColumns.Impact] = s.Impact,
                [RoadmapColumns.Confidence] = s.Confidence,
                [RoadmapColumns.Effort] = s.Effort,
                [RoadmapColumns.BusinessValue] = s.BusinessValue,
                [RoadmapColumns.TimeCriticality] = s.TimeCriticality,
                [RoadmapColumns.RiskReduction] = s.RiskReduction,
                [RoadmapColumns.JobSize] = s.JobSize,
                [RoadmapColumns.Moscow] = s.Moscow switch
                {
                    MoscowPriority.MustHave => "Must", MoscowPriority.ShouldHave => "Should",
                    MoscowPriority.CouldHave => "Could", MoscowPriority.WontHave => "Won't", _ => null,
                },
                [RoadmapColumns.ManualRank] = i.ManualRank,
                [RoadmapColumns.Score] = s.ComputedScore is { } cs ? Math.Round(cs, 2) : null,
                [RoadmapColumns.Id] = i.Id,
            };
            rows.Add(RoadmapColumns.Export.Select(c => cells[c]).ToArray());
        }
        return rows;
    }

    public static string ToCsv(ProductRoadmap roadmap) =>
        "﻿" + Csv.Write(Rows(roadmap).Select(r => r.Select(c => Csv.ProtectFormula(Text(c))).ToArray())
            .Prepend(RoadmapColumns.Export));

    public static byte[] ToXlsx(ProductRoadmap roadmap)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Roadmap");
        for (var c = 0; c < RoadmapColumns.Export.Length; c++) ws.Cell(1, c + 1).Value = RoadmapColumns.Export[c];

        var rows = Rows(roadmap);
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                switch (rows[r][c])
                {
                    case null: break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                    case double n: cell.Value = n; break;
                    case int n: cell.Value = n; break;
                    case string s: cell.Value = s; break;
                }
            }

        var header = ws.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f3f6");
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200), 8, 60);

        var deps = wb.AddWorksheet("Dependencies");
        deps.Cell(1, 1).Value = "Blocked item";
        deps.Cell(1, 2).Value = "Blocked by";
        deps.Row(1).Style.Font.Bold = true;
        var line = 2;
        foreach (var d in roadmap.Dependencies)
        {
            if (roadmap.Find(d.SuccessorId) is not { } succ || roadmap.Find(d.PredecessorId) is not { } pred) continue;
            deps.Cell(line, 1).Value = FullPath(roadmap, succ);
            deps.Cell(line, 2).Value = FullPath(roadmap, pred);
            line++;
        }
        deps.Columns().AdjustToContents(8, 80);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string FullPath(ProductRoadmap roadmap, RoadmapItem i) =>
        roadmap.PathOf(i) is { Length: > 0 } p ? p + RoadmapColumns.PathSeparator + i.Title : i.Title;

    private static string Text(object? cell) => cell switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString() ?? "",
    };
}
