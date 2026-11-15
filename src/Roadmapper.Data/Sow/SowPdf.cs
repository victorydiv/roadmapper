using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data.Exchange;

namespace Roadmapper.Data.Sows;

/// <summary>
/// A statement of work as a Kenco-branded PDF (US Letter, portrait), laid out like the SOW estimation template: the
/// logo on every page, the SOW summary, the description, the cost table by category (one-time and annual), the
/// footnote and agreement, and a signature block for each Kenco company and the customer. Optionally a last page lists
/// every cost line.
/// </summary>
public static class SowPdf
{
    private const string Font = "Calibri";
    private const double PageWidth = 612, PageHeight = 792; // points
    private const double SideMargin = 0.7 * 72;
    private static readonly double ContentWidth = PageWidth - 2 * SideMargin;
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    // Kenco brand colors: the template's dark teal, the logo's red and teal, and its near-black.
    private static readonly Color Teal = new(0x00, 0x4B, 0x5D);
    private static readonly Color Red = new(0xB5, 0x00, 0x28);
    private static readonly Color BrightTeal = new(0x1A, 0x9A, 0xAD);
    private static readonly Color Ink = new(0x04, 0x15, 0x1C);
    private static readonly Color Muted = new(0x5B, 0x67, 0x70);
    private static readonly Color Hairline = new(0xD5, 0xDD, 0xE0);
    private static readonly Color LabelFill = new(0xEC, 0xF3, 0xF5);
    private static readonly Color StripeFill = new(0xF6, 0xF9, 0xFA);

    static SowPdf()
    {
        WindowsFontResolver.Install();
    }

