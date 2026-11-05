using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using Roadmapper.Core;

namespace Roadmapper.Data.Exchange;

/// <summary>
/// A printable roadmap report (landscape Letter): the timeline as a Gantt chart fitted to the page, then summary
/// figures, the full hierarchy as a table and the dependency list.
/// </summary>
public static class RoadmapPdf
{
    private const string Font = "Segoe UI";

    static RoadmapPdf()
    {
        // PDFsharp's core build needs to be told where fonts live.
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
    }

    public static byte[] Render(ProductRoadmap roadmap, DateTime generatedAt, GanttOptions? gantt = null)
    {
        var today = DateOnly.FromDateTime(generatedAt);
        var product = roadmap.Product;
        var doc = new Document();
        doc.Info.Title = $"{product.Name} roadmap";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = Font;
        normal.Font.Size = 8.5;

        // The Gantt is drawn straight onto the PDF after MigraDoc renders. MigraDoc lays out blank
        // placeholder pages for it first, so page numbers and the footer cover the chart pages too.
        var chart = new GanttPdf(roadmap, gantt ?? new GanttOptions(), today);
        var chartSection = AddSection(doc, roadmap, generatedAt);
        chartSection.AddParagraph(" ");
        for (var p = 1; p < chart.PageCount; p++) chartSection.AddPageBreak();

        var section = AddSection(doc, roadmap, generatedAt);

        var title = section.AddParagraph(product.Name);
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        if (product.Description is { } desc)
        {
            var p = section.AddParagraph(desc);
            p.Format.Font.Color = Muted;
        }

        var goals = roadmap.ChildrenOf(null).Where(g => g.Status != ItemStatus.Cancelled).ToList();
        var overall = goals.Count == 0 ? 0 : (int)Math.Round(goals.Average(g => g.ProgressPct));
        var summary = section.AddParagraph(
            $"Overall progress {overall}%  ·  {roadmap.Items.Count} items  ·  " +
            $"{roadmap.Items.Count(i => i.Status == ItemStatus.InProgress)} in progress  ·  " +
            $"{roadmap.Items.Count(i => ProductRoadmap.IsOverdue(i, today))} overdue  ·  " +
            $"{roadmap.Items.Count(i => ProductRoadmap.IsOpen(i) && roadmap.IsBlocked(i.Id))} blocked  ·  " +
            $"scored by {roadmap.Strategy.DisplayName}");
        summary.Format.SpaceBefore = Unit.FromPoint(6);
        summary.Format.SpaceAfter = Unit.FromPoint(10);
        summary.Format.Font.Size = 9.5;

        var table = section.AddTable();
        table.Borders.Bottom.Width = 0.5;
        table.Borders.Bottom.Color = Hairline;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(4);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(3);
        (string Header, double Cm, ParagraphAlignment Align)[] columns =
        [
            ("Item", 9.6, ParagraphAlignment.Left), ("Type", 1.9, ParagraphAlignment.Left), ("Status", 2.0, ParagraphAlignment.Left),
            ("Progress", 1.6, ParagraphAlignment.Right), ("Dates", 4.4, ParagraphAlignment.Left), ("Horizon", 1.5, ParagraphAlignment.Left),
            ("Owner", 2.3, ParagraphAlignment.Left), ("Score", 1.5, ParagraphAlignment.Right),
        ];
        foreach (var c in columns) table.AddColumn(Unit.FromCentimeter(c.Cm)).Format.Alignment = c.Align;

        var head = table.AddRow();
        head.HeadingFormat = true; // repeat on every page
        head.Shading.Color = new Color(0xf1, 0xf3, 0xf6);
        head.Format.Font.Bold = true;
        head.Format.Font.Color = Muted;
        for (var c = 0; c < columns.Length; c++) head.Cells[c].AddParagraph(columns[c].Header);

        foreach (var row in roadmap.Flatten())
        {
            var i = row.Item;
            var r = table.AddRow();
            if (row.Depth == 0)
            {
                r.Format.Font.Bold = true;
                r.Shading.Color = new Color(0xfb, 0xfa, 0xff);
            }
            var titleCell = r.Cells[0].AddParagraph(i.Title);
            titleCell.Format.LeftIndent = Unit.FromCentimeter(row.Depth * 0.45);
            if (ProductRoadmap.IsOpen(i) && roadmap.IsBlocked(i.Id))
            {
                var flag = titleCell.AddFormattedText("  blocked");
                flag.Color = Critical;
                flag.Bold = true;
                flag.Size = 7;
            }
            r.Cells[1].AddParagraph(i.ItemType.ToString());
            r.Cells[2].AddParagraph(i.Status == ItemStatus.InProgress ? "In progress" : i.Status.ToString());
            r.Cells[3].AddParagraph($"{i.ProgressPct}%");
            var dates = r.Cells[4].AddParagraph(DateRange(i.StartDate, i.EndDate));
            if (ProductRoadmap.IsOverdue(i, today)) { dates.Format.Font.Color = Critical; dates.AddText("  overdue"); }
            r.Cells[5].AddParagraph(i.Horizon?.ToString() ?? "");
            r.Cells[6].AddParagraph(i.Owner ?? "");
            r.Cells[7].AddParagraph(i.Score?.ComputedScore is { } s
                ? product.ScoringModel == ScoringModel.Moscow ? ((MoscowPriority)(int)s).ToString().Replace("Have", "") : s.ToString("0.#")
                : "–");
            if (i.Status == ItemStatus.Cancelled) r.Format.Font.Color = Muted;
        }

        if (roadmap.Items.Count == 0) section.AddParagraph("This roadmap has no items yet.");

        AddDependencies(section, roadmap);

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        for (var p = 0; p < chart.PageCount; p++)
        {
            using var g = XGraphics.FromPdfPage(renderer.PdfDocument.Pages[p]);
            chart.Draw(g, p);
        }
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    /// <summary>US Letter landscape with the shared footer.</summary>
    private static Section AddSection(Document doc, ProductRoadmap roadmap, DateTime generatedAt)
    {
        var section = doc.AddSection();
        // Set as explicit dimensions: the default setup's A4 size overrides PageFormat,
        // and Orientation alone didn't swap them in this MigraDoc version.
        section.PageSetup = doc.DefaultPageSetup.Clone();
        section.PageSetup.PageWidth = Unit.FromPoint(GanttPdf.PageWidth);
        section.PageSetup.PageHeight = Unit.FromPoint(GanttPdf.PageHeight);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        footer.AddText($"{roadmap.Product.Name} · generated {generatedAt:MMM d, yyyy h:mm tt} · page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        return section;
    }

    /// <summary>Every "A blocks B" link: whether B is still waiting on A, and whether their dates overlap.</summary>
    private static void AddDependencies(Section section, ProductRoadmap roadmap)
    {
        var deps = roadmap.Dependencies
            .Select(d => (Dep: d, Pred: roadmap.Find(d.PredecessorId), Succ: roadmap.Find(d.SuccessorId)))
            .Where(d => d.Pred is not null && d.Succ is not null)
            .OrderByDescending(d => ProductRoadmap.IsOpen(d.Pred!) && ProductRoadmap.IsOpen(d.Succ!))
            .ThenBy(d => d.Succ!.StartDate ?? DateOnly.MaxValue)
            .ToList();

        var heading = section.AddParagraph("Dependencies");
        heading.Format.Font.Size = 12;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromPoint(16);
        heading.Format.SpaceAfter = Unit.FromPoint(6);
        heading.Format.KeepWithNext = true;
        if (deps.Count == 0)
        {
            section.AddParagraph("No dependencies.").Format.Font.Color = Muted;
            return;
        }

        var table = section.AddTable();
        table.Borders.Bottom.Width = 0.5;
        table.Borders.Bottom.Color = Hairline;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(4);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(3);
        (string Header, double Cm)[] columns =
            [("Predecessor (blocks)", 7.2), ("Status", 2.0), ("Successor (blocked by)", 7.2), ("Status", 2.0), ("Effect", 6.4)];
        foreach (var c in columns) table.AddColumn(Unit.FromCentimeter(c.Cm));

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = new Color(0xf1, 0xf3, 0xf6);
        head.Format.Font.Bold = true;
        head.Format.Font.Color = Muted;
        for (var c = 0; c < columns.Length; c++) head.Cells[c].AddParagraph(columns[c].Header);

        foreach (var (dep, pred, succ) in deps)
        {
            var r = table.AddRow();
            r.Cells[0].AddParagraph(pred!.Title);
            r.Cells[1].AddParagraph(StatusText(pred.Status));
            r.Cells[2].AddParagraph(succ!.Title);
            r.Cells[3].AddParagraph(StatusText(succ.Status));

            var effect = r.Cells[4].AddParagraph();
            if (ProductRoadmap.IsOpen(pred) && ProductRoadmap.IsOpen(succ))
            {
                var t = effect.AddFormattedText("Blocked");
                t.Bold = true;
                t.Color = Critical;
                effect.AddText(" until the predecessor is done");
            }
            else effect.AddText(ProductRoadmap.IsOpen(pred) ? "Successor closed" : "Cleared");

            if (roadmap.HasScheduleConflict(dep))
            {
                var overlap = r.Cells[4].AddParagraph($"Starts {OverlapDays(pred, succ)}d before the predecessor ends");
                overlap.Format.Font.Color = Critical;
                overlap.Format.Font.Size = 7.5;
            }
            if (!ProductRoadmap.IsOpen(pred) && !ProductRoadmap.IsOpen(succ)) r.Format.Font.Color = Muted;
        }
    }

    private static string StatusText(ItemStatus s) => s == ItemStatus.InProgress ? "In progress" : s.ToString();

    private static int OverlapDays(RoadmapItem pred, RoadmapItem succ) =>
        pred.EndDate is { } end && succ.StartDate is { } start ? end.DayNumber - start.DayNumber + 1 : 0;

    private static readonly Color Muted = new(0x66, 0x70, 0x85);
    private static readonly Color Hairline = new(0xe1, 0xe4, 0xea);
    private static readonly Color Critical = new(0xd0, 0x3b, 0x3b);

    private static string DateRange(DateOnly? s, DateOnly? e) => (s, e) switch
    {
        (null, null) => "",
        ({ } a, null) => $"{a:MMM d, yyyy} →",
        (null, { } b) => $"→ {b:MMM d, yyyy}",
        ({ } a, { } b) => $"{a:MMM d, yyyy} – {b:MMM d, yyyy}",
    };
}
