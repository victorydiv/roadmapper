using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Costing pricing lines for a site (no database).</summary>
public class CostEstimateTests
{
    private static PricingLine Line(PricingBasis basis, decimal price, PriceFrequency freq = PriceFrequency.Monthly, decimal? min = null, decimal? qty = null,
        string label = "x", PriceSource source = PriceSource.Estimate, string? unit = null) =>
        new() { Category = AppCategory.YMS, ApplicationId = 1, Label = label, Basis = basis, UnitPrice = price, Frequency = freq, Minimum = min, Quantity = qty, Source = source, Unit = unit };

    private static readonly SiteProfile SmallDc = new() { Name = "Small DC", Users = 15, Licenses = 10, DockDoors = 12, Assets = 40, TransactionsPerMonth = 2000 };

    [Fact]
    public void Every_basis_takes_its_quantity_from_the_site()
    {
        Assert.Equal(1m, CostEstimate.For([Line(PricingBasis.Flat, 1)], SmallDc).Monthly);
        Assert.Equal(1m, CostEstimate.For([Line(PricingBasis.Site, 1)], SmallDc).Monthly);
        Assert.Equal(15m, CostEstimate.For([Line(PricingBasis.User, 1)], SmallDc).Monthly);
        Assert.Equal(10m, CostEstimate.For([Line(PricingBasis.License, 1)], SmallDc).Monthly);
        Assert.Equal(12m, CostEstimate.For([Line(PricingBasis.DockDoor, 1)], SmallDc).Monthly);
        Assert.Equal(40m, CostEstimate.For([Line(PricingBasis.Asset, 1)], SmallDc).Monthly);
        Assert.Equal(2000m, CostEstimate.For([Line(PricingBasis.Transaction, 1)], SmallDc).Monthly);
        Assert.Equal(3m, CostEstimate.For([Line(PricingBasis.Other, 1, qty: 3, unit: "kiosk")], SmallDc).Monthly);
        // A line's own quantity wins over the site's.
        Assert.Equal(5m, CostEstimate.For([Line(PricingBasis.User, 1, qty: 5)], SmallDc).Monthly);
    }

    [Fact]
    public void Monthly_annual_and_one_time_prices_add_up_with_minimums()
    {
        var c = CostEstimate.For(
        [
            Line(PricingBasis.Site, 1200),                                           // 1,200 / month
            Line(PricingBasis.User, 30, PriceFrequency.Annual),                      // 15 × 30 = 450 / year → 37.50 / month
            Line(PricingBasis.Transaction, 0.1m, min: 500),                          // 2000 × 0.10 = 200, min 500
            Line(PricingBasis.Flat, 90000, PriceFrequency.OneTime, label: "Implementation", source: PriceSource.Quote),
        ], SmallDc);
        Assert.True(c.IsComplete);
        Assert.Equal(1737.50m, c.Monthly);
        Assert.Equal(90000m, c.OneTime);
        Assert.Equal(1737.50m * 12 + 90000m, c.FirstYear);
        Assert.Equal(500m, c.Lines[2].Charge);
        Assert.Equal([PriceSource.Quote, PriceSource.Estimate], c.Sources);
        Assert.Equal(115.83m, c.MonthlyPer(SmallDc.Users));
        Assert.Null(c.MonthlyPer(0));
    }

    [Fact]
    public void Unknown_sizes_make_it_incomplete_and_no_lines_means_not_priced()
    {
        var site = SiteProfile.OneSite(AppCategory.YMS);
        var c = CostEstimate.For([Line(PricingBasis.Site, 500), Line(PricingBasis.DockDoor, 25), Line(PricingBasis.User, 10)], site);
        Assert.True(c.IsPriced);
        Assert.False(c.IsComplete);
        Assert.Equal(500m, c.Monthly); // only what could be costed
        Assert.Equal(["dock doors", "users"], c.Missing);
        Assert.Null(c.Lines[1].Charge);

        Assert.False(CostEstimate.NotPriced.IsPriced);
        Assert.False(CostEstimate.NotPriced.IsComplete);
    }

