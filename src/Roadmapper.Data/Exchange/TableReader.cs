using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Roadmapper.Core.Exchange;

namespace Roadmapper.Data.Exchange;

/// <summary>Reads the first sheet of an .xlsx, or a .csv/.txt file, into rows of text (row 0 = headers).</summary>
public static class TableReader
{
    public const int MaxRows = 5000;

    public static List<string[]> Read(Stream seekableStream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var rows = ext switch
        {
            ".xlsx" => ReadXlsx(seekableStream),
            ".csv" or ".txt" => Csv.Parse(new StreamReader(seekableStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true).ReadToEnd()),
            _ => throw new InvalidOperationException("Choose a .csv or .xlsx file."),
        };
        if (rows.Count < 2) throw new InvalidOperationException("The file needs a header row and at least one data row.");
        if (rows.Count > MaxRows + 1) throw new InvalidOperationException($"The file has more than {MaxRows:N0} rows; split it into smaller files.");
        return rows;
    }

    /// <summary>A named sheet of an .xlsx as rows of text, or null when the file isn't .xlsx or has no such sheet.</summary>
    public static List<string[]>? ReadSheet(Stream seekableStream, string fileName, string sheetName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return null;
        seekableStream.Position = 0;
        using var wb = new XLWorkbook(seekableStream);
        return wb.Worksheets.TryGetWorksheet(sheetName, out var ws) ? Rows(ws) : null;
    }

    private static List<string[]> ReadXlsx(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        return Rows(wb.Worksheets.First());
    }

    private static List<string[]> Rows(IXLWorksheet sheet)
    {
        var range = sheet.RangeUsed();
        if (range is null) return [];

        var width = range.ColumnCount();
        return range.Rows()
            .Select(row => Enumerable.Range(1, width).Select(c => Text(row.Cell(c))).ToArray())
            .Where(cells => cells.Any(c => c.Length > 0))
            .ToList();
    }

    private static string Text(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        return cell.GetFormattedString();
    }
}
