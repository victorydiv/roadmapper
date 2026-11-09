using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Business processes, pain points, KPIs and impact analysis against a real in-memory SQLite database built from the migrations.</summary>
public sealed class ProcessServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;
    private readonly RoadmapService _roadmaps;

    public ProcessServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        _svc = new PortfolioService(_factory, clock);
        _roadmaps = new RoadmapService(_factory, clock);
    }

    public void Dispose() => _connection.Dispose();

    private Task<int> App(string name, Criticality tier = Criticality.Tier2) =>
        _svc.SaveApplicationAsync(new ApplicationDraft { Name = name, Criticality = tier });

    private Task<int> Process(string name, int? parent = null) =>
        _svc.SaveProcessAsync(new ProcessDraft { Name = name, ParentProcessId = parent });

    private Task<int> Flow(int source, int target, string name) =>
        _svc.SaveIntegrationAsync(new IntegrationDraft
        {
            SourceApplicationId = source, TargetApplicationId = target, Name = name, BusinessPurpose = "Moves data", LastReviewDate = _svc.Today,
        });

    [Fact]
    public async Task Processes_come_back_in_tree_order()
    {
        var o2c = await Process("Order to Cash");
        await Process("Inbound");
        await Process("Shipping", o2c);
        await Process("Intake", o2c);

        var names = (await _svc.GetProcessesAsync()).Select(p => p.Name).ToList();
        // Siblings keep the order they were added in; top-level processes come before nothing that's under another.
        Assert.Equal(["Order to Cash", "Shipping", "Intake", "Inbound"], names);
    }

    [Fact]
    public async Task Names_are_unique_and_a_process_cant_sit_under_its_own_subprocess()
    {
        var top = await Process("Order to Cash");
        var child = await Process("Shipping", top);
        var grandchild = await Process("Loading", child);

        var dup = await Assert.ThrowsAsync<InvalidOperationException>(() => Process("order to cash"));
        Assert.Contains("already exists", dup.Message);

        var loop = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveProcessAsync(new ProcessDraft { Id = top, Name = "Order to Cash", ParentProcessId = grandchild }));
        Assert.Contains("its own sub-processes", loop.Message);

        var self = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveProcessAsync(new ProcessDraft { Id = child, Name = "Shipping", ParentProcessId = child }));
        Assert.Contains("own parent", self.Message);
    }

    [Fact]
    public async Task A_process_with_subprocesses_cant_be_deleted_and_a_leaf_takes_its_parts_with_it()
    {
        var top = await Process("Order to Cash");
        var leaf = await Process("Shipping", top);
        var wms = await App("WMS");
        var tms = await App("TMS");
        var flow = await Flow(wms, tms, "Order release");
        await _svc.SetProcessApplicationAsync(leaf, wms, "Waves", true);
        await _svc.AddProcessIntegrationAsync(leaf, flow);
        await _svc.SavePainPointAsync(new PainPointDraft { ProcessId = leaf, Description = "Manual waves" });
        await _svc.SaveKpiAsync(new KpiDraft { ProcessId = leaf, Name = "On-time", Baseline = 90, Target = 98, Current = 94 });

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteProcessAsync(top));
        Assert.Contains("Shipping", refused.Message);

        await _svc.DeleteProcessAsync(leaf);
        await _svc.DeleteProcessAsync(top);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.BusinessProcesses);
        Assert.Empty(db.ProcessApplications);
        Assert.Empty(db.ProcessIntegrations);
        Assert.Empty(db.ProcessPainPoints);
        Assert.Empty(db.ProcessKpis);
        Assert.Equal(2, db.Applications.Count()); // the applications themselves stay
    }

    [Fact]
    public async Task Application_links_update_in_place_and_go_when_the_application_or_integration_does()
    {
        var p = await Process("Receiving");
        var wms = await App("WMS");
        var yard = await App("Yard log");
        var flow = await Flow(yard, wms, "Trailer arrivals");
        await _svc.SetProcessApplicationAsync(p, wms, "Receipts", false);
        await _svc.SetProcessApplicationAsync(p, wms, "Receipts and putaway", true);
        await _svc.SetProcessApplicationAsync(p, yard, null, false);
        await _svc.AddProcessIntegrationAsync(p, flow);
        await _svc.AddProcessIntegrationAsync(p, flow); // twice is fine

        var process = (await _svc.GetProcessAsync(p))!;
        var use = Assert.Single(process.Applications, a => a.ApplicationId == wms);
        Assert.Equal("Receipts and putaway", use.Usage);
        Assert.True(use.IsPrimary);
        Assert.Single(process.Integrations);
        Assert.Equal("Yard log", process.Integrations[0].Integration!.SourceName);

        var uses = await _svc.GetProcessesForApplicationAsync(yard);
        Assert.Equal(["Trailer arrivals"], Assert.Single(uses).Integrations.Select(i => i.Name));
        Assert.Single(await _svc.GetProcessesForIntegrationAsync(flow));

        await _svc.DeleteIntegrationAsync(flow);
        await _svc.DeleteApplicationAsync(yard);
        process = (await _svc.GetProcessAsync(p))!;
        Assert.Empty(process.Integrations);
        Assert.Equal([wms], process.Applications.Select(a => a.ApplicationId));
    }

    [Fact]
    public async Task A_pain_point_can_be_addressed_by_a_new_initiative_and_survives_its_deletion()
    {
        var product = await _roadmaps.CreateProductAsync("Billing roadmap", null, ScoringModel.Wsjf);
        var goal = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = product.Id, ItemType = ItemType.Goal, Title = "Faster billing" });
        var p = await Process("Invoicing");
        var erp = await App("ERP");

        // What the pain point editor does for "Create an initiative".
        var draft = PainPointDraft.New(p);
        draft.Description = "Accessorials keyed by hand";
        draft.Severity = PainSeverity.High;
        draft.ApplicationId = erp;
        Assert.Contains(await _svc.GetGoalsAsync(product.Id), g => g.Id == goal);
        var item = ItemDraft.New(product.Id, ItemType.Initiative, goal);
        item.Title = "Automate accessorial billing";
        item.Description = draft.Description;
        draft.AddressedByItemId = await _roadmaps.SaveItemAsync(item);
        var painId = await _svc.SavePainPointAsync(draft);

        var pain = Assert.Single((await _svc.GetProcessAsync(p))!.PainPoints);
        Assert.Equal("Automate accessorial billing", pain.AddressedBy!.Title);
        Assert.Equal("Billing roadmap", pain.AddressedBy.Product!.Name);
        Assert.Equal("ERP", pain.Application!.Name);

        await _roadmaps.DeleteItemAsync(draft.AddressedByItemId.Value);
        pain = Assert.Single((await _svc.GetProcessAsync(p))!.PainPoints);
        Assert.Equal(painId, pain.Id);
        Assert.Null(pain.AddressedByItemId);
    }

    [Theory]
    [InlineData(KpiDirection.HigherIsBetter, 90, 98, 94, 50, false)]
    [InlineData(KpiDirection.HigherIsBetter, 90, 98, 99, 113, true)]
    [InlineData(KpiDirection.LowerIsBetter, 24, 8, 18, 38, false)]
    [InlineData(KpiDirection.LowerIsBetter, 24, 8, 8, 100, true)]
    public void Kpi_progress_and_target_follow_the_direction(KpiDirection direction, double baseline, double target, double current, int pct, bool onTarget)
    {
        var k = new ProcessKpi { Direction = direction, Baseline = (decimal)baseline, Target = (decimal)target, Current = (decimal)current };
        Assert.Equal(pct, k.ProgressPct);
        Assert.Equal(onTarget, k.OnTarget);
    }

    [Fact]
    public async Task Kpis_validate_direction_and_stamp_the_measurement_date()
    {
        var p = await Process("Receiving");
        var wrong = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SaveKpiAsync(new KpiDraft { ProcessId = p, Name = "Dock to stock", Direction = KpiDirection.LowerIsBetter, Baseline = 8, Target = 24 }));
        Assert.Contains("below the baseline", wrong.Message);

        await _svc.SaveKpiAsync(new KpiDraft { ProcessId = p, Name = "Dock to stock", Unit = "hrs", Direction = KpiDirection.LowerIsBetter, Baseline = 24, Target = 8, Current = 18 });
        var kpi = Assert.Single((await _svc.GetProcessAsync(p))!.Kpis);
        Assert.Equal(_svc.Today, kpi.MeasuredOn);
    }

    [Fact]
    public async Task Changes_are_recorded_in_the_process_history()
    {
        var p = await Process("Receiving");
        var wms = await App("WMS");
        await _svc.SaveProcessAsync(new ProcessDraft { Id = p, Name = "Receiving", Domain = ProcessDomain.Inbound });
        await _svc.SetProcessApplicationAsync(p, wms, "Receipts", true);
        await _svc.SavePainPointAsync(new PainPointDraft { ProcessId = p, Description = "Typos in trailer numbers" });
        await _svc.SaveKpiAsync(new KpiDraft { ProcessId = p, Name = "Dock to stock" });

        var history = await _svc.GetActivityAsync(PortfolioEntityType.Process, p);
        Assert.Contains(history, h => h is { Action: AuditAction.Created, Field: null, EntityName: "Receiving" });
        Assert.Contains(history, h => h is { Field: "Domain", OldValue: "Other", NewValue: "Inbound" });
        Assert.Contains(history, h => h is { Field: "Application used", NewValue: "WMS" });
        Assert.Contains(history, h => h is { Field: "Pain point", NewValue: "Typos in trailer numbers (Medium)" });
        Assert.Contains(history, h => h is { Field: "KPI", NewValue: "Dock to stock" });
    }

    [Fact]
    public async Task Process_owners_show_in_contact_roles()
    {
        var who = await _svc.SaveContactAsync(new ContactDraft { Name = "Jordan Ruiz" });
        await _svc.SaveProcessAsync(new ProcessDraft { Name = "Receiving", ProcessOwnerId = who });
        Assert.Contains("Owner of process Receiving", await _svc.GetContactRolesAsync(who));
    }

    [Fact]
    public async Task Impact_reads_processes_initiatives_and_vendors_from_the_database()
    {
        await _svc.SeedSampleAsync();
        var apps = await _svc.GetApplicationsAsync();
        var wms = apps.Single(a => a.Name == "Northwind WMS");

        var r = (await _svc.GetImpactAsync(NodeRef.App(wms.Id), 2))!;
        Assert.Equal(ImpactSeverity.High, r.Severity);
        Assert.Contains(r.Processes, p => p.Process.Name == "Pick, pack and ship" && p.Kind == ProcessImpactKind.UsesApplication);
        Assert.Contains(r.Processes, p => p.Process.Name == "Order to Cash" && p.Kind == ProcessImpactKind.SubProcessAffected);
        Assert.Contains(r.Partners, p => p.Name == "Acme Retail" && p.IsKeyPartner);
        Assert.Equal("Northwind Supply Chain Software", r.Vendors[0].Vendor.Name);
        Assert.NotEmpty(r.Vendors[0].Escalation);
        Assert.Contains(r.OpenPainPoints, x => x.PainPoint.Description.StartsWith("Wave planning"));

        Assert.Null(await _svc.GetImpactAsync(NodeRef.App(9999)));
    }

    [Fact]
    public async Task Sample_processes_can_be_added_to_an_existing_sample_portfolio()
    {
        await _svc.SeedSampleAsync();
        Assert.False(await _svc.CanSeedSampleProcessesAsync()); // already there

        await using (var db = _factory.CreateDbContext())
        {
            db.BusinessProcesses.RemoveRange(db.BusinessProcesses.Where(p => p.ParentProcessId != null));
            await db.SaveChangesAsync();
            db.BusinessProcesses.RemoveRange(db.BusinessProcesses);
            await db.SaveChangesAsync();
        }
        Assert.True(await _svc.CanSeedSampleProcessesAsync());
        await _svc.SeedSampleProcessesAsync();
        var processes = await _svc.GetProcessesAsync();
        Assert.Equal(7, processes.Count);
        Assert.All(processes.Where(p => p.ParentProcessId is not null || p.Name == "Transportation"), p => Assert.NotEmpty(p.Applications));
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
