using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Development pipeline rules (no database).</summary>
public class DevRulesTests
{
    private static DevProject Project(DevStatus status, params (int Category, decimal? Hours, decimal? Rate, decimal? Fixed)[] lines) => new()
    {
        Name = status.ToString(), Status = status,
        CostLines = lines.Select(l => new DevCostLine { CategoryId = l.Category, Hours = l.Hours, Rate = l.Rate, FixedAmount = l.Fixed }).ToList(),
    };

    [Fact]
    public void A_line_is_hours_times_rate_or_a_fixed_amount()
    {
        var p = Project(DevStatus.Approved, (1, 40, 150, null), (2, 12.5m, 95, null), (4, null, null, 2500), (1, 10, 150, 999));
        Assert.Equal(6000m + 1187.5m + 2500m + 1500m, p.TotalCost); // a fixed amount is ignored when there are hours
        Assert.Equal(62.5m, p.TotalHours);
        Assert.Equal(7500m, p.CostOf(1));
        Assert.Equal(0m, p.CostOf(3));
    }

    [Fact]
    public void Summary_splits_proposed_committed_and_complete()
    {
        var s = DevRules.Summarize([
            Project(DevStatus.Requested, (1, null, null, 1000)), Project(DevStatus.Quoted, (1, null, null, 2000)),
            Project(DevStatus.Approved, (1, null, null, 300)), Project(DevStatus.InDevelopment, (1, null, null, 400)), Project(DevStatus.Test, (1, null, null, 500)),
            Project(DevStatus.Complete, (1, null, null, 7000)), Project(DevStatus.OnHold, (1, null, null, 50)), Project(DevStatus.Cancelled, (1, null, null, 60)),
        ]);
        Assert.Equal((3000m, 1200m, 7000m), (s.Proposed, s.Committed, s.Completed));
        Assert.Equal((1, 7000m), s.ByStatus[DevStatus.Complete]);
        Assert.Equal(0, DevRules.Summarize([]).ByStatus[DevStatus.Test].Count);
        Assert.False(DevRules.IsOpen(DevStatus.OnHold));
        Assert.True(DevRules.IsOpen(DevStatus.Test));
    }

    [Fact]
    public void Draft_validation_and_completion_date()
    {
        Assert.NotNull(new DevProjectDraft().Validate());
        var d = new DevProjectDraft { ProductId = 1, Name = "ASN rework", Lines = [new DevCostLineDraft { CategoryId = 1, Hours = 10 }] };
        Assert.Contains("rate", d.Validate());
        d.Lines[0].Rate = 100;
        Assert.Null(d.Validate());
        d.Lines.Add(new DevCostLineDraft { CategoryId = 2 });
        Assert.Contains("amount", d.Validate());
        d.Lines[1].Amount = 500;
        d.Lines.Add(new DevCostLineDraft()); // blank lines are ignored
        Assert.Null(d.Validate());
        Assert.Equal(1500m, d.Total);

        var today = new DateOnly(2026, 11, 12);
        var p = new DevProject();
        d.Status = DevStatus.Complete;
        d.ApplyTo(p, today);
        Assert.Equal(today, p.CompletedOn);
        d.Status = DevStatus.Test;
        d.CompletedOn = today;
        d.ApplyTo(p, today);
        Assert.Null(p.CompletedOn);
        Assert.Equal("Deployed / Complete", PortfolioText.Label(DevStatus.Complete));
    }
}

