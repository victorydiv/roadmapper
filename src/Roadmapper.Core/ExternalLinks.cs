using System.Text.RegularExpressions;

namespace Roadmapper.Core;

/// <summary>Links from roadmap items out to Jira (or anywhere else).</summary>
public static partial class ExternalLinks
{
    public const int MaxKeyLength = 50, MaxUrlLength = 1000;

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*-[0-9]+$")]
    private static partial Regex JiraKeyPattern();

    /// <summary>A Jira issue key: project key, dash, number, e.g. "WMS-123".</summary>
    public static bool IsJiraKey(string key) => JiraKeyPattern().IsMatch(key);

    /// <summary>An absolute http or https address.</summary>
    public static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Where the item's link goes: its own URL, else the Jira issue under <paramref name="jiraBaseUrl"/>
    /// ("https://acme.atlassian.net" + "/browse/WMS-123"); null when there's neither.
    /// </summary>
    public static string? Url(string? jiraKey, string? externalUrl, string? jiraBaseUrl) =>
        externalUrl is { Length: > 0 } ? externalUrl
        : jiraKey is { Length: > 0 } && jiraBaseUrl is { Length: > 0 } ? $"{jiraBaseUrl.TrimEnd('/')}/browse/{Uri.EscapeDataString(jiraKey)}"
        : null;
}