    [Fact]
    public void Matrix_costs_each_app_and_flags_the_cheapest_complete_one()
    {
        var apps = new[]
        {
            new Application { Id = 1, Name = "Kaleris", Category = AppCategory.YMS },
            new Application { Id = 2, Name = "Opendock", Category = AppCategory.YMS },
            new Application { Id = 3, Name = "Terminal", Category = AppCategory.YMS },
            new Application { Id = 4, Name = "Unpriced", Category = AppCategory.YMS },
        };
        PricingLine L(int app, PricingBasis b, decimal p) => new() { Category = AppCategory.YMS, ApplicationId = app, Label = "x", Basis = b, UnitPrice = p };
        var pricing = new[]
        {
            L(1, PricingBasis.Site, 1200), L(2, PricingBasis.DockDoor, 25), L(3, PricingBasis.User, 1),
            new PricingLine { Category = AppCategory.TMS, ApplicationId = 4, Label = "other comparison", UnitPrice = 1 },
        };
        var site = new SiteProfile { Name = "Small DC", DockDoors = 12 }; // users unknown → Terminal incomplete

        var m = CapabilityMatrix.Build(AppCategory.YMS, [], apps, [], [], pricing: pricing, site: site);
        Assert.Equal(1200m, m.Cost(1).Monthly);
        Assert.Equal(300m, m.Cost(2).Monthly);
        Assert.False(m.Cost(3).IsComplete);
        Assert.False(m.Cost(4).IsPriced); // its pricing is in another category's comparison
        Assert.Equal([2], m.Cheapest);
        Assert.Same(site, m.Site);
    }

    [Fact]
    public void Drafts_validate_prices_and_profiles()
    {
        PricingLineDraft D() => new() { Category = AppCategory.YMS, ApplicationId = 1, Label = "Subscription", UnitPrice = 10 };
        Assert.Null(D().Validate());
        Assert.NotNull(new PricingLineDraft { Label = "x" }.Validate());                       // no price
        var neg = D(); neg.UnitPrice = -1; Assert.NotNull(neg.Validate());
        var other = D(); other.Basis = PricingBasis.Other; Assert.NotNull(other.Validate());   // needs a unit and a quantity
        other.Unit = "kiosk"; Assert.NotNull(other.Validate());
        other.Quantity = 2; Assert.Null(other.Validate());
        var tx = D(); tx.Basis = PricingBasis.Transaction; tx.Frequency = PriceFrequency.Annual; Assert.NotNull(tx.Validate());

        Assert.NotNull(new SiteProfileDraft { Name = " " }.Validate());
        Assert.NotNull(new SiteProfileDraft { Name = "x", Users = -1 }.Validate());
        Assert.Equal("Your site", new SiteProfileDraft { Users = 5 }.ToProfile().Name);
    }
}

