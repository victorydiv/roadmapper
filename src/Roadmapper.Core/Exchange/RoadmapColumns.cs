namespace Roadmapper.Core.Exchange;

/// <summary>
/// The spreadsheet format shared by export and import, so an exported file can be edited and
/// re-imported. Parent is the ancestor path joined with " › ", e.g. "Grow revenue › Checkout".
/// </summary>
public static class RoadmapColumns
{
    public const string Type = "Type", Title = "Title", Parent = "Parent", Description = "Description",
        Status = "Status", Progress = "Progress", Start = "Start", End = "End", Horizon = "Horizon",
        Owner = "Owner", Theme = "Theme", Tags = "Tags", JiraKey = "Jira key", Link = "Link",
        Reach = "Reach", Impact = "Impact", Confidence = "Confidence", Effort = "Effort",
        BusinessValue = "Business value", TimeCriticality = "Time criticality", RiskReduction = "Risk reduction",
        JobSize = "Job size", Moscow = "MoSCoW", ManualRank = "Manual rank",
        FromTemplate = "From template", TemplateApplied = "Template applied", Score = "Score", Id = "Id";

    public const string PathSeparator = " › ";

    /// <summary>Columns written on export. Score and Id are informational and ignored on import.</summary>
    public static readonly string[] Export =
    [
        Type, Title, Parent, Description, Status, Progress, Start, End, Horizon, Owner, Theme, Tags, JiraKey, Link,
        Reach, Impact, Confidence, Effort, BusinessValue, TimeCriticality, RiskReduction, JobSize, Moscow,
        ManualRank, FromTemplate, TemplateApplied, Score, Id,
    ];

    public static readonly string[] Importable = Export.Where(c => c is not (Score or Id)).ToArray();

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["level"] = Type, ["item type"] = Type, ["kind"] = Type,
        ["name"] = Title, ["summary"] = Title,
        ["parent path"] = Parent, ["path"] = Parent,
        ["details"] = Description, ["notes"] = Description,
        ["state"] = Status,
        ["progress %"] = Progress, ["percent complete"] = Progress, ["% complete"] = Progress, ["complete"] = Progress,
        ["start date"] = Start, ["begin"] = Start,
        ["end date"] = End, ["due"] = End, ["due date"] = End, ["finish"] = End, ["target date"] = End,
        ["bucket"] = Horizon, ["timeframe"] = Horizon,
        ["assignee"] = Owner, ["owner name"] = Owner,
        ["category"] = Theme, ["labels"] = Tags, ["tag"] = Tags,
        ["bv"] = BusinessValue, ["tc"] = TimeCriticality, ["rr"] = RiskReduction, ["size"] = JobSize,
        ["moscow priority"] = Moscow, ["priority"] = Moscow, ["rank"] = ManualRank,
        ["jira"] = JiraKey, ["jira issue"] = JiraKey, ["issue key"] = JiraKey, ["key"] = JiraKey, ["url"] = Link, ["external link"] = Link,
    };

    /// <summary>The import field a spreadsheet header most likely means, or null.</summary>
    public static string? Guess(string header)
    {
        var h = header.Trim();
        var exact = Importable.FirstOrDefault(c => string.Equals(c, h, StringComparison.OrdinalIgnoreCase));
        return exact ?? Aliases.GetValueOrDefault(h);
    }
}
