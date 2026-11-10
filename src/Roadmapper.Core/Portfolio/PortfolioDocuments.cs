using System.Text.RegularExpressions;

namespace Roadmapper.Core.Portfolio;

/// <summary>
/// Where one kind of document is kept: a folder on this PC that OneDrive syncs to a SharePoint folder, and that
/// SharePoint folder's web address. Saving a file into <see cref="LocalPath"/> is what uploads it; Roadmapper never
/// talks to SharePoint itself.
/// </summary>
public class DocumentFolder
{
    public DocumentCategory Category { get; set; }
    /// <summary>The synced folder, e.g. "C:\Users\me\OneDrive - Acme\Team - Product Roadmaps".</summary>
    public string? LocalPath { get; set; }
    /// <summary>The same folder in SharePoint, in the form <see cref="DocumentLibrary.NormalizeFolderUrl"/> returns.</summary>
    public string? SharePointUrl { get; set; }
}

/// <summary>
/// A document kept in SharePoint: a roadmap, contract, invoice, SOW, BRD. Either a file Roadmapper saved into a synced
/// folder (<see cref="LocalPath"/>, plus its SharePoint <see cref="Url"/> when the folder's address is set), or just a
/// link to something already in SharePoint (<see cref="Url"/> only). Attached to at most one record.
/// </summary>
public class PortfolioDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DocumentCategory Category { get; set; }
    /// <summary>The file's name as saved, e.g. "WMS roadmap 2027.pdf". Null for a link.</summary>
    public string? FileName { get; set; }
    /// <summary>Where the file was saved on this PC. Null for a link.</summary>
    public string? LocalPath { get; set; }
    /// <summary>The document's SharePoint address.</summary>
    public string? Url { get; set; }
    public long? SizeBytes { get; set; }
    /// <summary>The document's own date: an invoice date, a contract's signing date.</summary>
    public DateOnly? DocumentDate { get; set; }
    public string? Notes { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? RoadmapItemId { get; set; }
    public RoadmapItem? RoadmapItem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsLink => LocalPath is null;

    public DocumentTarget? Target => DocumentTarget.Of(this);

    /// <summary>The attached record's name (needs its navigation loaded).</summary>
    public string? TargetName => Product?.Name ?? Application?.Name ?? Vendor?.Name ?? Contract?.Name ?? RoadmapItem?.Title;
}

/// <summary>What a document is attached to. <see cref="Key"/> ("p3", "a12", "v2", "c5", "r9") is used in select lists.</summary>
public readonly record struct DocumentTarget(DocumentTargetKind Kind, int Id)
{
    private static readonly (DocumentTargetKind Kind, string Prefix)[] Prefixes =
    [
        (DocumentTargetKind.Product, "p"), (DocumentTargetKind.Application, "a"), (DocumentTargetKind.Vendor, "v"),
        (DocumentTargetKind.Contract, "c"), (DocumentTargetKind.RoadmapItem, "r"),
    ];

    public string Key { get { var kind = Kind; return Prefixes.First(p => p.Kind == kind).Prefix + Id; } }

    public static DocumentTarget? Parse(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var (kind, prefix) in Prefixes)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.AsSpan(prefix.Length), out var id) && id > 0)
                return new DocumentTarget(kind, id);
        return null;
    }

    public static DocumentTarget? Of(PortfolioDocument d) =>
        d.ProductId is { } p ? new(DocumentTargetKind.Product, p)
        : d.ApplicationId is { } a ? new(DocumentTargetKind.Application, a)
        : d.VendorId is { } v ? new(DocumentTargetKind.Vendor, v)
        : d.ContractId is { } c ? new(DocumentTargetKind.Contract, c)
        : d.RoadmapItemId is { } r ? new(DocumentTargetKind.RoadmapItem, r)
        : null;

    /// <summary>Points the document at <paramref name="target"/>, or at nothing.</summary>
    public static void Apply(DocumentTarget? target, PortfolioDocument d)
    {
        (d.ProductId, d.ApplicationId, d.VendorId, d.ContractId, d.RoadmapItemId) = (null, null, null, null, null);
        if (target is not { } t) return;
        switch (t.Kind)
        {
            case DocumentTargetKind.Product: d.ProductId = t.Id; break;
            case DocumentTargetKind.Application: d.ApplicationId = t.Id; break;
            case DocumentTargetKind.Vendor: d.VendorId = t.Id; break;
            case DocumentTargetKind.Contract: d.ContractId = t.Id; break;
            default: d.RoadmapItemId = t.Id; break;
        }
    }

    /// <summary>The usual kind of document for this kind of record, to preselect when adding one.</summary>
    public DocumentCategory DefaultCategory => Kind switch
    {
        DocumentTargetKind.Product => DocumentCategory.Roadmap,
        DocumentTargetKind.Vendor or DocumentTargetKind.Contract => DocumentCategory.Contract,
        DocumentTargetKind.RoadmapItem => DocumentCategory.Sow,
        _ => DocumentCategory.Brd,
    };

    public override string ToString() => Key;
}

