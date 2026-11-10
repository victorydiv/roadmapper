using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>One document type's folder settings for a product (or shared, when <see cref="ProductId"/> is null), and whether the folder is there on this PC.</summary>
public record DocumentFolderStatus(int? ProductId, DocumentCategory Category, string? LocalPath, string? SharePointUrl, bool LocalExists)
{
    public bool CanUpload => LocalExists;
    public bool IsShared => ProductId is null;
}

/// <summary>Something a document can be attached to, for the picker. <see cref="ProductId"/>: the product it belongs to, if any.</summary>
public record DocumentTargetOption(DocumentTarget Target, string Name, string? Detail = null, int? ProductId = null);

public partial class PortfolioService
{
    // ---- Folders ----

    /// <summary>A product's folder settings (the shared ones when <paramref name="productId"/> is null) for every document type, set or not, in category order.</summary>
    public async Task<List<DocumentFolderStatus>> GetDocumentFoldersAsync(int? productId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.DocumentFolders.AsNoTracking().Where(f => f.ProductId == productId).ToDictionaryAsync(f => f.Category);
        return Enum.GetValues<DocumentCategory>()
            .Select(c => Status(rows.GetValueOrDefault(c) ?? new DocumentFolder { ProductId = productId, Category = c })).ToList();
    }

