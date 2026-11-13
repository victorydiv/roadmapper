using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

public class CustomerMatrixTests
{
    private static AppDeployment D(int app, int partner, string name, string? site, DeploymentStatus status = DeploymentStatus.Live) => new()
    {
        Category = AppCategory.YMS, ApplicationId = app, TradingPartnerId = partner, TradingPartner = new TradingPartner { Id = partner, Name = name },
        Site = site, Status = status,
    };

    [Fact]
    public void Sites_are_split_trimmed_and_deduplicated()
    {
        Assert.Equal(["AZ", "CO", "FL"], DeploymentRules.SplitSites(" AZ, CO;FL\naz , "));
        Assert.Equal([null], DeploymentRules.SplitSites("  "));
        Assert.Equal([null], DeploymentRules.SplitSites(null));
    }

    [Fact]
    public void Drafts_need_an_app_and_a_customer_and_edit_one_site_at_a_time()
    {
        Assert.NotNull(new DeploymentDraft { TradingPartnerId = 1 }.Validate());
        Assert.NotNull(new DeploymentDraft { ApplicationId = 1 }.Validate());
        Assert.Null(new DeploymentDraft { ApplicationId = 1, NewCustomerName = "Oatly" }.Validate());
        Assert.Null(new DeploymentDraft { ApplicationId = 1, TradingPartnerId = 1, Sites = "AZ, CO" }.Validate());
        Assert.NotNull(new DeploymentDraft { Id = 5, ApplicationId = 1, TradingPartnerId = 1, Sites = "AZ, CO" }.Validate());
        Assert.NotNull(new DeploymentDraft { ApplicationId = 1, TradingPartnerId = 1, Sites = new string('x', 101) }.Validate());
    }

    [Fact]
    public void Rows_are_customers_then_sites_and_reach_counts_live_sites_and_customers()
    {
        var list = new[]
        {
            D(1, 2, "Oatly", "UT"), D(1, 1, "Interstate Batteries", "TX"), D(1, 1, "Interstate Batteries", "AZ"),
            D(2, 1, "Interstate Batteries", "az"), D(1, 3, "Hisense", null, DeploymentStatus.Pipeline), D(1, 1, "Interstate Batteries", null, DeploymentStatus.Evaluating),
            D(9, 4, "Elsewhere", null), // not a shown application
        };
        var m = CustomerMatrix.Build([1, 2, 3], list);
        Assert.Equal(["Hisense", "Interstate Batteries", "Interstate Batteries · AZ", "Interstate Batteries · TX", "Oatly · UT"],
            m.Rows.Select(r => DeploymentRules.Where(r.Customer, r.Site)));
        // Same site regardless of case: application 2's "az" shares the AZ row.
        Assert.Equal(DeploymentStatus.Live, m.Cell(m.Rows[2], 2)!.Status);
        Assert.Equal(new DeploymentReach(3, 2, 2), m.Reach[1]);
        Assert.Equal(new DeploymentReach(1, 1, 0), m.Reach[2]);
        Assert.True(m.Reach[3].NoneLive);
        Assert.Equal(3, m.Customers);

        var live = CustomerMatrix.Build([1, 2, 3], list, [DeploymentStatus.Live]);
        Assert.Equal(3, live.Rows.Count);
        Assert.Equal(new DeploymentReach(3, 2, 2), live.Reach[1]); // reach still counts everything
    }
}