/// <summary>A document's details, for adding a link or editing either kind. The file itself is never changed here.</summary>
public class DocumentDraft
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DocumentCategory Category { get; set; }
    /// <summary>Required for a link; ignored for an uploaded file (its address comes from its folder).</summary>
    public string? Url { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? Notes { get; set; }
    public DocumentTarget? Target { get; set; }
    /// <summary>Set when editing an uploaded file.</summary>
    public bool IsFile { get; set; }

    public bool IsNew => Id == 0;

    public static DocumentDraft New(DocumentTarget? target, DocumentCategory? category = null) =>
        new() { Target = target, Category = category ?? target?.DefaultCategory ?? DocumentCategory.Other };

    public static DocumentDraft From(PortfolioDocument d) => new()
    {
        Id = d.Id, Title = d.Title, Category = d.Category, Url = d.Url, DocumentDate = d.DocumentDate, Notes = d.Notes,
        Target = d.Target, IsFile = !d.IsLink,
    };

    /// <summary>Checks the details. <paramref name="uploading"/>: a file comes with it, so the title may be blank (the file name is used) and no link is needed.</summary>
    public string? Validate(bool uploading = false)
    {
        if (!uploading && string.IsNullOrWhiteSpace(Title)) return "Give the document a title.";
        if (Title?.Trim().Length > 300) return "Title can be at most 300 characters.";
        if (Notes?.Trim().Length > 2000) return "Notes can be at most 2000 characters.";
        if (!uploading && !IsFile)
        {
            if (string.IsNullOrWhiteSpace(Url)) return "Paste the document's SharePoint address.";
            if (Url.Trim().Length > ExternalLinks.MaxUrlLength || !ExternalLinks.IsWebUrl(Url.Trim()))
                return "The link must be a web address starting with https://.";
        }
        return null;
    }

    public void ApplyTo(PortfolioDocument d)
    {
        d.Title = Title.Trim();
        d.Category = Category;
        if (!IsFile) d.Url = Url?.Trim();
        d.DocumentDate = DocumentDate;
        d.Notes = DraftText.Clean(Notes);
        DocumentTarget.Apply(Target, d);
    }
}

/// <summary>Rules for document folders, file names and SharePoint addresses.</summary>
public static partial class DocumentLibrary
{
    /// <summary>The largest file the app will upload: 250 MB.</summary>
    public const long MaxUploadBytes = 250L * 1024 * 1024;
    public const int MaxFileNameLength = 200;

