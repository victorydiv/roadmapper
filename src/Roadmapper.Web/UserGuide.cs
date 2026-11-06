using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Roadmapper.Web;

/// <summary>docs/USER_GUIDE.md, embedded in the app and rendered once to HTML for the /guide page.</summary>
public static class UserGuide
{
    public const string Route = "guide";

    private static readonly Lazy<string> Rendered = new(Render);

    public static string Html => Rendered.Value;

    private static string Render()
    {
        using var stream = typeof(UserGuide).Assembly.GetManifestResourceStream("USER_GUIDE.md")
            ?? throw new InvalidOperationException("The user guide wasn't embedded in the app.");
        using var reader = new StreamReader(stream);

        // GitHub-style heading ids, so the guide's links (e.g. #1-getting-started) work here and on GitHub.
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UsePipeTables()
            .UseEmphasisExtras()
            .Build();
        var doc = Markdown.Parse(reader.ReadToEnd(), pipeline);

        // The page has <base href="/">, so a bare "#section" would resolve to the home page. Point it at this page instead.
        foreach (var link in doc.Descendants<LinkInline>())
            if (link.Url is { } u && u.StartsWith('#')) link.Url = Route + u;

        return doc.ToHtml(pipeline);
    }
}
