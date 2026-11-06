using System.Globalization;

namespace Roadmapper.Core.Exchange;

public record ImportedTemplateLine(string Title, string? Description, string? Owner, int? StartOffsetDays, int? DurationDays, IReadOnlyList<string> TagNames);

public record ImportedTemplate(string Name, string? Description, IReadOnlyList<ImportedTemplateLine> Lines);

/// <summary>
/// The "Templates" sheet of the Excel export: one row per task line, grouped by template name in
/// order. Tags are names joined with "; ", so templates survive export and re-import into any product.
/// </summary>
public static class TemplateSheet
{
    public const string SheetName = "Templates";

    public const string Template = "Template", TemplateDescription = "Template description", Line = "#",
        Title = "Task title", Description = "Task description", Owner = "Owner",
        Offset = "Gap after previous task (days)", Duration = "Duration (days)", Tags = "Tags";

    /// <summary>The Gap column's name in files exported before tasks were chained.</summary>
    private const string OldOffset = "Start offset (days)";

    public static readonly string[] Columns = [Template, TemplateDescription, Line, Title, Description, Owner, Offset, Duration, Tags];

    public static List<object?[]> Rows(IEnumerable<TaskTemplate> templates) =>
        templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(t => t.Lines.OrderBy(l => l.SortOrder).Select((l, i) => new object?[]
            {
                t.Name, i == 0 ? t.Description : null, i + 1, l.Title, l.Description, l.Owner,
                l.StartOffsetDays, l.DurationDays,
                l.Tags.Count == 0 ? null : string.Join("; ", l.Tags.Select(x => x.Name).Order()),
            }))
            .ToList();

    /// <summary>Parses a sheet whose first row is <see cref="Columns"/> (in any order). Rows without a template name or title are skipped.</summary>
    public static List<ImportedTemplate> Parse(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return [];
        var index = rows[0].Select((h, i) => (h: h.Trim(), i)).GroupBy(x => x.h, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        if (!index.ContainsKey(Offset) && index.Remove(OldOffset, out var old)) index[Offset] = old;
        string Get(string[] r, string col) => index.TryGetValue(col, out var i) && i < r.Length ? r[i].Trim() : "";
        int? Int(string[] r, string col) => int.TryParse(Get(r, col), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v
            : double.TryParse(Get(r, col), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;

        var result = new List<ImportedTemplate>();
        foreach (var group in rows.Skip(1).Where(r => Get(r, Template).Length > 0 && Get(r, Title).Length > 0)
                     .GroupBy(r => Get(r, Template), StringComparer.OrdinalIgnoreCase))
        {
            var lines = group.Select(r => (Order: Int(r, Line) ?? int.MaxValue, Line: new ImportedTemplateLine(
                    Get(r, Title), NullIfEmpty(Get(r, Description)), NullIfEmpty(Get(r, Owner)),
                    Int(r, Offset), Int(r, Duration) is { } d and >= 1 ? d : null,
                    Get(r, Tags).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList())))
                .Select((x, i) => (x.Order, Seq: i, x.Line))
                .OrderBy(x => x.Order).ThenBy(x => x.Seq)
                .Select(x => x.Line).ToList();
            var description = group.Select(r => Get(r, TemplateDescription)).FirstOrDefault(s => s.Length > 0);
            result.Add(new ImportedTemplate(group.Key, description, lines));
        }
        return result;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
