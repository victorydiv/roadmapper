using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Runs the portfolio service against a real in-memory SQLite database built from the migrations.</summary>
public sealed class PortfolioServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;
    private readonly RoadmapService _roadmaps;
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    public PortfolioServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        _svc = new PortfolioService(_factory, _clock);
        _roadmaps = new RoadmapService(_factory);
    }

    public void Dispose() => _connection.Dispose();

    private DateOnly Today => _svc.Today;

    private async Task<(int Vendor, int Contract)> VendorWithContract(string name = "Acme", int renewInDays = 100, int notice = 30)
    {
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = name });
        var contract = await _svc.SaveContractAsync(new ContractDraft
        {
            VendorId = vendor, Name = $"{name} subscription", RenewalDate = Today.AddDays(renewInDays), NoticePeriodDays = notice,
        });
        return (vendor, contract);
    }

    [Fact]
    public async Task Application_renewal_and_notice_deadline_come_from_its_contract()
    {
        var (vendor, contract) = await VendorWithContract(renewInDays: 100, notice: 30);
        var id = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS", VendorId = vendor, ContractId = contract });

        var app = (await _svc.GetApplicationAsync(id))!;
        Assert.Equal(Today.AddDays(100), app.RenewalDate);
        Assert.Equal(Today.AddDays(70), app.NoticeDeadline);
        Assert.Equal("Acme", app.Vendor!.Name);
    }

    [Fact]
    public async Task A_contract_from_another_vendor_is_rejected()
    {
        var a = await VendorWithContract("A");
        var b = await VendorWithContract("B");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveApplicationAsync(new ApplicationDraft { Name = "TMS", VendorId = a.Vendor, ContractId = b.Contract }));
        Assert.Contains("different vendor", ex.Message);
    }

    [Fact]
    public async Task Application_and_vendor_names_are_unique_ignoring_case()
    {
        await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "Northwind WMS" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveApplicationAsync(new ApplicationDraft { Name = "northwind wms" }));

        await _svc.SaveVendorAsync(new VendorDraft { Name = "Contoso" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveVendorAsync(new VendorDraft { Name = "CONTOSO " }));
    }

    [Fact]
    public async Task A_roadmap_can_belong_to_only_one_application()
    {
        var pid = (await _roadmaps.CreateProductAsync("Portal roadmap", null, ScoringModel.Rice)).Id;
        await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "Portal", ProductId = pid });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveApplicationAsync(new ApplicationDraft { Name = "Portal 2", ProductId = pid }));
        Assert.Contains("“Portal”", ex.Message);
        // Several applications with no roadmap is fine.
        await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "A" });
        await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "B" });
    }

    [Fact]
    public async Task Linked_items_read_live_from_the_roadmap_and_go_when_the_product_is_deleted()
    {
        var pid = (await _roadmaps.CreateProductAsync("EDI program", null, ScoringModel.Rice)).Id;
        var goal = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = pid, ItemType = ItemType.Goal, Title = "Modernize EDI" });
        var init = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = pid, ItemType = ItemType.Initiative, ParentId = goal, Title = "Move 214s to API" });
        var wms = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS", ProductId = pid });
        var tms = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "TMS" });

        await _svc.LinkItemAsync(wms, init, InitiativeImpact.Integrates);
        await _svc.LinkItemAsync(tms, init, InitiativeImpact.Integrates);
        await _svc.LinkItemAsync(tms, init, InitiativeImpact.Upgrades); // re-linking changes the impact

        var options = await _svc.GetLinkableItemsAsync(pid);
        Assert.Equal(init, options[0].Id); // Initiatives first
        Assert.Equal("Modernize EDI", options[0].Path);

        var linked = Assert.Single(await _svc.GetLinkedItemsAsync(tms));
        Assert.Equal(("Move 214s to API", "EDI program", InitiativeImpact.Upgrades), (linked.Title, linked.ProductName, linked.Impact));
        Assert.Equal(2, (await _svc.GetLinksForItemAsync(init)).Count);

        await _roadmaps.DeleteProductAsync(pid);
        Assert.Empty(await _svc.GetLinkedItemsAsync(tms));
        Assert.Null((await _svc.GetApplicationAsync(wms))!.ProductId);
    }

    [Fact]
    public async Task Changes_are_recorded_with_names_instead_of_ids()
    {
        var (vendor, contract) = await VendorWithContract("Fabrikam");
        var owner = await _svc.SaveContactAsync(new ContactDraft { Name = "Priya Shah" });
        var id = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "TMS" });
        await _svc.SaveApplicationAsync(new ApplicationDraft
        {
            Id = id, Name = "TMS", VendorId = vendor, ContractId = contract, BusinessOwnerId = owner, Criticality = Criticality.Tier1,
        });

        var history = await _svc.GetActivityAsync(PortfolioEntityType.Application, id);
        Assert.Contains(history, h => h.Action == AuditAction.Created && h.Field is null);
        Assert.Contains(history, h => h.Field == "Vendor" && h.NewValue == "Fabrikam");
        Assert.Contains(history, h => h.Field == "Business owner" && h.NewValue == "Priya Shah");
        Assert.Contains(history, h => h.Field == "Criticality" && h.OldValue == "Tier 2" && h.NewValue == "Tier 1");

        // Contract edits are filed under the vendor.
        var draft = ContractDraft.From((await _svc.GetVendorAsync(vendor))!.Contracts.Single());
        draft.NoticePeriodDays = 90;
        await _svc.SaveContractAsync(draft);
        var vendorHistory = await _svc.GetActivityAsync(PortfolioEntityType.Vendor, vendor);
        Assert.Contains(vendorHistory, h => h.Field == "Contract “Fabrikam subscription” · Notice period (days)" && h.OldValue == "30" && h.NewValue == "90");
    }

    [Fact]
    public async Task Deleting_a_vendor_removes_its_contracts_and_people_but_keeps_its_applications()
    {
        var (vendor, contract) = await VendorWithContract();
        var am = await _svc.SaveContactAsync(new ContactDraft { Name = "Account Manager", VendorId = vendor });
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS", VendorId = vendor, ContractId = contract, TechnicalOwnerId = am });

        await _svc.DeleteVendorAsync(vendor);

        var kept = (await _svc.GetApplicationAsync(app))!;
        Assert.Null(kept.VendorId);
        Assert.Null(kept.ContractId);
        Assert.Null(kept.TechnicalOwnerId);
        Assert.Empty(await _svc.GetContactsAsync());
        Assert.Contains(await _svc.GetActivityAsync(PortfolioEntityType.Vendor, vendor), h => h.Action == AuditAction.Deleted);
    }

    [Fact]
    public async Task Escalation_path_keeps_levels_in_order()
    {
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "V" });
        var a = await _svc.SaveContactAsync(new ContactDraft { Name = "A", VendorId = vendor });
        var b = await _svc.SaveContactAsync(new ContactDraft { Name = "B", VendorId = vendor });
        var c = await _svc.SaveContactAsync(new ContactDraft { Name = "C", VendorId = vendor });
        foreach (var id in new[] { a, b, c }) await _svc.AddEscalationContactAsync(vendor, id, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AddEscalationContactAsync(vendor, a, null));

        await _svc.MoveEscalationContactAsync(vendor, c, -1); // A, C, B
        await _svc.RemoveEscalationContactAsync(vendor, a);    // C, B

        var path = (await _svc.GetVendorAsync(vendor))!.EscalationContacts;
        Assert.Equal(new[] { ("C", 1), ("B", 2) }, path.Select(x => (x.Contact!.Name, x.Level)));
    }

    [Fact]
    public async Task Renewals_are_listed_by_notice_deadline_within_the_window()
    {
        var soon = await VendorWithContract("Soon", renewInDays: 60, notice: 30);   // notice in 30 days
        await VendorWithContract("Later", renewInDays: 400, notice: 90);            // notice in 310 days
        var missed = await VendorWithContract("Missed", renewInDays: 20, notice: 60); // notice was 40 days ago
        await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "EDI", VendorId = soon.Vendor, ContractId = soon.Contract });

        var rows = await _svc.GetRenewalsAsync(120);
        Assert.Equal(new[] { "Missed", "Soon" }, rows.Select(r => r.VendorName));
        Assert.Equal(new[] { "EDI" }, rows[1].Applications);
        Assert.True(PortfolioRules.NoticeMissed(rows[0].Contract, Today));
        Assert.True(PortfolioRules.NoticeDue(rows[1].Contract, Today, 120));
    }

    [Fact]
    public async Task Contact_roles_explain_where_a_person_is_named()
    {
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "V" });
        var p = await _svc.SaveContactAsync(new ContactDraft { Name = "P" });
        await _svc.SaveVendorAsync(new VendorDraft { Id = vendor, Name = "V", AccountManagerId = p });
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS", BusinessOwnerId = p });
        await _svc.AddSupportContactAsync(app, p, SupportRole.KeyUser, null);

        var roles = await _svc.GetContactRolesAsync(p);
        Assert.Equal(new[] { "Business owner of WMS", "Account manager for V", "Key user for WMS" }, roles);

        await _svc.DeleteContactAsync(p);
        Assert.Null((await _svc.GetApplicationAsync(app))!.BusinessOwnerId);
    }

    [Fact]
    public async Task Sample_portfolio_links_to_the_sample_product_and_only_seeds_once()
    {
        await DbInitializer.SeedSampleAsync(_roadmaps);
        await _svc.SeedSampleAsync();

        var apps = await _svc.GetApplicationsAsync();
        Assert.Equal(7, apps.Count);
        var portal = apps.Single(a => a.Name == "Customer Portal");
        Assert.Equal(PortfolioService.SampleProductName, portal.Product!.Name);
        Assert.Equal(2, (await _svc.GetLinkedItemsAsync(portal.Id)).Count);
        Assert.Contains(apps, a => a.TimeQuadrant == TimeQuadrant.Migrate);
        Assert.NotEmpty(await _svc.GetRenewalsAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SeedSampleAsync());
    }

    [Theory]
    [InlineData(4, 4, TimeQuadrant.Invest)]
    [InlineData(4, 2, TimeQuadrant.Migrate)]
    [InlineData(2, 4, TimeQuadrant.Tolerate)]
    [InlineData(1, 2, TimeQuadrant.Eliminate)]
    [InlineData(3, 3, TimeQuadrant.Invest)]
    public void Time_quadrant_comes_from_business_and_technical_fit(int business, int technical, TimeQuadrant expected) =>
        Assert.Equal(expected, PortfolioRules.Quadrant(business, technical));

    [Fact]
    public void Time_quadrant_needs_both_scores() => Assert.Null(PortfolioRules.Quadrant(4, null));

    [Fact]
    public void Drafts_validate_fit_scores_and_dates()
    {
        Assert.NotNull(new ApplicationDraft { Name = "X", BusinessFit = 6 }.Validate());
        Assert.NotNull(new ApplicationDraft { Name = "X", ContractId = 1 }.Validate());
        Assert.NotNull(new ContractDraft { Name = "C", StartDate = new(2026, 2, 1), EndDate = new(2026, 1, 1) }.Validate());
        Assert.NotNull(new ContactDraft { Name = "N", Email = "not an email" }.Validate());
        Assert.Null(new ApplicationDraft { Name = "X", BusinessFit = 5, TechnicalFit = 1 }.Validate());
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