    private static readonly Lazy<string> Logo = new(() =>
    {
        using var stream = typeof(SowPdf).Assembly.GetManifestResourceStream("Roadmapper.Data.Sow.kenco-logo.png")
            ?? throw new InvalidOperationException("The Kenco logo is missing from the build.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return "base64:" + Convert.ToBase64String(ms.ToArray());
    });

    public static byte[] Render(Sow sow, SowSettings settings)
    {
        var doc = new Document();
        doc.Info.Title = SowRules.Title(sow);
        doc.Info.Subject = $"Statement of Work for {sow.CustomerName}";
        doc.Info.Author = "Kenco";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = Font;
        normal.Font.Size = 10;
        normal.Font.Color = Ink;

        var section = doc.AddSection();
        var setup = doc.DefaultPageSetup.Clone();
        setup.PageWidth = Unit.FromPoint(PageWidth);
        setup.PageHeight = Unit.FromPoint(PageHeight);
        setup.LeftMargin = setup.RightMargin = Unit.FromPoint(SideMargin);
        setup.TopMargin = Unit.FromInch(1.45);
        setup.BottomMargin = Unit.FromInch(0.85);
        setup.HeaderDistance = Unit.FromInch(0.45);
        setup.FooterDistance = Unit.FromInch(0.4);
        section.PageSetup = setup;

        AddHeader(section, sow);
        AddFooter(section, sow);

        Band(section, "Statement of Work (SOW) Summary", first: true);
        var summary = KeyValueTable(section);
        KeyValue(summary, "Project Name", sow.ProjectName);
        KeyValue(summary, "Kenco SOW Number", sow.Number);
        KeyValue(summary, "Customer", sow.CustomerName);
        KeyValue(summary, "Site", sow.Site);
        KeyValue(summary, "Kenco SOW Manager", sow.Manager);
        KeyValue(summary, "SOW Expiration", sow.ExpiresOn?.ToString("MMMM d, yyyy", Us));

        var parts = new (string Label, string? Text)[]
        {
            ("Overview", sow.Overview), ("Scope of Services", sow.Scope), ("Detailed Description", sow.DetailedDescription),
            ("Assumptions", sow.Assumptions), ("Schedule", sow.Schedule),
        };
        if (parts.Any(p => !string.IsNullOrWhiteSpace(p.Text)))
        {
            Band(section, "SOW Description");
            foreach (var (label, text) in parts.Where(p => !string.IsNullOrWhiteSpace(p.Text)))
            {
                var heading = section.AddParagraph(label);
                heading.Format.Font.Bold = true;
                heading.Format.Font.Size = 10.5;
                heading.Format.Font.Color = Teal;
                heading.Format.SpaceBefore = Unit.FromPoint(8);
                heading.Format.SpaceAfter = Unit.FromPoint(3);
                heading.Format.KeepWithNext = true;
                heading.Format.Borders.Bottom.Width = 0.5;
                heading.Format.Borders.Bottom.Color = Hairline;
                heading.Format.Borders.Distance = Unit.FromPoint(1.5);
                Text(section, text);
            }
        }

        AddCosts(section, sow, settings);
        AddSignatures(section, sow, settings);
        if (sow.IncludeCostDetail) AddCostDetail(section, sow);

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    /// <summary>The logo on the left; "Statement of Work" and the SOW number on the right; a red rule under both.</summary>
    private static void AddHeader(Section section, Sow sow)
    {
        var table = section.Headers.Primary.AddTable();
        table.Borders.Width = 0;
        table.AddColumn(Unit.FromPoint(ContentWidth * 0.45));
        table.AddColumn(Unit.FromPoint(ContentWidth * 0.55)).Format.Alignment = ParagraphAlignment.Right;
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Bottom;
        row.Borders.Bottom.Width = 1.5;
        row.Borders.Bottom.Color = Red;
        row.BottomPadding = Unit.FromPoint(5);

        var image = row.Cells[0].AddParagraph().AddImage(Logo.Value);
        image.Width = Unit.FromInch(1.75);
        image.LockAspectRatio = true;

        var title = row.Cells[1].AddParagraph("Statement of Work");
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Teal;
        var sub = row.Cells[1].AddParagraph();
        sub.Format.Font.Size = 9;
        sub.Format.Font.Color = Muted;
        sub.AddText(sow.Number is { Length: > 0 } n ? $"Kenco SOW {n}" : "Kenco SOW");
        sub.AddText("  ·  ");
        sub.AddText(sow.CustomerName);
    }

    private static void AddFooter(Section section, Sow sow)
    {
        var p = section.Footers.Primary.AddParagraph();
        p.Format.Font.Size = 7.5;
        p.Format.Font.Color = Muted;
        p.Format.Borders.Top.Width = 0.5;
        p.Format.Borders.Top.Color = Hairline;
        p.Format.Borders.Distance = Unit.FromPoint(4);
        p.Format.TabStops.AddTabStop(Unit.FromPoint(ContentWidth), TabAlignment.Right);
        p.AddText($"{SowRules.Title(sow)}  ·  Prepared {sow.PreparedOn.ToString("MMMM d, yyyy", Us)}  ·  Kenco confidential");
        p.AddTab();
        p.AddText("Page ");
        p.AddPageField();
        p.AddText(" of ");
        p.AddNumPagesField();
    }

    /// <summary>A section heading: white on Kenco teal, full width.</summary>
    private static void Band(Section section, string text, bool first = false)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Bold = true;
        p.Format.Font.Size = 11.5;
        p.Format.Font.Color = Colors.White;
        p.Format.Shading.Color = Teal;
        p.Format.Borders.Width = 0.5;
        p.Format.Borders.Color = Teal;
        p.Format.Borders.Distance = Unit.FromPoint(4);
        p.Format.LeftIndent = p.Format.RightIndent = Unit.FromPoint(4);
        p.Format.SpaceBefore = Unit.FromPoint(first ? 2 : 16);
        p.Format.SpaceAfter = Unit.FromPoint(8);
        p.Format.KeepWithNext = true;
    }

    private static Table KeyValueTable(Section section)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Hairline;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(6);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(4);
        table.AddColumn(Unit.FromInch(1.85));
        table.AddColumn(Unit.FromPoint(ContentWidth - 1.85 * 72));
        return table;
    }

    private static void KeyValue(Table table, string label, string? value)
    {
        var row = table.AddRow();
        row.Cells[0].Shading.Color = LabelFill;
        var l = row.Cells[0].AddParagraph(label);
        l.Format.Font.Bold = true;
        l.Format.Font.Color = Teal;
        row.Cells[1].AddParagraph(string.IsNullOrWhiteSpace(value) ? "" : value);
    }

