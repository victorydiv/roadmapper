using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Trading partners as integration endpoints, and the upgrade that turns old "External party" applications into partners.</summary>
public sealed class TradingPartnerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;

    public TradingPartnerTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        _svc = new PortfolioService(_factory, new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose() => _connection.Dispose();

    private void MigrateToLatest()
    {
        using var db = _factory.CreateDbContext();
        db.Database.Migrate();
    }

    private Task<int> App(string name) => _svc.SaveApplicationAsync(new ApplicationDraft { Name = name });

    private Task<int> Partner(string name, PartnerType type = PartnerType.Customer) =>
        _svc.SavePartnerAsync(new TradingPartnerDraft { Name = name, Type = type, EdiQualifier = "zz", EdiId = name.ToUpperInvariant() });

    private Task<int> Flow(NodeRef source, NodeRef target, string name, int? via = null) =>
        _svc.SaveIntegrationAsync(new IntegrationDraft
        {
            SourceKey = source.Key, TargetKey = target.Key, ViaApplicationId = via, Name = name, Type = IntegrationType.EDI,
            BusinessPurpose = "Moves data", LastReviewDate = _svc.Today,
        });

    [Fact]
    public async Task A_partner_can_be_the_source_or_target_of_an_integration()
    {
        MigrateToLatest();
        var acme = await Partner("Acme");
        var wms = await App("WMS");
        var edi = await App("EDI");
        var orders = await Flow(NodeRef.Partner(acme), NodeRef.App(wms), "Orders", via: edi);
        await Flow(NodeRef.App(wms), NodeRef.Partner(acme), "Ship confirmations", via: edi);

        var i = (await _svc.GetIntegrationAsync(orders))!;
        Assert.Equal("Acme", i.SourceName);
        Assert.Null(i.SourceApplicationId);
        Assert.Equal(NodeRef.Partner(acme), i.SourceNode);
        Assert.True(i.InvolvesPartner);

        Assert.Equal(2, (await _svc.GetIntegrationsForPartnerAsync(acme)).Count);
        var partner = (await _svc.GetPartnerAsync(acme))!;
        Assert.Equal("ZZ:ACME", partner.EdiAddress);
    }

    [Fact]
    public async Task Partner_dependencies_reach_the_applications_its_flows_pass_through()
    {
        MigrateToLatest();
        var acme = await Partner("Acme");
        var wms = await App("WMS");
        var edi = await App("EDI");
        var erp = await App("ERP");
        await Flow(NodeRef.Partner(acme), NodeRef.App(wms), "Orders", via: edi);
        await _svc.SaveIntegrationAsync(new IntegrationDraft { SourceApplicationId = wms, TargetApplicationId = erp, Name = "Billing", BusinessPurpose = "x" });

        var view = (await _svc.GetDependenciesAsync(NodeRef.Partner(acme), 2))!;
        Assert.Equal("Acme", view.Name);
        Assert.Equal(["EDI", "WMS", "ERP"], view.Downstream.Select(r => r.Name));
        Assert.All(view.Downstream, r => Assert.NotNull(r.Application));

        // And from the application's side the partner shows up upstream.
        var fromWms = (await _svc.GetDependenciesAsync(wms, 1))!;
        Assert.Contains(fromWms.Upstream, r => r.Partner?.Name == "Acme");
    }

    [Fact]
    public async Task Partner_to_partner_flows_are_rejected()
    {
        MigrateToLatest();
        var a = await Partner("Acme");
        var b = await Partner("Blue Ridge", PartnerType.Carrier);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Flow(NodeRef.Partner(a), NodeRef.Partner(b), "x"));
        Assert.Contains("applications", ex.Message);
    }

    [Fact]
    public async Task A_partner_with_integrations_cannot_be_deleted_but_one_without_can_and_takes_its_people()
    {
        MigrateToLatest();
        var acme = await Partner("Acme");
        var wms = await App("WMS");
        var flow = await Flow(NodeRef.Partner(acme), NodeRef.App(wms), "Orders");
        var person = await _svc.SaveContactAsync(new ContactDraft { Name = "Sam", TradingPartnerId = acme });
        Assert.Equal("Acme", (await _svc.GetContactsAsync()).Single(c => c.Id == person).CompanyName);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeletePartnerAsync(acme));
        Assert.Contains("Orders", ex.Message);

        await _svc.DeleteIntegrationAsync(flow);
        await _svc.DeletePartnerAsync(acme);
        Assert.Null(await _svc.GetPartnerAsync(acme));
        Assert.DoesNotContain(await _svc.GetContactsAsync(), c => c.Id == person);
    }

    [Fact]
    public async Task Partner_names_are_unique_and_edi_qualifiers_are_two_characters()
    {
        MigrateToLatest();
        await Partner("Acme");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Partner("ACME"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SavePartnerAsync(new TradingPartnerDraft { Name = "Globex", EdiQualifier = "ZZZ", EdiId = "X" }));
        Assert.Equal(["Acme"], (await _svc.GetPartnersAsync()).Select(p => p.Name));
    }

    [Fact]
    public async Task Partner_changes_are_recorded_in_its_history()
    {
        MigrateToLatest();
        var acme = await Partner("Acme");
        var draft = TradingPartnerDraft.From((await _svc.GetPartnerAsync(acme))!);
        draft.Importance = PartnerImportance.Key;
        await _svc.SavePartnerAsync(draft);

        var history = await _svc.GetActivityAsync(PortfolioEntityType.TradingPartner, acme);
        Assert.Contains(history, h => h.Action == Core.AuditAction.Created);
        Assert.Contains(history, h => h.Field == "Importance" && h.NewValue == "Key partner");
    }

    [Fact]
    public async Task Upgrading_turns_external_party_applications_into_partners_and_repoints_their_integrations()
    {
        // Build the database as it was before trading partners, with the old way of modelling a customer.
        await using (var db = _factory.CreateDbContext())
        {
            db.GetService<IMigrator>().Migrate("20261109170032_AddIntegrations");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Contacts" ("Id", "Name", "IsActive", "CreatedAt", "UpdatedAt") VALUES (1, 'Alex', 1, '2026-01-01', '2026-01-01');
                INSERT INTO "Applications" ("Id", "Name", "Description", "Category", "Criticality", "Status", "Hosting", "BusinessOwnerId", "CreatedAt", "UpdatedAt") VALUES
                    (1, 'Northwind WMS', NULL, 'WMS', 'Tier1', 'Active', 'SaaS', NULL, '2026-01-01', '2026-01-01'),
                    (2, 'Acme Retail', 'Big customer', 'ExternalParty', 'Tier1', 'Active', 'SaaS', 1, '2026-01-01', '2026-01-01'),
                    (3, 'Blue Ridge Freight', NULL, 'ExternalParty', 'Tier2', 'Retired', 'SaaS', NULL, '2026-01-01', '2026-01-01'),
                    (4, 'Contoso EDI', NULL, 'EDI', 'Tier1', 'Active', 'SaaS', NULL, '2026-01-01', '2026-01-01');
                INSERT INTO "Integrations" ("Id", "Name", "SourceApplicationId", "TargetApplicationId", "ViaApplicationId", "Type", "Direction", "Frequency",
                    "BusinessPurpose", "Criticality", "Status", "ReviewIntervalMonths", "CreatedAt", "UpdatedAt") VALUES
                    (1, 'Orders', 2, 1, 4, 'EDI', 'OneWay', 'Hourly', 'Orders in', 'Tier1', 'Active', 12, '2026-01-01', '2026-01-01'),
                    (2, 'Tenders', 1, 3, NULL, 'EDI', 'OneWay', 'Hourly', 'Tenders out', 'Tier2', 'Active', 12, '2026-01-01', '2026-01-01');
                """);
        }

        MigrateToLatest();

        Assert.Equal(["Contoso EDI", "Northwind WMS"], (await _svc.GetApplicationsAsync()).Select(a => a.Name));
        var partners = await _svc.GetPartnersAsync();
        var acme = partners.Single(p => p.Name == "Acme Retail");
        Assert.Equal((PartnerType.Customer, PartnerImportance.Key, PartnerStatus.Active), (acme.Type, acme.Importance, acme.Status));
        Assert.Equal("Alex", acme.RelationshipOwner?.Name);
        Assert.Equal("Big customer", acme.Notes);
        var carrier = partners.Single(p => p.Name == "Blue Ridge Freight");
        Assert.Equal((PartnerType.Carrier, PartnerImportance.Standard, PartnerStatus.Inactive), (carrier.Type, carrier.Importance, carrier.Status));

        var orders = (await _svc.GetIntegrationAsync(1))!;
        Assert.Equal((NodeRef.Partner(acme.Id), NodeRef.App(1), 4), (orders.SourceNode, orders.TargetNode, orders.ViaApplicationId!.Value));
        var tenders = (await _svc.GetIntegrationAsync(2))!;
        Assert.Equal(NodeRef.Partner(carrier.Id), tenders.TargetNode);

        Assert.Contains(await _svc.GetActivityAsync(PortfolioEntityType.TradingPartner, acme.Id),
            h => h.NewValue == "Converted from an External party application");
        await using var check = _factory.CreateDbContext();
        Assert.Empty(await check.Database.SqlQueryRaw<string>("SELECT \"table\" AS \"Value\" FROM pragma_foreign_key_check").ToListAsync());
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
