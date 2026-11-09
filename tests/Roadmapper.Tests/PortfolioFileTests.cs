using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>The .rpf portfolio file and Jira links, against real in-memory SQLite databases.</summary>
public sealed class PortfolioFileTests : IDisposable
{
    private sealed class Db : IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        public readonly TestDbFactory Factory;
        public readonly PortfolioService Portfolio;
        public readonly RoadmapService Roadmaps;

        public Db()
        {
            _connection.Open();
            Factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
            using (var db = Factory.CreateDbContext()) db.Database.Migrate();
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
            Portfolio = new PortfolioService(Factory, clock);
            Roadmaps = new RoadmapService(Factory, clock);
        }

        public void Dispose() => _connection.Dispose();
    }

    private readonly List<Db> _dbs = [];

    private Db NewDb()
    {
        var db = new Db();
        _dbs.Add(db);
        return db;
    }

    public void Dispose() => _dbs.ForEach(d => d.Dispose());

    private async Task<Db> SampleAsync()
    {
        var db = NewDb();
        await DbInitializer.SeedSampleAsync(db.Roadmaps);
        await db.Portfolio.SeedSampleAsync();
        await db.Portfolio.SaveSettingsAsync(32m, 90, 6, "https://acme.atlassian.net");
        return db;
    }

    /// <summary>The file's content with ids replaced by names, so two exports can be compared whatever ids they got.</summary>
    private static List<string> Fingerprint(PortfolioFile f)
    {
        var contacts = f.Contacts.ToDictionary(c => c.Id, c => c.Name);
        var vendors = f.Vendors.ToDictionary(v => v.Id, v => v.Name);
        var contracts = f.Vendors.SelectMany(v => v.Contracts).ToDictionary(c => c.Id, c => c.Name);
        var partners = f.Partners.ToDictionary(p => p.Id, p => p.Name);
        var apps = f.Applications.ToDictionary(a => a.Id, a => a.Name);
        var flows = f.Integrations.ToDictionary(i => i.Id, i => i.Name);
        var processes = f.Processes.ToDictionary(p => p.Id, p => p.Name);
        var kpis = f.Processes.SelectMany(p => p.Kpis).ToDictionary(k => k.Id, k => k.Name);
        var decisions = f.Decisions.ToDictionary(d => d.Id, d => d.Title);
        static string N(Dictionary<int, string> map, int? id) => id is { } x ? map[x] : "-";

        var lines = new List<string>
        {
            $"settings {f.Settings?.LaborRate} {f.Settings?.RenewalWarningDays} {f.Settings?.DefaultReviewIntervalMonths} {f.Settings?.JiraBaseUrl}",
            $"history {f.History.Count}",
        };
        lines.AddRange(f.Contacts.Select(c => $"contact {c.Name} {c.Title} {c.Email} {N(vendors, c.VendorId)} {N(partners, c.PartnerId)} {c.Department} {c.IsActive}"));
        lines.AddRange(f.Vendors.Select(v => $"vendor {v.Name} {N(contacts, v.AccountManagerId)} {N(contacts, v.TechnicalContactId)} {v.SupportPhone} " +
            string.Join(",", v.Escalation.Select(e => $"{e.Level}:{contacts[e.ContactId]}"))));
        lines.AddRange(f.Vendors.SelectMany(v => v.Contracts.Select(c => $"contract {v.Name} {c.Name} {c.RenewalDate} {c.NoticePeriodDays} {c.AnnualValue} {c.AutoRenews}")));
        lines.AddRange(f.Partners.Select(p => $"partner {p.Name} {p.Type} {p.Importance} {p.EdiQualifier}:{p.EdiId} {N(contacts, p.RelationshipOwnerId)}"));
        lines.AddRange(f.Applications.Select(a => $"app {a.Name} {a.Category} {a.Criticality} {N(vendors, a.VendorId)} {N(contracts, a.ContractId)} {a.Roadmap} " +
            $"{N(contacts, a.BusinessOwnerId)} {N(contacts, a.TechnicalOwnerId)} {a.AnnualCost} {a.BusinessFit}/{a.TechnicalFit} " +
            string.Join(",", a.Support.Select(s => $"{contacts[s.ContactId]}:{s.Role}").Order()) + " " +
            string.Join(",", a.Initiatives.Select(i => $"{i.Item}:{i.Impact}").Order())));
        lines.AddRange(f.Integrations.Select(i => $"flow {i.Name} {N(apps, i.SourceApplicationId)} {N(partners, i.SourcePartnerId)} {N(apps, i.ViaApplicationId)} " +
            $"{N(apps, i.TargetApplicationId)} {N(partners, i.TargetPartnerId)} {i.Type} {i.Criticality} {N(contacts, i.OwnerId)} {i.LastReviewDate} " +
            string.Join(",", i.Risks.Select(r => $"{r.Description}:{r.Likelihood}x{r.Impact}:{N(contacts, r.OwnerId)}"))));
        lines.AddRange(f.Processes.Select(p => $"process {p.Name} {N(processes, p.ParentId)} {p.Domain} {N(contacts, p.OwnerId)} " +
            string.Join(",", p.Applications.Select(a => $"{apps[a.ApplicationId]}:{a.Usage}:{a.IsPrimary}").Order()) + " " +
            string.Join(",", p.IntegrationIds.Select(i => flows[i]).Order()) + " " +
            string.Join(",", p.PainPoints.Select(x => $"{x.Description}:{x.Severity}:{N(apps, x.ApplicationId)}:{x.AddressedBy}")) + " " +
            string.Join(",", p.Kpis.Select(k => $"{k.Name}:{k.Baseline}>{k.Target}@{k.Current}"))));
        lines.AddRange(f.Benefits.Select(b => $"benefit {b.Item} {b.Description} {N(apps, b.ApplicationId)} {N(kpis, b.KpiId)} {b.ExpectedValue} " +
            string.Join(",", b.Measurements.Select(m => $"{m.MeasuredOn}:{m.Value}"))));
        lines.AddRange(f.Decisions.Select(d => $"decision {d.Title} {d.DecidedOn} {d.Status} {N(decisions, d.SupersededById)} " +
            string.Join(",", d.Alternatives.Select(a => a.Option)) + " " +
            string.Join(",", d.Links.Select(l => l.Kind switch
            {
                DecisionTargetKind.Application => apps[l.Id!.Value], DecisionTargetKind.Integration => flows[l.Id!.Value],
                DecisionTargetKind.Vendor => vendors[l.Id!.Value], DecisionTargetKind.Contract => contracts[l.Id!.Value],
                DecisionTargetKind.Process => processes[l.Id!.Value], DecisionTargetKind.TradingPartner => partners[l.Id!.Value],
                _ => l.Item!.ToString(),
            }).Order())));
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static PortfolioFile ThroughBytes(PortfolioFile f) => PortfolioFile.Read(new MemoryStream(f.ToBytes()));

    [Fact]
    public async Task A_restored_portfolio_exports_the_same_file()
    {
        var source = await SampleAsync();
        var file = ThroughBytes(await source.Portfolio.ExportPortfolioAsync("1.2.0"));
        Assert.NotEmpty(file.Benefits);
        Assert.Contains(file.Decisions, d => d.Links.Any(l => l.Kind == DecisionTargetKind.Contract));

        var target = NewDb();
        await DbInitializer.SeedSampleAsync(target.Roadmaps); // the roadmap the links point into
        var result = await target.Portfolio.ImportPortfolioAsync(file, PortfolioImportMode.Restore);

        Assert.Empty(result.Unmatched);
        Assert.Empty(result.Skipped);
        Assert.Equal(file.Applications.Count, result.Added["applications"]);
        Assert.Equal(Fingerprint(file), Fingerprint(await target.Portfolio.ExportPortfolioAsync()));

        // History is restored and re-pointed at the new records.
        var wms = (await target.Portfolio.GetApplicationsAsync()).Single(a => a.Name == "Northwind WMS");
        Assert.Contains(await target.Portfolio.GetActivityAsync(PortfolioEntityType.Application, wms.Id), h => h.Action == AuditAction.Created);
    }

    [Fact]
    public async Task Roadmap_links_without_a_matching_roadmap_are_reported_not_fatal()
    {
        var file = await (await SampleAsync()).Portfolio.ExportPortfolioAsync();
        var target = NewDb(); // no sample roadmap here

        var result = await target.Portfolio.ImportPortfolioAsync(file, PortfolioImportMode.Restore);

        Assert.Contains(result.Unmatched, u => u.StartsWith("Roadmap of Customer Portal"));
        Assert.Contains(result.Unmatched, u => u.StartsWith("Benefit"));
        Assert.Contains(result.Unmatched, u => u.StartsWith("Pain point fix"));
        Assert.False(result.Added.ContainsKey("benefits") && result.Added["benefits"] > 0);
        var portal = (await target.Portfolio.GetApplicationsAsync()).Single(a => a.Name == "Customer Portal");
        Assert.Null(portal.ProductId);
        Assert.Equal(file.Integrations.Count, (await target.Portfolio.GetIntegrationsAsync()).Count);
    }

    [Fact]
    public async Task Restore_needs_an_empty_portfolio()
    {
        var db = await SampleAsync();
        var file = await db.Portfolio.ExportPortfolioAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Portfolio.ImportPortfolioAsync(file, PortfolioImportMode.Restore));
        Assert.Contains("Merge", ex.Message);
    }

    [Fact]
    public async Task Merging_into_itself_adds_nothing()
    {
        var db = await SampleAsync();
        var file = await db.Portfolio.ExportPortfolioAsync();
        var before = Fingerprint(file);

        var result = await db.Portfolio.ImportPortfolioAsync(ThroughBytes(file), PortfolioImportMode.Merge);

        Assert.All(result.Added.Values, n => Assert.Equal(0, n));
        Assert.Contains("Application Northwind WMS (already here)", result.Skipped);
        Assert.Contains(result.Skipped, s => s.StartsWith("Decision Keep Contoso"));
        var after = await db.Portfolio.ExportPortfolioAsync();
        Assert.Equal(before.Where(l => !l.StartsWith("history")), Fingerprint(after).Where(l => !l.StartsWith("history")));
    }

    [Fact]
    public async Task A_merge_reuses_records_that_are_already_here()
    {
        var file = await (await SampleAsync()).Portfolio.ExportPortfolioAsync();
        var target = NewDb();
        await DbInitializer.SeedSampleAsync(target.Roadmaps);
        var wms = await target.Portfolio.SaveApplicationAsync(new ApplicationDraft { Name = "northwind wms", Criticality = Criticality.Tier2 });
        await target.Portfolio.SaveSettingsAsync(50m, 120, 12, null);

        var result = await target.Portfolio.ImportPortfolioAsync(file, PortfolioImportMode.Merge);

        Assert.Equal(["Application Northwind WMS (already here)"], result.Skipped);
        Assert.Equal(file.Applications.Count - 1, result.Added["applications"]);
        var app = (await target.Portfolio.GetApplicationAsync(wms))!;
        Assert.Equal(Criticality.Tier2, app.Criticality); // left as it was
        Assert.Equal(file.Integrations.Count(i => i.SourceApplicationId == file.Applications.Single(a => a.Name == "Northwind WMS").Id),
            (await target.Portfolio.GetIntegrationsAsync()).Count(i => i.SourceApplicationId == wms)); // flows point at the existing WMS
        Assert.Equal(50m, (await target.Portfolio.GetSettingsAsync()).LaborRate); // a merge keeps this portfolio's settings
        Assert.NotEmpty(await target.Portfolio.GetRecentActivityAsync()); // merged records are recorded as created
    }

    [Fact]
    public async Task A_minimal_hand_written_file_imports()
    {
        const string json = """
            { "format": "roadmapper-portfolio", "version": 1,
              "applications": [ { "id": 1, "name": "Synapse", "category": "WMS", "criticality": "Tier1", "status": "Retiring", "hosting": "Hosted" } ],
              "partners": [ { "id": 7, "name": "Morton Salt", "type": "Customer", "status": "Active", "importance": "Key" } ],
              "integrations": [ { "id": 3, "name": "Morton orders", "sourcePartnerId": 7, "targetApplicationId": 1, "type": "EDI", "businessPurpose": "Orders" } ] }
            """;
        var file = PortfolioFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        var db = NewDb();
        var result = await db.Portfolio.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal(1, result.Added["integrations"]);
        Assert.Equal("Morton Salt", (await db.Portfolio.GetIntegrationsAsync()).Single().SourcePartner!.Name);
    }

    [Theory]
    [InlineData("""{ "format": "roadmapper", "version": 1 }""", "roadmap (.rmp) file")]
    [InlineData("""{ "format": "roadmapper-portfolio", "version": 99 }""", "newer version")]
    [InlineData("""{ "format": "roadmapper-portfolio", "version": 1, "applications": [ { "id": 1, "name": "A", "vendorId": 5 } ] }""", "vendor 5")]
    [InlineData("""{ "format": "roadmapper-portfolio", "version": 1, "applications": [ { "id": 1, "name": "A" }, { "id": 1, "name": "B" } ] }""", "share id 1")]
    [InlineData("""not json""", "isn't a readable")]
    public void Bad_files_are_rejected_with_a_reason(string json, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PortfolioFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        Assert.Contains(expected, ex.Message);
    }

    // ---- Jira links ----

    [Theory]
    [InlineData("WMS-123", null, true)]
    [InlineData("wms-123", null, true)]   // upper-cased on save
    [InlineData("WMS123", null, false)]
    [InlineData(null, "https://acme.atlassian.net/browse/WMS-1", true)]
    [InlineData(null, "acme.atlassian.net/browse/WMS-1", false)]
    [InlineData(null, "javascript:alert(1)", false)]
    public void Item_links_are_validated(string? key, string? url, bool ok)
    {
        var draft = new ItemDraft { Title = "x", JiraKey = key, ExternalUrl = url };
        Assert.Equal(ok, draft.Validate() is null);
    }

    [Fact]
    public void The_link_prefers_the_items_own_url_then_the_jira_site()
    {
        Assert.Equal("https://acme.atlassian.net/browse/WMS-12", ExternalLinks.Url("WMS-12", null, "https://acme.atlassian.net/"));
        Assert.Equal("https://wiki/x", ExternalLinks.Url("WMS-12", "https://wiki/x", "https://acme.atlassian.net"));
        Assert.Null(ExternalLinks.Url("WMS-12", null, null));
    }

    [Fact]
    public async Task Jira_keys_save_and_travel_in_rmp_files_and_spreadsheets()
    {
        var db = NewDb();
        var product = await db.Roadmaps.CreateProductAsync("WMS", null, ScoringModel.Wsjf);
        var goal = await db.Roadmaps.SaveItemAsync(new ItemDraft { ProductId = product.Id, ItemType = ItemType.Goal, Title = "Go live", JiraKey = "wms-7", ExternalUrl = " https://x.example/7 " });

        var roadmap = (await db.Roadmaps.GetRoadmapAsync(product.Id))!;
        var item = roadmap.Find(goal)!;
        Assert.Equal("WMS-7", item.JiraKey);
        Assert.Equal("https://x.example/7", item.ExternalUrl);

        var rmp = (await db.Roadmaps.ExportFileAsync(product.Id))!;
        var copy = await db.Roadmaps.ImportFileAsync(RoadmapFile.Read(new MemoryStream(rmp.ToBytes())));
        Assert.Equal("WMS-7", (await db.Roadmaps.GetRoadmapAsync(copy.Id))!.Items.Single().JiraKey);

        var csv = Roadmapper.Data.Exchange.RoadmapExport.ToCsv(roadmap);
        Assert.Contains("Jira key,Link", csv);
        Assert.Contains("WMS-7,https://x.example/7", csv);
        Assert.Equal(RoadmapColumns.JiraKey, RoadmapColumns.Guess("Issue key"));
    }

    [Fact]
    public async Task Settings_validate_the_jira_site()
    {
        var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Portfolio.SaveSettingsAsync(null, 120, 12, "acme.atlassian.net"));
        await db.Portfolio.SaveSettingsAsync(null, 120, 12, "https://acme.atlassian.net/");
        Assert.Equal("https://acme.atlassian.net", (await db.Portfolio.GetSettingsAsync()).JiraBaseUrl);
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