    /// <summary>
    /// Where a document of this type and product is uploaded: the product's own folder when set, else the shared one
    /// (<see cref="DocumentLibrary.Resolve"/>). Null when neither is set.
    /// </summary>
    public async Task<DocumentFolderStatus?> ResolveDocumentFolderAsync(int? productId, DocumentCategory category)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.Category == category && (f.ProductId == null || f.ProductId == productId)).ToListAsync();
        return DocumentLibrary.Resolve(rows, productId, category) is { } f ? Status(f) : null;
    }

    /// <summary>True when any product, or the shared set, has a document folder.</summary>
    public async Task<bool> HasDocumentFoldersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.DocumentFolders.AnyAsync(f => f.LocalPath != null);
    }

    private static DocumentFolderStatus Status(DocumentFolder f) =>
        new(f.ProductId, f.Category, f.LocalPath, f.SharePointUrl, f.LocalPath is { } p && Directory.Exists(p));

    /// <summary>
    /// Sets where one document type is kept, for a product (<paramref name="productId"/>) or shared (null).
    /// <paramref name="localPath"/> is the OneDrive-synced folder on this PC; <paramref name="sharePointUrl"/> is that
    /// folder in SharePoint, in any form <see cref="DocumentLibrary.NormalizeFolderUrl"/> accepts. Both may be blank.
    /// The local folder must exist: Roadmapper never creates it, so a mistyped path can't quietly collect files that
    /// never reach SharePoint.
    /// </summary>
    public async Task SaveDocumentFolderAsync(int? productId, DocumentCategory category, string? localPath, string? sharePointUrl)
    {
        var local = Clean(localPath)?.Trim('"').TrimEnd('\\', '/');
        if (local is not null)
        {
            if (!Path.IsPathFullyQualified(local)) throw new InvalidOperationException($"The {PortfolioText.Label(category)} folder must be a full path, like C:\\Users\\you\\OneDrive - Company\\Team - Folder.");
            if (local.Length > 1000) throw new InvalidOperationException("That folder path is too long.");
            if (!Directory.Exists(local)) throw new InvalidOperationException($"The folder {local} doesn't exist on this PC. Check the path, and that OneDrive has synced it.");
        }
        var url = Clean(sharePointUrl) is { } u ? DocumentLibrary.NormalizeFolderUrl(u) : null;

        await using var db = await dbFactory.CreateDbContextAsync();
        if (productId is { } pid && !await db.Products.AnyAsync(p => p.Id == pid)) throw NotFound("Product");
        var row = await db.DocumentFolders.FirstOrDefaultAsync(f => f.ProductId == productId && f.Category == category);
        if (local is null && url is null)
        {
            if (row is not null) db.DocumentFolders.Remove(row);
        }
        else
        {
            row ??= db.DocumentFolders.Add(new DocumentFolder { ProductId = productId, Category = category }).Entity;
            row.LocalPath = local;
            row.SharePointUrl = url;
        }
        await db.SaveChangesAsync();
    }

    // ---- Documents ----

    private static IQueryable<PortfolioDocument> WithTargets(IQueryable<PortfolioDocument> q) => q
        .Include(d => d.Product).Include(d => d.Application).Include(d => d.Vendor)
        .Include(d => d.Contract).ThenInclude(c => c!.Vendor).Include(d => d.RoadmapItem).ThenInclude(i => i!.Product);

    /// <summary>
    /// Documents, newest first. With <paramref name="target"/>, only those attached to it (a vendor's list also has its
    /// contracts' documents); with <paramref name="productId"/>, only those belonging to that product.
    /// </summary>
    public async Task<List<PortfolioDocument>> GetDocumentsAsync(DocumentTarget? target = null, DocumentCategory? category = null, int? productId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = WithTargets(db.Documents.AsNoTracking());
        if (target is { } t)
        {
            q = t.Kind switch
            {
                DocumentTargetKind.Application => q.Where(d => d.ApplicationId == t.Id),
                DocumentTargetKind.Vendor => q.Where(d => d.VendorId == t.Id || d.Contract!.VendorId == t.Id),
                DocumentTargetKind.Contract => q.Where(d => d.ContractId == t.Id),
                _ => q.Where(d => d.RoadmapItemId == t.Id),
            };
        }
        if (category is { } c) q = q.Where(d => d.Category == c);
        if (productId is { } pid) q = q.Where(d => d.ProductId == pid);
        return (await q.ToListAsync())
            .OrderByDescending(d => d.DocumentDate ?? DateOnly.FromDateTime(d.CreatedAt))
            .ThenByDescending(d => d.Id).ToList();
    }

    public async Task<PortfolioDocument?> GetDocumentAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithTargets(db.Documents.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id);
    }

    /// <summary>The saved file on this PC, when the document has one and it's still there.</summary>
    public async Task<string?> GetDocumentFileAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var path = await db.Documents.AsNoTracking().Where(d => d.Id == id).Select(d => d.LocalPath).FirstOrDefaultAsync();
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Saves a file into its product's folder for its type, or the shared one when the product has none (OneDrive then
    /// uploads it to SharePoint), and records it. When a file of that name is already there, <paramref name="replace"/>
    /// saves over it (SharePoint keeps the old one as a previous version); otherwise the upload is refused. Replacing a
    /// file Roadmapper already lists updates that entry. Returns the document's id.
    /// </summary>
    public async Task<int> UploadDocumentAsync(DocumentDraft draft, string fileName, Stream content, bool replace = false)
    {
        if (draft.Validate(uploading: true) is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        await PrepareDocumentAsync(db, draft);
        var label = PortfolioText.Label(draft.Category);
        var folder = await ResolveDocumentFolderAsync(draft.ProductId, draft.Category)
            ?? throw new InvalidOperationException($"No folder is set for {label} documents. Set one in the product's Settings › Document folders, or a shared one in Portfolio › Settings.");
        if (!folder.LocalExists)
            throw new InvalidOperationException($"The {label} folder ({folder.LocalPath}) isn't on this PC. Check that OneDrive is syncing it, or fix the path in Settings › Document folders.");

        var name = DocumentLibrary.SafeFileName(fileName);
        var root = Path.GetFullPath(folder.LocalPath!);
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That file name can't be used. Rename the file and try again.");
        if (File.Exists(path) && !replace)
            throw new InvalidOperationException($"There's already a file named “{name}” in {root}. Tick “Replace a file with the same name” to save over it (SharePoint keeps the old one as a previous version), or rename your file.");

        // Write next to it first, so a failed upload never leaves half a file where SharePoint will pick it up.
        var temp = Path.Combine(root, $"{DocumentLibrary.TempPrefix}{Guid.NewGuid():N}.tmp");
        long size;
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(output);
                size = output.Length;
            }
            File.Move(temp, path, overwrite: replace);
        }
        catch (IOException ex)
        {
            TryDelete(temp);
            throw new InvalidOperationException($"Couldn't save the file to {root}: {ex.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new InvalidOperationException($"Roadmapper isn't allowed to save files in {root}.");
        }

        var doc = await db.Documents.FirstOrDefaultAsync(d => d.LocalPath == path);
        if (doc is null)
        {
            doc = new PortfolioDocument();
            db.Documents.Add(doc);
        }
        var title = Clean(draft.Title) ?? DocumentLibrary.TitleFromFileName(name);
        draft.IsFile = true;
        draft.ApplyTo(doc);
        doc.Title = title;
        doc.FileName = name;
        doc.LocalPath = path;
        doc.Url = folder.SharePointUrl is { } u ? DocumentLibrary.FileUrl(u, name) : null;
        doc.SizeBytes = size;
        doc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>
    /// Adds a link to a document already in SharePoint, or changes any document's details. An uploaded file is never
    /// moved: changing its product or type only changes how it's listed.
    /// </summary>
    public async Task<int> SaveDocumentAsync(DocumentDraft draft)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        PortfolioDocument doc;
        if (draft.IsNew)
        {
            draft.IsFile = false;
            doc = new PortfolioDocument();
            db.Documents.Add(doc);
        }
        else
        {
            doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == draft.Id) ?? throw NotFound("Document");
            draft.IsFile = !doc.IsLink;
        }
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await PrepareDocumentAsync(db, draft);
        draft.ApplyTo(doc);
        doc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>Takes the document off Roadmapper's lists. The file stays in its folder and in SharePoint.</summary>
    public async Task RemoveDocumentAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Documents.FindAsync(id) is not { } doc) return;
        db.Documents.Remove(doc);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Everything a document can be attached to, with the product each belongs to where there is one (an application's
    /// own roadmap, a roadmap item's product). Roadmap items: goals, initiatives and features.
    /// </summary>
    public async Task<List<DocumentTargetOption>> GetDocumentTargetOptionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        static DocumentTarget T(DocumentTargetKind k, int id) => new(k, id);
        var options = new List<DocumentTargetOption>();
        options.AddRange((await db.Applications.AsNoTracking().Select(x => new { x.Id, x.Name, x.ProductId }).ToListAsync())
            .OrderBy(x => x.Name).Select(x => new DocumentTargetOption(T(DocumentTargetKind.Application, x.Id), x.Name, null, x.ProductId)));
        options.AddRange((await db.Vendors.AsNoTracking().Select(x => new { x.Id, x.Name }).ToListAsync())
            .OrderBy(x => x.Name).Select(x => new DocumentTargetOption(T(DocumentTargetKind.Vendor, x.Id), x.Name)));
        options.AddRange((await db.Contracts.AsNoTracking().Select(x => new { x.Id, x.Name, Vendor = x.Vendor!.Name }).ToListAsync())
            .OrderBy(x => x.Vendor).ThenBy(x => x.Name).Select(x => new DocumentTargetOption(T(DocumentTargetKind.Contract, x.Id), x.Name, x.Vendor)));
        options.AddRange((await db.Items.AsNoTracking()
                .Where(i => i.ItemType == ItemType.Goal || i.ItemType == ItemType.Initiative || i.ItemType == ItemType.Feature)
                .Select(i => new { i.Id, i.Title, Product = i.Product!.Name, i.ProductId, i.ItemType }).ToListAsync())
            .OrderBy(x => x.Product).ThenBy(x => x.ItemType).ThenBy(x => x.Title)
            .Select(x => new DocumentTargetOption(T(DocumentTargetKind.RoadmapItem, x.Id), x.Title, x.Product, x.ProductId)));
        return options;
    }

    /// <summary>Checks the product and target exist. A roadmap item's document always belongs to the item's product.</summary>
    private static async Task PrepareDocumentAsync(RoadmapDbContext db, DocumentDraft draft)
    {
        if (draft.ProductId is { } pid && !await db.Products.AnyAsync(p => p.Id == pid)) throw NotFound("Product");
        if (draft.Target is not { } t) return;
        var exists = t.Kind switch
        {
            DocumentTargetKind.Application => await db.Applications.AnyAsync(x => x.Id == t.Id),
            DocumentTargetKind.Vendor => await db.Vendors.AnyAsync(x => x.Id == t.Id),
            DocumentTargetKind.Contract => await db.Contracts.AnyAsync(x => x.Id == t.Id),
            _ => await db.Items.AnyAsync(x => x.Id == t.Id),
        };
        if (!exists) throw NotFound(PortfolioText.Label(t.Kind));
        if (t.Kind == DocumentTargetKind.RoadmapItem)
            draft.ProductId = await db.Items.Where(i => i.Id == t.Id).Select(i => (int?)i.ProductId).FirstAsync();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