    /// <summary>Description text: paragraphs and bullets (indented by level), as <see cref="SowRules.TextLines"/> reads them.</summary>
    private static void Text(Section section, string? text)
    {
        foreach (var line in SowRules.TextLines(text))
        {
            var p = section.AddParagraph();
            p.Format.SpaceAfter = Unit.FromPoint(2.5);
            var indent = 0.2 * line.Level;
            if (line.Bullet)
            {
                var left = Unit.FromInch(indent + 0.32);
                p.Format.LeftIndent = left;
                p.Format.FirstLineIndent = Unit.FromInch(-0.17);
                p.Format.TabStops.AddTabStop(left);
                var mark = p.AddFormattedText(line.Level == 0 ? "•" : "–");
                mark.Color = line.Level == 0 ? Red : Teal;
                mark.Bold = true;
                p.AddTab();
            }
            else p.Format.LeftIndent = Unit.FromInch(indent + 0.15);
            p.AddText(line.Text);
        }
    }

    /// <summary>The cost table by category, one-time and annual, with the totals, the footnote and the agreement text.</summary>
    private static void AddCosts(Section section, Sow sow, SowSettings settings)
    {
        Band(section, "Cost");
        var totals = sow.Totals;
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Hairline;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(6);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(3.5);
        var money = (ContentWidth - 2.9 * 72) / 2;
        table.AddColumn(Unit.FromInch(2.9));
        table.AddColumn(Unit.FromPoint(money)).Format.Alignment = ParagraphAlignment.Right;
        table.AddColumn(Unit.FromPoint(money)).Format.Alignment = ParagraphAlignment.Right;

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = LabelFill;
        head.Format.Font.Bold = true;
        head.Format.Font.Color = Teal;
        head.Cells[0].AddParagraph("Category");
        head.Cells[1].AddParagraph("One-Time");
        head.Cells[2].AddParagraph("Annual Recurring");

        var stripe = false;
        foreach (var r in totals.Rows)
        {
            var row = table.AddRow();
            if (stripe) row.Shading.Color = StripeFill;
            stripe = !stripe;
            row.Cells[0].AddParagraph(PortfolioText.Label(r.Category));
            Amount(row.Cells[1], r.OneTime);
            Amount(row.Cells[2], r.Annual);
        }
        var total = table.AddRow();
        total.Format.Font.Bold = true;
        total.Format.Font.Size = 11;
        total.Borders.Top.Width = 1.25;
        total.Borders.Top.Color = Teal;
        total.Cells[0].AddParagraph(settings.CostFootnote is null ? "TOTAL" : "TOTAL*");
        Amount(total.Cells[1], totals.OneTime, always: true);
        Amount(total.Cells[2], totals.Annual, always: true);
        // Keep the totals with the rows above them.
        for (var i = table.Rows.Count - 4; i < table.Rows.Count - 1; i++) table.Rows[i].KeepWith = 1;

        if (settings.CostFootnote is { } note)
        {
            var p = section.AddParagraph("* " + note);
            p.Format.Font.Size = 8;
            p.Format.Font.Italic = true;
            p.Format.Font.Color = Muted;
            p.Format.SpaceBefore = Unit.FromPoint(4);
        }
    }

    private static void Amount(Cell cell, decimal value, bool always = false) =>
        cell.AddParagraph(value == 0 && !always ? "–" : value.ToString("C2", Us));

    /// <summary>The agreement paragraph, then a signature block per Kenco company and one for the customer, two to a row, kept together.</summary>
    private static void AddSignatures(Section section, Sow sow, SowSettings settings)
    {
        Band(section, "Authorization");
        if (settings.Agreement is { } text)
        {
            var agreement = section.AddParagraph(text);
            agreement.Format.SpaceAfter = Unit.FromPoint(10);
            agreement.Format.KeepWithNext = true;
        }

        var parties = SowRules.SignatoryList(settings.Signatories);
        parties.Add(sow.CustomerName);

        // Up to three blocks to a row (the usual two Kenco companies and the customer fit on one), kept together.
        var perRow = Math.Min(3, parties.Count);
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.LeftPadding = 0;
        table.RightPadding = Unit.FromPoint(16);
        var colWidth = ContentWidth / perRow;
        for (var c = 0; c < perRow; c++) table.AddColumn(Unit.FromPoint(colWidth));
        for (var i = 0; i < parties.Count; i += perRow)
        {
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(4);
            row.BottomPadding = Unit.FromPoint(12);
            for (var c = 0; c < perRow && i + c < parties.Count; c++)
                SignatureBlock(row.Cells[c], parties[i + c], colWidth - 16);
        }
        for (var i = 0; i < table.Rows.Count - 1; i++) table.Rows[i].KeepWith = 1;
    }

