using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>SharePoint addresses, file names, folders and targets (no database).</summary>
public class DocumentLibraryTests
{
    private const string Folder = "https://kencogroup1.sharepoint.com/sites/DistributionTechnologyTeam/Shared%20Documents/General/Product%20Roadmaps";

    [Theory]
    // "Copy link" from SharePoint: the folder's path is in the link.
    [InlineData("https://kencogroup1.sharepoint.com/:f:/r/sites/DistributionTechnologyTeam/Shared%20Documents/General/Product%20Roadmaps?d=w1f23361c4f024cb2aabc07316ae4acb9&csf=1&web=1&e=IYX1NM")]
    // The address bar while viewing the folder.
    [InlineData("https://kencogroup1.sharepoint.com/sites/DistributionTechnologyTeam/Shared%20Documents/Forms/AllItems.aspx?id=%2Fsites%2FDistributionTechnologyTeam%2FShared%20Documents%2FGeneral%2FProduct%20Roadmaps&viewid=abc")]
    // Typed by hand, with spaces and a trailing slash.
    [InlineData("  https://kencogroup1.sharepoint.com/sites/DistributionTechnologyTeam/Shared Documents/General/Product Roadmaps/ ")]
    public void Folder_addresses_are_normalized_from_any_form(string input) =>
        Assert.Equal(Folder, DocumentLibrary.NormalizeFolderUrl(input));

    [Theory]
    [InlineData("https://kencogroup1.sharepoint.com/:f:/s/DistributionTechnologyTeam/EabcDEF123")] // opaque sharing link
    [InlineData("http://kencogroup1.sharepoint.com/sites/Team")] // not https
    [InlineData("C:\\Users\\me\\OneDrive")]
    [InlineData("https://kencogroup1.sharepoint.com/sites/Team/Shared%20Documents/Forms/AllItems.aspx")] // view with no folder
    public void Addresses_without_a_folder_path_are_refused(string input) =>
        Assert.Throws<InvalidOperationException>(() => DocumentLibrary.NormalizeFolderUrl(input));

    [Fact]
    public void File_links_open_office_files_in_the_browser()
    {
        Assert.Equal(Folder + "/WMS%20roadmap%202027.pdf", DocumentLibrary.FileUrl(Folder, "WMS roadmap 2027.pdf"));
        Assert.Equal(Folder + "/BYC%20SOW%20%26%20plan.docx?web=1", DocumentLibrary.FileUrl(Folder + "/", "BYC SOW & plan.docx"));
    }

    [Fact]
    public void File_names_are_made_safe_for_sharepoint_and_windows()
    {
        Assert.Equal("SOW.pdf", DocumentLibrary.SafeFileName(@"C:\temp\..\SOW.pdf"));
        Assert.Equal("a_b_c_.xlsx", DocumentLibrary.SafeFileName("a:b*c?.xlsx"));
        Assert.Equal("budget.xlsx", DocumentLibrary.SafeFileName("~$budget.xlsx"));
        Assert.Equal("notes", DocumentLibrary.SafeFileName(" notes. "));
        var longName = DocumentLibrary.SafeFileName(new string('x', 300) + ".pdf");
        Assert.Equal(DocumentLibrary.MaxFileNameLength, longName.Length);
        Assert.EndsWith(".pdf", longName);
        Assert.Throws<InvalidOperationException>(() => DocumentLibrary.SafeFileName("..."));
        Assert.Throws<InvalidOperationException>(() => DocumentLibrary.SafeFileName("C:\\folder\\"));
    }

    [Fact]
    public void A_products_own_folder_wins_and_the_shared_one_fills_in()
    {
        DocumentFolder F(int? product, DocumentCategory c, string? path) => new() { ProductId = product, Category = c, LocalPath = path };
        var folders = new[]
        {
            F(null, DocumentCategory.Roadmap, "C:\\shared"), F(1, DocumentCategory.Roadmap, "C:\\synapse"),
            F(null, DocumentCategory.Contract, "C:\\contracts"), F(2, DocumentCategory.Contract, null), // product 2: blank row
        };
        Assert.Equal("C:\\synapse", DocumentLibrary.Resolve(folders, 1, DocumentCategory.Roadmap)!.LocalPath);
        Assert.Equal("C:\\shared", DocumentLibrary.Resolve(folders, 2, DocumentCategory.Roadmap)!.LocalPath);
        Assert.Equal("C:\\shared", DocumentLibrary.Resolve(folders, null, DocumentCategory.Roadmap)!.LocalPath);
        Assert.Equal("C:\\contracts", DocumentLibrary.Resolve(folders, 2, DocumentCategory.Contract)!.LocalPath);
        Assert.Null(DocumentLibrary.Resolve(folders, 1, DocumentCategory.Invoice));
    }