/// <summary>Pricing lines and site profiles through the service.</summary>
public sealed class CapabilityCostServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;

    public CapabilityCostServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        _svc = new PortfolioService(_factory, new FixedClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose() => _connection.Dispose();

    private Task<int> AppAsync(string name, AppCategory c = AppCategory.YMS) =>
        _svc.SaveApplicationAsync(new ApplicationDraft { Name = name, Category = c, Status = LifecycleStatus.Evaluating });

    private Task<int> PriceAsync(int app, string label, PricingBasis basis, decimal price, PriceFrequency freq = PriceFrequency.Monthly, AppCategory c = AppCategory.YMS) =>
        _svc.SavePricingLineAsync(new PricingLineDraft { Category = c, ApplicationId = app, Label = label, Basis = basis, UnitPrice = price, Frequency = freq });

    [Fact]
    public async Task Profiles_cost_the_comparison_and_the_first_is_the_default()
    {
        var kaleris = await AppAsync("Kaleris");
        var opendock = await AppAsync("Opendock");
        await PriceAsync(kaleris, "Subscription", PricingBasis.Site, 1200);
        await PriceAsync(kaleris, "Implementation", PricingBasis.Flat, 90000, PriceFrequency.OneTime);
        await PriceAsync(opendock, "Per door", PricingBasis.DockDoor, 25);

        // No profile yet: one site, door count unknown.
        var m = await _svc.GetCapabilityMatrixAsync(AppCategory.YMS);
        Assert.Equal("One site", m.Site.Name);
        Assert.Equal((1200m, 90000m), (m.Cost(kaleris).Monthly, m.Cost(kaleris).OneTime));
        Assert.False(m.Cost(opendock).IsComplete);

        var small = await _svc.SaveSiteProfileAsync(new SiteProfileDraft { Category = AppCategory.YMS, Name = "Small DC", DockDoors = 10 });
        var large = await _svc.SaveSiteProfileAsync(new SiteProfileDraft { Category = AppCategory.YMS, Name = "Large DC", DockDoors = 60 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveSiteProfileAsync(new SiteProfileDraft { Category = AppCategory.YMS, Name = "small dc" }));

        m = await _svc.GetCapabilityMatrixAsync(AppCategory.YMS);
        Assert.Equal("Small DC", m.Site.Name); // the first profile became the default
        Assert.Equal(250m, m.Cost(opendock).Monthly);
        Assert.Equal([opendock], m.Cheapest);

        m = await _svc.GetCapabilityMatrixAsync(AppCategory.YMS, profileId: large);
        Assert.Equal(1500m, m.Cost(opendock).Monthly);
        Assert.Equal([kaleris], m.Cheapest);

        // "Try your site": an unsaved profile.
        m = await _svc.GetCapabilityMatrixAsync(AppCategory.YMS, site: new SiteProfileDraft { DockDoors = 4 }.ToProfile());
        Assert.Equal(100m, m.Cost(opendock).Monthly);

        await _svc.SetDefaultSiteProfileAsync(large);
        Assert.Equal("Large DC", (await _svc.GetSiteProfilesAsync(AppCategory.YMS))[0].Name);
        await _svc.DeleteSiteProfileAsync(large);
        var left = Assert.Single(await _svc.GetSiteProfilesAsync(AppCategory.YMS));
        Assert.True(left.IsDefault);
        Assert.Equal(small, left.Id);
    }

    [Fact]
    public async Task Prices_need_the_app_in_the_comparison_and_show_on_its_page()
    {
        var wms = await AppAsync("Blue Yonder WMS", AppCategory.WMS);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PriceAsync(wms, "Yard add-on", PricingBasis.Site, 400));
        Assert.Contains("isn't in the YMS comparison", ex.Message);

        await _svc.AddComparisonMemberAsync(AppCategory.YMS, wms);
        var id = await PriceAsync(wms, "Yard add-on", PricingBasis.Site, 400);
        await _svc.SavePricingLineAsync(new PricingLineDraft { Id = id, Category = AppCategory.YMS, ApplicationId = wms, Label = "Yard add-on", UnitPrice = 450, Basis = PricingBasis.Site });

        var group = Assert.Single(await _svc.GetFeaturesForApplicationAsync(wms), g => g.Category == AppCategory.YMS);
        Assert.Equal(450m, Assert.Single(group.Pricing).UnitPrice);
        Assert.Equal(450m, group.Cost.Monthly);

        var history = await _svc.GetActivityAsync(PortfolioEntityType.Application, wms);
        Assert.Contains(history, h => h.Field == "YMS price “Yard add-on” · Price");

        await _svc.DeletePricingLineAsync(id);
        Assert.Empty(await _svc.GetPricingAsync(AppCategory.YMS, wms));
    }

    [Fact]
    public async Task Pricing_and_profiles_travel_in_the_portfolio_file()
    {
        var kaleris = await AppAsync("Kaleris");
        await PriceAsync(kaleris, "Subscription", PricingBasis.User, 40);
        await PriceAsync(kaleris, "Implementation", PricingBasis.Flat, 50000, PriceFrequency.OneTime);
        await _svc.SaveSiteProfileAsync(new SiteProfileDraft { Category = AppCategory.YMS, Name = "Small DC", Users = 15 });

        using var buffer = new MemoryStream((await _svc.ExportPortfolioAsync()).ToBytes());
        var file = PortfolioFile.Read(buffer);
        Assert.Equal((2, 1), (file.PricingLines.Count, file.SiteProfiles.Count));

        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(other).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var target = new PortfolioService(factory);
        var result = await target.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal((2, 1), (result.Added["prices"], result.Added["site profiles"]));

        var m = await target.GetCapabilityMatrixAsync(AppCategory.YMS);
        Assert.Equal("Small DC", m.Site.Name);
        Assert.True(m.Site.IsDefault);
        var cost = m.Cost(m.Apps.Single().Id);
        Assert.Equal((600m, 50000m), (cost.Monthly, cost.OneTime));

        // Merging again adds nothing.
        var again = await target.ImportPortfolioAsync(file, PortfolioImportMode.Merge);
        Assert.Equal(0, again.Added.GetValueOrDefault("prices"));
        Assert.Equal(0, again.Added.GetValueOrDefault("site profiles"));
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