    /// <summary>Prefix for the temporary file written before the real one appears. OneDrive doesn't sync "~$" files.</summary>
    public const string TempPrefix = "~$roadmapper-";

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".xlsb", ".ppt", ".pptx", ".pptm", ".vsd", ".vsdx",
    };

    [GeneratedRegex(@"^/:[a-z]:/r/", RegexOptions.IgnoreCase)]
    private static partial Regex PathSharingLink();

    [GeneratedRegex(@"^/:[a-z]:/[a-z]/", RegexOptions.IgnoreCase)]
    private static partial Regex TokenSharingLink();

    /// <summary>
    /// A SharePoint folder's address in one clean form, from whatever the person pasted: the browser's address bar,
    /// a "Copy link" sharing link (https://acme.sharepoint.com/:f:/r/sites/Team/Shared%20Documents/General?d=…),
    /// or a library view (…/Forms/AllItems.aspx?id=%2Fsites%2FTeam%2FShared%20Documents%2FGeneral).
    /// Returns e.g. "https://acme.sharepoint.com/sites/Team/Shared%20Documents/General". Throws
    /// <see cref="InvalidOperationException"/> for addresses that don't contain the folder's path.
    /// </summary>
    public static string NormalizeFolderUrl(string input)
    {
        var text = input.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The SharePoint address must be a web address starting with https://.");

        var path = uri.AbsolutePath;
        var query = ParseQuery(uri.Query);
        if (path.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
        {
            // A library view: the folder is in the id (or older RootFolder) parameter.
            path = query.GetValueOrDefault("id") ?? query.GetValueOrDefault("RootFolder")
                ?? throw new InvalidOperationException("That SharePoint page doesn't say which folder it shows. Open the folder itself and copy its address.");
        }
        else if (PathSharingLink().IsMatch(path))
        {
            path = "/" + PathSharingLink().Replace(path, "");
        }
        else if (TokenSharingLink().IsMatch(path))
        {
            throw new InvalidOperationException("That's a sharing link that hides the folder's address. In SharePoint, open the folder, choose Copy link, "
                + "and use a link for “People with existing access”, or copy the address from the browser's address bar.");
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => Uri.EscapeDataString(Uri.UnescapeDataString(s)));
        var clean = $"{uri.Scheme}://{uri.Authority}/{string.Join('/', segments)}";
        return clean.Length > ExternalLinks.MaxUrlLength ? throw new InvalidOperationException("That address is too long.") : clean;
    }

    /// <summary>A file's SharePoint address from its folder's. Office files open in the browser (?web=1) instead of downloading.</summary>
    public static string FileUrl(string folderUrl, string fileName) =>
        $"{folderUrl.TrimEnd('/')}/{Uri.EscapeDataString(fileName)}" + (OfficeExtensions.Contains(Path.GetExtension(fileName)) ? "?web=1" : "");

    /// <summary>
    /// A file name SharePoint and Windows both accept: no folder part, none of " * : &lt; &gt; ? / \ | # %, no leading or
    /// trailing spaces and dots, not starting with "~$", and at most <see cref="MaxFileNameLength"/> characters (the
    /// extension is kept). Throws when nothing usable is left.
    /// </summary>
    public static string SafeFileName(string name)
    {
        var baseName = name.Replace('\\', '/');
        baseName = baseName[(baseName.LastIndexOf('/') + 1)..];
        var chars = baseName.Select(c => c < 32 || "\"*:<>?|#%".Contains(c) ? '_' : c).ToArray();
        var clean = new string(chars).Trim().TrimEnd('.').Trim();
        while (clean.StartsWith("~$", StringComparison.Ordinal)) clean = clean[2..].TrimStart();
        if (clean.Length > MaxFileNameLength)
        {
            var ext = Path.GetExtension(clean);
            if (ext.Length > 20) ext = "";
            clean = clean[..(MaxFileNameLength - ext.Length)].TrimEnd() + ext;
        }
        if (clean.Length == 0 || clean.Trim('.', '_', ' ').Length == 0) throw new InvalidOperationException("That file name can't be used. Rename the file and try again.");
        return clean;
    }

    /// <summary>The title to use when none was given: the file name without its extension.</summary>
    public static string TitleFromFileName(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName) is { Length: > 0 } t ? t : fileName;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            result.TryAdd(Uri.UnescapeDataString(part[..eq]), Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' ')));
        }
        return result;
    }
}