    [Fact]
    public void Targets_round_trip_through_their_keys_and_suggest_a_type()
    {
        foreach (var kind in Enum.GetValues<DocumentTargetKind>())
        {
            var t = new DocumentTarget(kind, 42);
            Assert.Equal(t, DocumentTarget.Parse(t.Key));
        }
        Assert.Null(DocumentTarget.Parse("x1"));
        Assert.Null(DocumentTarget.Parse("a0"));
        Assert.Equal(DocumentCategory.Contract, new DocumentTarget(DocumentTargetKind.Contract, 1).DefaultCategory);
        Assert.Equal(DocumentCategory.Sow, new DocumentTarget(DocumentTargetKind.RoadmapItem, 1).DefaultCategory);
        Assert.Equal(DocumentCategory.Roadmap, DocumentDraft.New(null, productId: 3).Category);
        Assert.Equal(DocumentCategory.Other, DocumentDraft.New(null).Category);
    }
}

/// <summary>Uploading into synced folders, per-product folders, links, attachments and backups.</summary>
public sealed class DocumentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PortfolioService _svc;
    private readonly RoadmapService _roadmaps;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roadmapper-doc-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _sharedRoadmaps, _contracts;

    public DocumentServiceTests()
    {
        _connection.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        _svc = new PortfolioService(factory, clock);
        _roadmaps = new RoadmapService(factory, clock);
        _sharedRoadmaps = Folder("Team - Product Roadmaps");
        _contracts = Folder("Team - Contracts");
    }

    public void Dispose()
    {
        _connection.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private async Task SetUpSharedFoldersAsync()
    {
        await _svc.SaveDocumentFolderAsync(null, DocumentCategory.Roadmap, _sharedRoadmaps,
            "https://acme.sharepoint.com/:f:/r/sites/Team/Shared%20Documents/General/Product%20Roadmaps?d=w1&csf=1&web=1&e=X");
        await _svc.SaveDocumentFolderAsync(null, DocumentCategory.Contract, $"\"{_contracts}\\\"", null);
    }

    [Fact]
    public async Task Folders_are_checked_and_their_addresses_cleaned_up()
    {
        await SetUpSharedFoldersAsync();
        var folders = await _svc.GetDocumentFoldersAsync();
        Assert.Equal(Enum.GetValues<DocumentCategory>().Length, folders.Count);
        var roadmap = folders.Single(f => f.Category == DocumentCategory.Roadmap);
        Assert.True(roadmap.LocalExists);
        Assert.True(roadmap.IsShared);
        Assert.Equal("https://acme.sharepoint.com/sites/Team/Shared%20Documents/General/Product%20Roadmaps", roadmap.SharePointUrl);
        // Quotes and a trailing slash from a pasted path are dropped.
        Assert.Equal(_contracts, folders.Single(f => f.Category == DocumentCategory.Contract).LocalPath);
        Assert.Null(folders.Single(f => f.Category == DocumentCategory.Invoice).LocalPath);
        Assert.True(await _svc.HasDocumentFoldersAsync());

        // A folder that isn't there is refused, not created.
        var missing = Path.Combine(_root, "Not synced");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDocumentFolderAsync(null, DocumentCategory.Invoice, missing, null));
        Assert.False(Directory.Exists(missing));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDocumentFolderAsync(null, DocumentCategory.Invoice, "relative\\path", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDocumentFolderAsync(999, DocumentCategory.Invoice, _contracts, null));

        // Clearing both removes the setting.
        await _svc.SaveDocumentFolderAsync(null, DocumentCategory.Contract, " ", "");
        Assert.Null((await _svc.GetDocumentFoldersAsync()).Single(f => f.Category == DocumentCategory.Contract).LocalPath);
    }

    [Fact]
    public async Task Each_product_uploads_to_its_own_folders_and_falls_back_to_the_shared_ones()
    {
        await SetUpSharedFoldersAsync();
        var synapse = await _roadmaps.CreateProductAsync("Synapse", null, ScoringModel.Wsjf);
        var byc = await _roadmaps.CreateProductAsync("BYC", null, ScoringModel.Wsjf);
        var synapseRoadmaps = Folder("Team - Synapse Roadmaps");
        var synapseSows = Folder("Team - Synapse SOWs");
        await _svc.SaveDocumentFolderAsync(synapse.Id, DocumentCategory.Roadmap, synapseRoadmaps, "https://acme.sharepoint.com/sites/Synapse/Shared Documents/Roadmaps");
        await _svc.SaveDocumentFolderAsync(synapse.Id, DocumentCategory.Sow, synapseSows, null);

        // The product's own folders are listed separately from the shared ones.
        var own = await _svc.GetDocumentFoldersAsync(synapse.Id);
        Assert.Equal(synapseRoadmaps, own.Single(f => f.Category == DocumentCategory.Roadmap).LocalPath);
        Assert.Null(own.Single(f => f.Category == DocumentCategory.Contract).LocalPath);
        Assert.Equal(_sharedRoadmaps, (await _svc.GetDocumentFoldersAsync()).Single(f => f.Category == DocumentCategory.Roadmap).LocalPath);

        // Synapse's roadmap goes to Synapse's folder; BYC has none, so its roadmap goes to the shared one.
        var a = await _svc.UploadDocumentAsync(DocumentDraft.New(null, synapse.Id), "roadmap.pdf", Content("synapse"));
        var b = await _svc.UploadDocumentAsync(DocumentDraft.New(null, byc.Id), "roadmap.pdf", Content("byc"));
        Assert.Equal("synapse", await File.ReadAllTextAsync(Path.Combine(synapseRoadmaps, "roadmap.pdf")));
        Assert.Equal("byc", await File.ReadAllTextAsync(Path.Combine(_sharedRoadmaps, "roadmap.pdf")));
        Assert.Equal("https://acme.sharepoint.com/sites/Synapse/Shared%20Documents/Roadmaps/roadmap.pdf", (await _svc.GetDocumentAsync(a))!.Url);
        Assert.StartsWith("https://acme.sharepoint.com/sites/Team/", (await _svc.GetDocumentAsync(b))!.Url);

        // Synapse's contracts use the shared folder too.
        await _svc.UploadDocumentAsync(DocumentDraft.New(null, synapse.Id, DocumentCategory.Contract), "msa.pdf", Content("msa"));
        Assert.True(File.Exists(Path.Combine(_contracts, "msa.pdf")));

        // A roadmap item's document belongs to the item's product, whatever was picked.
        var goal = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = synapse.Id, ItemType = ItemType.Goal, Title = "Retire Synapse" });
        var sowDraft = DocumentDraft.New(new DocumentTarget(DocumentTargetKind.RoadmapItem, goal), productId: byc.Id);
        var sow = await _svc.UploadDocumentAsync(sowDraft, "sow.docx", Content("sow"));
        Assert.True(File.Exists(Path.Combine(synapseSows, "sow.docx")));
        var sowDoc = (await _svc.GetDocumentAsync(sow))!;
        Assert.Equal(synapse.Id, sowDoc.ProductId);
        Assert.Equal("Retire Synapse", sowDoc.TargetName);

        // Each product lists its own.
        Assert.Equal(["msa", "roadmap", "sow"], (await _svc.GetDocumentsAsync(productId: synapse.Id)).Select(d => d.Title).Order());
        Assert.Equal(b, Assert.Single(await _svc.GetDocumentsAsync(productId: byc.Id)).Id);

        // Deleting a product drops its folders and leaves its documents shared.
        await _roadmaps.DeleteProductAsync(byc.Id);
        Assert.Null((await _svc.GetDocumentAsync(b))!.ProductId);
    }

    [Fact]
    public async Task Uploading_saves_the_file_and_links_it_to_sharepoint()
    {
        await SetUpSharedFoldersAsync();
        var product = await _roadmaps.CreateProductAsync("Synapse", null, ScoringModel.Wsjf);

        var id = await _svc.UploadDocumentAsync(DocumentDraft.New(null, product.Id), "Synapse roadmap 2027.pptx", Content("deck v1"));

        var path = Path.Combine(_sharedRoadmaps, "Synapse roadmap 2027.pptx");
        Assert.Equal("deck v1", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_sharedRoadmaps, DocumentLibrary.TempPrefix + "*"));
        var doc = (await _svc.GetDocumentAsync(id))!;
        Assert.Equal("Synapse roadmap 2027", doc.Title);
        Assert.Equal(DocumentCategory.Roadmap, doc.Category);
        Assert.Equal(7, doc.SizeBytes);
        Assert.Equal("https://acme.sharepoint.com/sites/Team/Shared%20Documents/General/Product%20Roadmaps/Synapse%20roadmap%202027.pptx?web=1", doc.Url);
        Assert.Equal("Synapse", doc.Product?.Name);
        Assert.Null(doc.Target);
        Assert.Equal(path, await _svc.GetDocumentFileAsync(id));

        // History is kept like any other portfolio record.
        Assert.Contains(await _svc.GetActivityAsync(PortfolioEntityType.Document, id), a => a.Action == AuditAction.Created && a.EntityName == "Synapse roadmap 2027");
    }

    [Fact]
    public async Task A_file_of_the_same_name_is_only_replaced_when_asked()
    {
        await SetUpSharedFoldersAsync();
        var draft = () => new DocumentDraft { Category = DocumentCategory.Roadmap, Title = "WMS roadmap" };
        var first = await _svc.UploadDocumentAsync(draft(), "wms.pdf", Content("v1"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.UploadDocumentAsync(draft(), "wms.pdf", Content("v2")));
        Assert.Contains("already a file named", ex.Message);
        Assert.Equal("v1", await File.ReadAllTextAsync(Path.Combine(_sharedRoadmaps, "wms.pdf")));

        var second = await _svc.UploadDocumentAsync(draft(), "wms.pdf", Content("v2!"), replace: true);
        Assert.Equal(first, second);
        Assert.Equal("v2!", await File.ReadAllTextAsync(Path.Combine(_sharedRoadmaps, "wms.pdf")));
        var doc = Assert.Single(await _svc.GetDocumentsAsync());
        Assert.Equal(3, doc.SizeBytes);
        Assert.Empty(Directory.GetFiles(_sharedRoadmaps, DocumentLibrary.TempPrefix + "*"));
    }

    [Fact]
    public async Task Uploading_needs_a_folder_that_is_on_this_pc()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.UploadDocumentAsync(new DocumentDraft { Category = DocumentCategory.Invoice }, "inv.pdf", Content("x")));
        Assert.Contains("No folder is set for Invoice", ex.Message);

        // Set, then OneDrive stops syncing it.
        var folder = Folder("Invoices");
        await _svc.SaveDocumentFolderAsync(null, DocumentCategory.Invoice, folder, null);
        Directory.Delete(folder);
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.UploadDocumentAsync(new DocumentDraft { Category = DocumentCategory.Invoice }, "inv.pdf", Content("x")));
        Assert.Contains("isn't on this PC", ex.Message);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Without_a_sharepoint_address_the_file_opens_from_this_pc()
    {
        await SetUpSharedFoldersAsync();
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Blue Yonder" });
        var id = await _svc.UploadDocumentAsync(DocumentDraft.New(new DocumentTarget(DocumentTargetKind.Vendor, vendor)), "MSA.pdf", Content("msa"));
        var doc = (await _svc.GetDocumentAsync(id))!;
        Assert.Null(doc.Url);
        Assert.Equal(DocumentCategory.Contract, doc.Category);
        Assert.True(File.Exists(Path.Combine(_contracts, "MSA.pdf")));
    }

    [Fact]
    public async Task Links_vendor_lists_and_removal()
    {
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Blue Yonder" });
        var contract = await _svc.SaveContractAsync(new ContractDraft { VendorId = vendor, Name = "BYC subscription" });
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "BYC WMS" });

        // A link needs a web address.
        var noUrl = new DocumentDraft { Title = "Invoice", Category = DocumentCategory.Invoice };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDocumentAsync(noUrl));
        noUrl.Url = "not a link";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDocumentAsync(noUrl));
        // Attaching to something that isn't there is refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDocumentAsync(new DocumentDraft
            { Title = "x", Url = "https://acme.sharepoint.com/x.pdf", Target = new DocumentTarget(DocumentTargetKind.Application, 999) }));

        var invoice = await _svc.SaveDocumentAsync(new DocumentDraft
        {
            Title = "Invoice 10/2026", Category = DocumentCategory.Invoice, Url = "https://acme.sharepoint.com/sites/Team/inv-1026.pdf",
            DocumentDate = new DateOnly(2026, 10, 1), Target = new DocumentTarget(DocumentTargetKind.Contract, contract),
        });
        var brd = await _svc.SaveDocumentAsync(new DocumentDraft
        {
            Title = "BRD", Category = DocumentCategory.Brd, Url = "https://acme.sharepoint.com/sites/Team/brd.docx",
            Target = new DocumentTarget(DocumentTargetKind.Application, app),
        });

        // A vendor's list includes its contracts' documents.
        var vendorDocs = await _svc.GetDocumentsAsync(new DocumentTarget(DocumentTargetKind.Vendor, vendor));
        Assert.Equal(invoice, Assert.Single(vendorDocs).Id);
        Assert.Equal("BYC subscription", vendorDocs[0].TargetName);
        Assert.Single(await _svc.GetDocumentsAsync(category: DocumentCategory.Brd));

        // Editing a link changes its details.
        var edit = DocumentDraft.From((await _svc.GetDocumentAsync(brd))!);
        edit.Title = "BRD v2";
        edit.Target = null;
        await _svc.SaveDocumentAsync(edit);
        Assert.Null((await _svc.GetDocumentAsync(brd))!.Target);

        // Deleting what a document is attached to leaves the document, unattached.
        await _svc.DeleteVendorAsync(vendor);
        Assert.Null((await _svc.GetDocumentAsync(invoice))!.Target);

        await _svc.RemoveDocumentAsync(invoice);
        Assert.Null(await _svc.GetDocumentAsync(invoice));
    }

    [Fact]
    public async Task Removing_an_uploaded_document_keeps_the_file()
    {
        await SetUpSharedFoldersAsync();
        var id = await _svc.UploadDocumentAsync(new DocumentDraft { Category = DocumentCategory.Roadmap }, "keep.pdf", Content("k"));
        await _svc.RemoveDocumentAsync(id);
        Assert.Empty(await _svc.GetDocumentsAsync());
        Assert.True(File.Exists(Path.Combine(_sharedRoadmaps, "keep.pdf")));
    }

    [Fact]
    public async Task Documents_and_folders_travel_in_the_portfolio_file()
    {
        await SetUpSharedFoldersAsync();
        var product = await _roadmaps.CreateProductAsync("Synapse", null, ScoringModel.Wsjf);
        var goal = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = product.Id, ItemType = ItemType.Goal, Title = "Retire Synapse" });
        await _svc.SaveDocumentFolderAsync(product.Id, DocumentCategory.Roadmap, Folder("Synapse Roadmaps"), "https://acme.sharepoint.com/sites/Synapse/Roadmaps");
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Blue Yonder" });
        var contract = await _svc.SaveContractAsync(new ContractDraft { VendorId = vendor, Name = "BYC subscription" });
        await _svc.UploadDocumentAsync(DocumentDraft.New(null, product.Id), "roadmap.pdf", Content("r"));
        await _svc.SaveDocumentAsync(new DocumentDraft { Title = "SOW", Category = DocumentCategory.Sow, Url = "https://acme.sharepoint.com/sow.docx", Target = new DocumentTarget(DocumentTargetKind.RoadmapItem, goal) });
        await _svc.SaveDocumentAsync(new DocumentDraft { Title = "Order form", Category = DocumentCategory.Contract, Url = "https://acme.sharepoint.com/of.pdf", ProductId = product.Id, Target = new DocumentTarget(DocumentTargetKind.Contract, contract) });

        using var buffer = new MemoryStream((await _svc.ExportPortfolioAsync()).ToBytes());
        var file = PortfolioFile.Read(buffer);
        Assert.Equal(3, file.Documents.Count);
        Assert.Equal(3, file.Settings!.DocumentFolders.Count);
        Assert.Equal("Synapse", file.Settings.DocumentFolders.Single(f => f.Product is not null).Product);

        // Restore into a fresh database that has the same roadmap.
        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(other).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var target = new PortfolioService(factory);
        var roadmaps = new RoadmapService(factory);
        var p2 = await roadmaps.CreateProductAsync("Synapse", null, ScoringModel.Wsjf);
        await roadmaps.SaveItemAsync(new ItemDraft { ProductId = p2.Id, ItemType = ItemType.Goal, Title = "Retire Synapse" });

        var result = await target.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal(3, result.Added["documents"]);
        Assert.Empty(result.Unmatched);
        var docs = await target.GetDocumentsAsync();
        Assert.All(docs, d => Assert.Equal(p2.Id, d.ProductId));
        Assert.Equal(["BYC subscription", "Retire Synapse"], docs.Where(d => d.Target is not null).Select(d => d.TargetName!).Order());
        Assert.NotNull((await target.GetDocumentFoldersAsync(p2.Id)).Single(f => f.Category == DocumentCategory.Roadmap).LocalPath);
        Assert.Equal(2, (await target.GetDocumentFoldersAsync()).Count(f => f.LocalPath is not null));

        // Merging the same file again adds nothing.
        var again = await target.ImportPortfolioAsync(file, PortfolioImportMode.Merge);
        Assert.Equal(0, again.Added.GetValueOrDefault("documents"));
        Assert.Equal(3, (await target.GetDocumentsAsync()).Count);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