    private static void SignatureBlock(Cell cell, string party, double width)
    {
        var name = cell.AddParagraph(party);
        name.Format.Font.Bold = true;
        name.Format.Font.Color = Teal;
        name.Format.SpaceAfter = Unit.FromPoint(4);
        name.Format.Borders.Left.Width = 2.5;
        name.Format.Borders.Left.Color = BrightTeal;
        name.Format.Borders.Distance = Unit.FromPoint(4);
        name.Format.LeftIndent = Unit.FromPoint(4);
        foreach (var label in new[] { "Signature:", "Name:", "Title:", "Date:" })
        {
            var p = cell.AddParagraph();
            p.Format.SpaceBefore = Unit.FromPoint(label == "Signature:" ? 14 : 9);
            p.Format.Font.Color = Muted;
            p.Format.TabStops.AddTabStop(Unit.FromPoint(width - 6), TabAlignment.Right, TabLeader.Lines);
            p.AddText(label);
            p.AddTab();
        }
    }

    /// <summary>Every cost line with a quantity or amount: what it is, how many, at what rate, and the amount.</summary>
    private static void AddCostDetail(Section section, Sow sow)
    {
        var lines = sow.Lines.Where(l => l.Quantity != 0 || l.Amount != 0)
            .OrderBy(l => l.Category).ThenBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();
        section.AddPageBreak();
        Band(section, "Cost Detail", first: true);
        if (lines.Count == 0)
        {
            section.AddParagraph("No cost lines.").Format.Font.Color = Muted;
            return;
        }
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Hairline;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(5);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(3);
        (string Head, double Inch, ParagraphAlignment Align)[] cols =
        [
            ("Item", 2.55, ParagraphAlignment.Left), ("Quantity", 1.0, ParagraphAlignment.Right), ("Rate", 0.95, ParagraphAlignment.Right),
            ("Frequency", 0.85, ParagraphAlignment.Left), ("Amount", 0, ParagraphAlignment.Right),
        ];
        var fixedWidth = cols.Sum(c => c.Inch) * 72;
        foreach (var c in cols) table.AddColumn(Unit.FromPoint(c.Inch > 0 ? c.Inch * 72 : ContentWidth - fixedWidth)).Format.Alignment = c.Align;
        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = LabelFill;
        head.Format.Font.Bold = true;
        head.Format.Font.Color = Teal;
        for (var i = 0; i < cols.Length; i++) head.Cells[i].AddParagraph(cols[i].Head);

        foreach (var group in lines.GroupBy(l => l.Category))
        {
            var g = table.AddRow();
            g.KeepWith = 1;
            g.Shading.Color = StripeFill;
            g.Cells[0].MergeRight = cols.Length - 1;
            var gp = g.Cells[0].AddParagraph(PortfolioText.Label(group.Key));
            gp.Format.Font.Bold = true;
            gp.Format.Alignment = ParagraphAlignment.Left;
            foreach (var l in group)
            {
                var r = table.AddRow();
                var item = r.Cells[0].AddParagraph(l.Description);
                item.Format.LeftIndent = Unit.FromPoint(8);
                r.Cells[1].AddParagraph($"{l.Quantity.ToString("#,0.##", Us)} {Plural(l.Unit, l.Quantity)}".Trim());
                r.Cells[2].AddParagraph(l.Rate.ToString("C2", Us));
                r.Cells[3].AddParagraph(PortfolioText.Label(l.Frequency));
                r.Cells[4].AddParagraph(l.Amount.ToString("C2", Us));
            }
        }
        var totals = sow.Totals;
        foreach (var (label, value) in new[] { ("Total one-time", totals.OneTime), ("Total annual recurring", totals.Annual) })
        {
            var t = table.AddRow();
            t.Format.Font.Bold = true;
            t.Cells[0].MergeRight = cols.Length - 2;
            t.Cells[0].AddParagraph(label).Format.Alignment = ParagraphAlignment.Left;
            t.Cells[4].AddParagraph(value.ToString("C2", Us));
        }
        table.Rows[table.Rows.Count - 2].Borders.Top.Width = 1.25;
        table.Rows[table.Rows.Count - 2].Borders.Top.Color = Teal;
    }

    private static string Plural(string? unit, decimal qty) =>
        unit is not { Length: > 0 } u ? "" : qty == 1 || u.EndsWith('s') ? u : u + "s";
}