public sealed class DeploymentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;

    public DeploymentServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        _svc = new PortfolioService(_factory);
    }

    public void Dispose() => _connection.Dispose();

    private Task<int> AppAsync(string name, AppCategory c = AppCategory.YMS) =>
        _svc.SaveApplicationAsync(new ApplicationDraft { Name = name, Category = c, Status = LifecycleStatus.Active });

    [Fact]
    public async Task Several_sites_at_once_a_new_customer_and_no_repeats()
    {
        var opendock = await AppAsync("OpenDock");
        var ids = await _svc.SaveDeploymentAsync(new DeploymentDraft
        {
            Category = AppCategory.YMS, ApplicationId = opendock, NewCustomerName = "Interstate Batteries", Sites = "AZ, CO, FL", Note = "On the October billing",
        });
        Assert.Equal(3, ids.Count);
        var ib = (await _svc.GetPartnersAsync()).Single();
        Assert.Equal(("Interstate Batteries", PartnerType.Customer), (ib.Name, ib.Type));

        // The same name again uses the existing customer; a site it already has is refused.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDeploymentAsync(new DeploymentDraft
        {
            Category = AppCategory.YMS, ApplicationId = opendock, NewCustomerName = "interstate batteries", Sites = "TX, az",
        }));
        Assert.Contains("Interstate Batteries · az", ex.Message);
        Assert.Single(await _svc.GetPartnersAsync());

        var rows = await _svc.GetDeploymentsForPartnerAsync(ib.Id);
        Assert.Equal(["AZ", "CO", "FL"], rows.Select(r => r.Site));
        Assert.All(rows, r => Assert.Equal("On the October billing", r.Note));

        // Editing one: moving it onto a site that's taken is refused; changing status is fine.
        var co = DeploymentDraft.From(rows[1]);
        co.Sites = "FL";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDeploymentAsync(co));
        co.Sites = "CO";
        co.Status = DeploymentStatus.Retired;
        await _svc.SaveDeploymentAsync(co);
        Assert.Equal(DeploymentStatus.Retired, (await _svc.GetDeploymentsAsync(AppCategory.YMS)).Single(d => d.Site == "CO").Status);

        await _svc.DeleteDeploymentAsync(rows[0].Id);
        Assert.Equal(2, (await _svc.GetDeploymentsForApplicationAsync(opendock)).Count);
    }

    [Fact]
    public async Task Only_applications_in_the_comparison_and_deletes_cascade()
    {
        var wms = await AppAsync("Blue Yonder Cloud", AppCategory.WMS);
        var partner = await _svc.SavePartnerAsync(new TradingPartnerDraft { Name = "Bob Evans" });
        var draft = new DeploymentDraft { Category = AppCategory.YMS, ApplicationId = wms, TradingPartnerId = partner, Status = DeploymentStatus.Evaluating };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveDeploymentAsync(draft));

        await _svc.AddComparisonMemberAsync(AppCategory.YMS, wms);
        await _svc.SaveDeploymentAsync(draft);
        // The same application in its own category's comparison is a separate entry.
        await _svc.SaveDeploymentAsync(new DeploymentDraft { Category = AppCategory.WMS, ApplicationId = wms, TradingPartnerId = partner });
        Assert.Equal([AppCategory.YMS], (await _svc.GetDeploymentsAsync(AppCategory.YMS)).Select(d => d.Category));
        Assert.Contains(await _svc.GetDeploymentChoicesAsync(), c => c is { Category: AppCategory.YMS, Added: true } && c.ApplicationId == wms);

        await _svc.DeletePartnerAsync(partner);
        Assert.Empty(await _svc.GetDeploymentsForApplicationAsync(wms));
    }

    [Fact]
    public async Task Deployments_travel_in_the_portfolio_file_and_merge_once()
    {
        var opendock = await AppAsync("OpenDock");
        await _svc.SaveDeploymentAsync(new DeploymentDraft
        {
            Category = AppCategory.YMS, ApplicationId = opendock, NewCustomerName = "Oatly", Sites = "UT", Since = new DateOnly(2025, 3, 1),
        });
        await _svc.SaveDeploymentAsync(new DeploymentDraft
        {
            Category = AppCategory.YMS, ApplicationId = opendock, NewCustomerName = "Hisense", Status = DeploymentStatus.Pipeline,
        });

        using var buffer = new MemoryStream((await _svc.ExportPortfolioAsync()).ToBytes());
        var file = PortfolioFile.Read(buffer);
        Assert.Equal(2, file.Deployments.Count);

        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(other).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var target = new PortfolioService(factory);
        var result = await target.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal(2, result.Added["customer deployments"]);
        var rows = await target.GetDeploymentsAsync(AppCategory.YMS);
        var oatly = rows.Single(d => d.TradingPartner!.Name == "Oatly");
        Assert.Equal(("UT", DeploymentStatus.Live, new DateOnly(2025, 3, 1)), (oatly.Site, oatly.Status, oatly.Since!.Value));
        Assert.Equal(DeploymentStatus.Pipeline, rows.Single(d => d.TradingPartner!.Name == "Hisense").Status);

        var again = await target.ImportPortfolioAsync(file, PortfolioImportMode.Merge);
        Assert.Equal(0, again.Added.GetValueOrDefault("customer deployments"));
        Assert.Equal(2, (await target.GetDeploymentsAsync(AppCategory.YMS)).Count);
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