/// <summary>The development pipeline against a database: saving, cost lines, categories, documents and the portfolio file.</summary>
public sealed class DevPipelineServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;
    private readonly RoadmapService _roadmaps;

    public DevPipelineServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        _svc = new PortfolioService(_factory, clock);
        _roadmaps = new RoadmapService(_factory, clock);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<int> CategoryAsync(string name) => (await _svc.GetDevCostCategoriesAsync()).Single(c => c.Name == name).Id;

    [Fact]
    public async Task Projects_save_with_cost_lines_and_move_through_the_pipeline()
    {
        var product = (await _roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        Assert.Equal(["DEV", "QA", "EDI", "PM"], (await _svc.GetDevCostCategoriesAsync()).Select(c => c.Name));
        var (dev, qa, pm) = (await CategoryAsync("DEV"), await CategoryAsync("QA"), await CategoryAsync("PM"));
        var customer = await _svc.SavePartnerAsync(new TradingPartnerDraft { Name = "Acme Foods", Type = PartnerType.Customer });

        var id = await _svc.SaveDevProjectAsync(new DevProjectDraft
        {
            ProductId = product, Name = "Acme ASN enhancement", Status = DevStatus.Quoted, Reference = "Q-1042", CustomerId = customer,
            Lines = [new() { CategoryId = dev, Hours = 40, Rate = 150 }, new() { CategoryId = qa, Hours = 10, Rate = 95 }, new() { CategoryId = pm, Amount = 1200 }],
        });
        var p = (await _svc.GetDevProjectsAsync(product)).Single();
        Assert.Equal((id, 8150m, 50m, "Acme Foods"), (p.Id, p.TotalCost, p.TotalHours, p.Customer!.Name));

        // Edit: change one line, drop one, add one. Unchanged lines keep their ids.
        var draft = DevProjectDraft.From(p);
        var devLineId = draft.Lines[0].Id;
        draft.Lines[0].Hours = 50;
        draft.Lines.RemoveAt(1);
        draft.Lines.Add(new DevCostLineDraft { CategoryId = qa, Amount = 800 });
        draft.Status = DevStatus.Approved;
        await _svc.SaveDevProjectAsync(draft);
        p = (await _svc.GetDevProjectAsync(id))!;
        Assert.Equal(3, p.CostLines.Count);
        Assert.Equal(7500m + 1200m + 800m, p.TotalCost);
        Assert.Contains(p.CostLines, l => l.Id == devLineId && l.Hours == 50);

        await _svc.SetDevProjectStatusAsync(id, DevStatus.Complete);
        p = (await _svc.GetDevProjectAsync(id))!;
        Assert.Equal((DevStatus.Complete, new DateOnly(2026, 11, 12)), (p.Status, p.CompletedOn));

        var history = await _svc.GetActivityAsync(PortfolioEntityType.DevProject, id);
        Assert.Contains(history, h => h.Field == "Status" && h.NewValue == "Deployed / Complete");
        Assert.Contains(history, h => h.Field == "DEV cost · Hours" && h.OldValue == "40" && h.NewValue == "50");
        Assert.Contains(history, h => h.Field == "QA cost" && h.Action == AuditAction.Deleted);

        var pipeline = (await _svc.GetDevPipelineAsync(product))!;
        Assert.Equal(9500m, pipeline.Summary.Completed);
        Assert.Null(await _svc.GetDevPipelineAsync(999));
    }

    [Fact]
    public async Task Rules_are_enforced()
    {
        var wms = (await _roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var tms = (await _roadmaps.CreateProductAsync("TMS", null, ScoringModel.Rice)).Id;
        var tmsItem = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = tms, ItemType = ItemType.Goal, Title = "Cut freight cost" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDevProjectAsync(new DevProjectDraft { ProductId = wms, Name = " " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDevProjectAsync(new DevProjectDraft { ProductId = 999, Name = "X" }));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveDevProjectAsync(new DevProjectDraft { ProductId = wms, Name = "X", RoadmapItemId = tmsItem }));
        Assert.Contains("roadmap", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveDevProjectAsync(new DevProjectDraft { ProductId = wms, Name = "X", Lines = [new() { CategoryId = 999, Amount = 1 }] }));

        var id = await _svc.SaveDevProjectAsync(new DevProjectDraft { ProductId = wms, Name = "X" });
        var moved = DevProjectDraft.From((await _svc.GetDevProjectAsync(id))!);
        moved.ProductId = tms;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDevProjectAsync(moved));
    }

    [Fact]
    public async Task Categories_can_be_added_renamed_reordered_and_deleted_when_unused()
    {
        var product = (await _roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var ba = await _svc.SaveDevCostCategoryAsync(0, " BA ", 120);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDevCostCategoryAsync(0, "dev", null));
        await _svc.SaveDevCostCategoryAsync(ba, "Business analysis", 125);
        await _svc.MoveDevCostCategoryAsync(ba, -1);
        Assert.Equal(["DEV", "QA", "EDI", "Business analysis", "PM"], (await _svc.GetDevCostCategoriesAsync()).Select(c => c.Name));
        Assert.Equal(125m, (await _svc.GetDevCostCategoriesAsync()).Single(c => c.Id == ba).DefaultRate);

        await _svc.SaveDevProjectAsync(new DevProjectDraft { ProductId = product, Name = "X", Lines = [new() { CategoryId = ba, Hours = 2, Rate = 125 }] });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteDevCostCategoryAsync(ba));
        Assert.Contains("1 cost line", ex.Message);
        Assert.Equal(1, (await _svc.GetDevCostCategoryUsageAsync())[ba]);
        await _svc.DeleteDevCostCategoryAsync(await CategoryAsync("EDI"));
        Assert.DoesNotContain(await _svc.GetDevCostCategoriesAsync(), c => c.Name == "EDI");
    }

    [Fact]
    public async Task A_projects_SOW_belongs_to_its_product_and_stays_when_the_project_goes()
    {
        var product = (await _roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var id = await _svc.SaveDevProjectAsync(new DevProjectDraft { ProductId = product, Name = "Wave release rework" });
        var target = new DocumentTarget(DocumentTargetKind.DevProject, id);
        Assert.Equal("d" + id, target.Key);
        Assert.Equal(DocumentCategory.Sow, DocumentDraft.New(target).Category);

        var doc = await _svc.SaveDocumentAsync(new DocumentDraft
        {
            Title = "SOW v2", Category = DocumentCategory.Sow, Url = "https://acme.sharepoint.com/sites/IT/SOW/wave.pdf", Target = target,
        });
        var saved = (await _svc.GetDocumentsAsync(target)).Single();
        Assert.Equal((doc, product, "Wave release rework"), (saved.Id, saved.ProductId, saved.TargetName));
        Assert.Single((await _svc.GetDevPipelineAsync(product))!.DocumentsOf(id));
        Assert.Contains(await _svc.GetDocumentTargetOptionsAsync(), o => o.Target == target);

        await _svc.DeleteDevProjectAsync(id);
        var kept = (await _svc.GetDocumentAsync(doc))!;
        Assert.Null(kept.DevProjectId);
        Assert.Equal(product, kept.ProductId);
    }

    [Fact]
    public async Task The_pipeline_travels_in_the_portfolio_file()
    {
        var product = (await _roadmaps.CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var ba = await _svc.SaveDevCostCategoryAsync(0, "BA", 110);
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Blue Yonder" });
        var id = await _svc.SaveDevProjectAsync(new DevProjectDraft
        {
            ProductId = product, Name = "Acme ASN enhancement", Status = DevStatus.Test, VendorId = vendor, TargetDate = new DateOnly(2026, 12, 1),
            Lines = [new() { CategoryId = await CategoryAsync("DEV"), Hours = 40, Rate = 150 }, new() { CategoryId = ba, Amount = 900, Note = "fixed" }],
        });
        await _svc.SaveDocumentAsync(new DocumentDraft
        {
            Title = "SOW", Category = DocumentCategory.Sow, Url = "https://acme.sharepoint.com/sites/IT/SOW/asn.pdf", Target = new DocumentTarget(DocumentTargetKind.DevProject, id),
        });

        using var buffer = new MemoryStream((await _svc.ExportPortfolioAsync()).ToBytes());
        var file = PortfolioFile.Read(buffer);
        Assert.Equal((5, 1), (file.DevCostCategories.Count, file.DevProjects.Count));

        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(other).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var target = new PortfolioService(factory);
        var targetProduct = (await new RoadmapService(factory).CreateProductAsync("WMS", null, ScoringModel.Rice)).Id;
        var result = await target.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal(1, result.Added["development projects"]);

        var pipeline = (await target.GetDevPipelineAsync(targetProduct))!;
        var p = pipeline.Projects.Single();
        Assert.Equal((DevStatus.Test, 6900m, "Blue Yonder"), (p.Status, p.TotalCost, p.Vendor!.Name));
        Assert.Equal(["DEV", "QA", "EDI", "PM", "BA"], pipeline.Categories.Select(c => c.Name));
        Assert.Equal("SOW", pipeline.DocumentsOf(p.Id).Single().Title);

        // Merging again adds nothing.
        var again = await target.ImportPortfolioAsync(file, PortfolioImportMode.Merge);
        Assert.Equal(0, again.Added.GetValueOrDefault("development projects"));
        Assert.Single((await target.GetDevProjectsAsync(targetProduct)));
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
