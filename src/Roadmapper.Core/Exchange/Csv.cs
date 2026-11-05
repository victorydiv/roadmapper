using System.Text;

namespace Roadmapper.Core.Exchange;

/// <summary>Minimal RFC 4180 CSV reading and writing.</summary>
public static class Csv
{
    /// <summary>Parses CSV text. The delimiter (comma, semicolon or tab) is detected from the header line.</summary>
    public static List<string[]> Parse(string text)
    {
        text = text.TrimStart('﻿');
        var delimiter = DetectDelimiter(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row.ToArray()); row.Clear();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }

        // Drop fully blank lines (common at the end of files saved by Excel).
        return rows.Where(r => r.Any(f => f.Length > 0)).ToList();
    }

    public static string Write(IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.Append(string.Join(',', row.Select(Quote))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Neutralizes text that a spreadsheet would run as a formula (=, +, -, @) by prefixing an
    /// apostrophe; <see cref="UnprotectFormula"/> reverses it on import.
    /// </summary>
    public static string ProtectFormula(string s) => s.Length > 0 && s[0] is '=' or '+' or '-' or '@' ? "'" + s : s;

    public static string UnprotectFormula(string s) => s.Length > 1 && s[0] == '\'' && s[1] is '=' or '+' or '-' or '@' ? s[1..] : s;

    private static string Quote(string s) =>
        s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return new[] { ',', ';', '\t' }.MaxBy(d => header.Count(c => c == d));
    }
}
