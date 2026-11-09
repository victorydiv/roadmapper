using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Integrations, risks and dependencies against a real in-memory SQLite database built from the migrations.</summary>
public sealed class IntegrationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;

    public IntegrationServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        _svc = new PortfolioService(_factory, new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose() => _connection.Dispose();

    private DateOnly Today => _svc.Today;

    private Task<int> App(string name, AppCategory category = AppCategory.Other) =>
        _svc.SaveApplicationAsync(new ApplicationDraft { Name = name, Category = category });

    private Task<int> Flow(int source, int target, int? via = null, string? name = null, LifecycleStatus status = LifecycleStatus.Active,
        IntegrationType type = IntegrationType.API, FlowDirection direction = FlowDirection.OneWay) =>
        _svc.SaveIntegrationAsync(new IntegrationDraft
        {
            SourceApplicationId = source, TargetApplicationId = target, ViaApplicationId = via, Name = name, Status = status,
            Type = type, Direction = direction, BusinessPurpose = "Moves data", LastReviewDate = Today,
        });

    [Fact]
    public async Task A_blank_name_becomes_source_to_target_and_parts_load()
    {
        var wms = await App("WMS");
        var tms = await App("TMS");
        var id = await Flow(wms, tms);

        var i = (await _svc.GetIntegrationAsync(id))!;
        Assert.Equal("WMS → TMS", i.Name);
        Assert.Equal("WMS", i.Source!.Name);
        Assert.Equal("TMS", i.Target!.Name);
        Assert.Equal(Today.AddMonths(12), i.NextReviewDate);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("via-source")]
    [InlineData("no-purpose")]
    public async Task Invalid_integrations_are_rejected(string kind)
    {
        var a = await App("A");
        var b = await App("B");
        var draft = kind switch
        {
            "same" => new IntegrationDraft { SourceApplicationId = a, TargetApplicationId = a, BusinessPurpose = "x" },
            "via-source" => new IntegrationDraft { SourceApplicationId = a, TargetApplicationId = b, ViaApplicationId = a, BusinessPurpose = "x" },
            _ => new IntegrationDraft { SourceApplicationId = a, TargetApplicationId = b, BusinessPurpose = " " },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveIntegrationAsync(draft));
    }

    [Fact]
    public async Task The_same_flow_twice_is_rejected_but_another_type_is_fine()
    {
        var a = await App("A");
        var b = await App("B");
        await Flow(a, b, name: "Orders");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Flow(a, b, name: "orders"));
        await Flow(a, b, name: "Orders", type: IntegrationType.SFTP);
    }

    [Fact]
    public async Task An_application_in_use_cannot_be_deleted_until_its_integrations_are_gone()
    {
        var edi = await App("EDI");
        var wms = await App("WMS");
        var cust = await App("Customers");
        var id = await Flow(cust, wms, via: edi, name: "940s");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteApplicationAsync(edi));
        Assert.Contains("940s", ex.Message);
        Assert.NotNull(await _svc.GetApplicationAsync(edi));

        await _svc.DeleteIntegrationAsync(id);
        await _svc.DeleteApplicationAsync(edi);
        Assert.Null(await _svc.GetApplicationAsync(edi));
    }

    [Fact]
    public async Task Dependencies_list_upstream_and_downstream_and_skip_retired_flows()
    {
        var cust = await App("Customers");
        var edi = await App("EDI");
        var wms = await App("WMS");
        var tms = await App("TMS");
        var old = await App("Old reporting");
        await Flow(cust, wms, via: edi, name: "940s");
        await Flow(wms, tms, name: "Release");
        await Flow(wms, old, name: "Legacy extract", status: LifecycleStatus.Retired);

        var view = (await _svc.GetDependenciesAsync(wms, 2))!;
        Assert.Equal(["Customers", "EDI"], view.Upstream.Select(r => r.Application.Name).Order());
        Assert.Equal(["TMS"], view.Downstream.Select(r => r.Application.Name));
        Assert.Equal(["940s", "Release"], view.Direct.Select(i => i.Name));

        Assert.Null(await _svc.GetDependenciesAsync(999));
    }

    [Fact]
    public async Task Risks_drive_the_risk_level_and_go_with_their_integration()
    {
        var a = await App("A");
        var b = await App("B");
        var id = await Flow(a, b);
        await _svc.SaveRiskAsync(new RiskDraft { IntegrationId = id, Description = "Silent failures", Likelihood = 3, Impact = 5 });

        var i = (await _svc.GetIntegrationAsync(id))!;
        Assert.Equal(15, PortfolioRules.TopRiskScore(i));
        Assert.Equal(RiskLevel.High, PortfolioRules.RiskLevel(i, Today));

        await _svc.DeleteIntegrationAsync(id);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(0, await db.IntegrationRisks.CountAsync());
    }

    [Fact]
    public async Task Integration_history_names_applications_and_files_risks_under_the_integration()
    {
        var a = await App("WMS");
        var b = await App("TMS");
        var c = await App("ERP");
        var id = await Flow(a, b, name: "Release");
        var draft = IntegrationDraft.From((await _svc.GetIntegrationAsync(id))!);
        draft.TargetApplicationId = c;
        await _svc.SaveIntegrationAsync(draft);
        await _svc.SaveRiskAsync(new RiskDraft { IntegrationId = id, Description = "Timeouts at peak", Likelihood = 2, Impact = 3 });
        await _svc.MarkReviewedAsync(id);

        var history = await _svc.GetActivityAsync(PortfolioEntityType.Integration, id);
        Assert.Contains(history, h => h.Action == AuditAction.Created && h.Field is null);
        Assert.Contains(history, h => h.Field == "Target" && h.OldValue == "TMS" && h.NewValue == "ERP");
        Assert.Contains(history, h => h.Field == "Risk" && h.NewValue!.StartsWith("Timeouts at peak"));
    }

    [Fact]
    public async Task Integration_owners_show_in_a_contacts_roles()
    {
        var person = await _svc.SaveContactAsync(new ContactDraft { Name = "Marcus" });
        var a = await App("A");
        var b = await App("B");
        await _svc.SaveIntegrationAsync(new IntegrationDraft { SourceApplicationId = a, TargetApplicationId = b, BusinessPurpose = "x", OwnerId = person, Name = "Feed" });
        Assert.Contains("Owner of integration Feed", await _svc.GetContactRolesAsync(person));
    }

    [Fact]
    public async Task The_sample_portfolio_includes_integrations_once()
    {
        await _svc.SeedSampleAsync();
        var integrations = await _svc.GetIntegrationsAsync();
        Assert.True(integrations.Count >= 10);
        Assert.Contains(integrations, i => i.ViaApplicationId is not null);
        Assert.Contains(integrations, i => PortfolioRules.RiskLevel(i, Today) == RiskLevel.High);
        Assert.Contains(integrations, i => PortfolioRules.ReviewOverdue(i, Today));
        Assert.False(await _svc.CanSeedSampleIntegrationsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SeedSampleIntegrationsAsync());
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
