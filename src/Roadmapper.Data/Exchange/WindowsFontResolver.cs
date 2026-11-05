using PdfSharp.Fonts;

namespace Roadmapper.Data.Exchange;

/// <summary>
/// Supplies TrueType files from the Windows fonts folder to PDFsharp's core build. Known families
/// map to their regular/bold/italic/bold-italic files; anything else falls back to Arial.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    private static readonly string FontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    // File order: regular, bold, italic, bold-italic.
    private static readonly Dictionary<string, string[]> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Segoe UI"] = ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"],
        ["Arial"] = ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var files = Families.GetValueOrDefault(familyName) ?? Families["Arial"];
        var face = files[(bold ? 1 : 0) + (italic ? 2 : 0)];
        // Fall back to the regular face, letting PDFsharp simulate bold/italic.
        return File.Exists(Path.Combine(FontDir, face))
            ? new FontResolverInfo(face)
            : new FontResolverInfo(files[0], bold, italic);
    }

    public byte[]? GetFont(string faceName) => File.ReadAllBytes(Path.Combine(FontDir, faceName));
}
