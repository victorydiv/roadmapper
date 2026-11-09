using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Application health scoring (no database).</summary>
public class PortfolioHealthTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static Application App(Criticality tier = Criticality.Tier2, int? business = null, int? technical = null,
        LifecycleStatus status = LifecycleStatus.Active, Contract? contract = null) =>
        new() { Id = 1, Name = "WMS", Criticality = tier, BusinessFit = business, TechnicalFit = technical, Status = status, Contract = contract };

    private static Integration Flow(int id, (int Likelihood, int Impact)? risk = null, DateOnly? reviewed = null, LifecycleStatus status = LifecycleStatus.Active)
    {
        var i = new Integration { Id = id, Name = $"Flow {id}", SourceApplicationId = 1, TargetApplicationId = 2, Status = status,
            LastReviewDate = reviewed ?? Today, ReviewIntervalMonths = 12 };
        if (risk is { } r) i.Risks.Add(new IntegrationRisk { Likelihood = r.Likelihood, Impact = r.Impact });
        return i;
    }

    [Fact]
    public void A_healthy_application_scores_100_and_retired_ones_arent_rated()
    {
        var h = PortfolioHealth.Assess(App(business: 4, technical: 4), [Flow(1)], Today, 120)!;
        Assert.Equal(100, h.Score);
        Assert.Equal(HealthRating.Green, h.Rating);
        Assert.Empty(h.Factors);
        Assert.Null(PortfolioHealth.Assess(App(status: LifecycleStatus.Retired), [], Today, 120));
    }

    [Fact]
    public void Each_problem_takes_points_off()
    {
        var contract = new Contract { Name = "WMS SaaS", RenewalDate = Today.AddDays(40), NoticePeriodDays = 30 }; // notice in 10 days
        var app = App(business: 4, technical: 2, status: LifecycleStatus.Retiring, contract: contract); // Migrate
        var flows = new[] { Flow(1, risk: (3, 3)), Flow(2, reviewed: Today.AddMonths(-13)), Flow(3, reviewed: Today.AddMonths(-20)) }; // 3 × 3 = 9: medium

        var h = PortfolioHealth.Assess(app, flows, Today, 120)!;
        Assert.Equal(["Medium-risk integration: Flow 1", "2 integration reviews overdue", "Notice deadline for WMS SaaS is coming up",
            "Retiring but still in 3 live integrations", "TIME: Migrate (technically weak)"], h.Factors.Select(f => f.Reason));
        Assert.Equal(100 - 10 - 10 - 10 - 15 - 15, h.Score); // 40
        Assert.Equal(HealthRating.Red, h.Rating);
    }

    [Theory]
    [InlineData(Criticality.Tier1, 62)] // 25 × 1.5 = 37.5 → 38
    [InlineData(Criticality.Tier2, 75)]
    [InlineData(Criticality.Tier3, 81)] // 25 × 0.75 = 18.75 → 19
    public void Tier_scales_the_penalties(Criticality tier, int expected)
    {
        var h = PortfolioHealth.Assess(App(tier), [Flow(1, risk: (3, 5))], Today, 120)!;
        Assert.Equal(expected, h.Score);
    }

    [Fact]
    public void A_missed_notice_deadline_and_retired_flows()
    {
        var contract = new Contract { Name = "ERP", RenewalDate = Today.AddDays(10), NoticePeriodDays = 60 };
        var h = PortfolioHealth.Assess(App(contract: contract), [Flow(1, risk: (4, 5), status: LifecycleStatus.Retired)], Today, 120)!;
        Assert.Equal(["Notice deadline for ERP has passed"], h.Factors.Select(f => f.Reason)); // the retired high-risk flow doesn't count
        Assert.Equal(80, h.Score);
        Assert.Equal(HealthRating.Green, h.Rating);
    }

    [Theory]
    [InlineData(80, HealthRating.Green)]
    [InlineData(79, HealthRating.Amber)]
    [InlineData(60, HealthRating.Amber)]
    [InlineData(59, HealthRating.Red)]
    public void Ratings_band_the_score(int score, HealthRating rating) => Assert.Equal(rating, PortfolioHealth.Rate(score));
}

/// <summary>Decisions, the dashboard and the portfolio workbook against a real in-memory SQLite database.</summary>
public sealed class DecisionAndDashboardTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;
    private readonly RoadmapService _roadmaps;

    public DecisionAndDashboardTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        _svc = new PortfolioService(_factory, clock);
        _roadmaps = new RoadmapService(_factory, clock);
    }

    public void Dispose() => _connection.Dispose();

    private Task<int> Decide(string title, DecisionStatus status = DecisionStatus.Decided, int? supersededBy = null, int id = 0) =>
        _svc.SaveDecisionAsync(new DecisionDraft
        {
            Id = id, Title = title, DecidedOn = _svc.Today, Status = status, DecisionText = "We will.", SupersededById = supersededBy,
        });

    [Fact]
    public async Task Decisions_save_with_alternatives_and_links_and_can_be_found_by_what_they_link()
    {
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS" });
        var other = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "TMS" });
        var target = new DecisionTarget(DecisionTargetKind.Application, app);
        var id = await _svc.SaveDecisionAsync(new DecisionDraft
        {
            Title = "Upgrade the WMS", DecidedOn = _svc.Today, DecisionText = "Upgrade in Q2.",
            Alternatives = [new() { Option = "Stay put", Cons = "Out of support" }, new(), new() { Option = "Replace", WhyRejected = "Too risky" }],
        }, linkTo: target);
        await _svc.LinkDecisionAsync(id, target); // twice is fine

        var d = (await _svc.GetDecisionAsync(id))!;
        Assert.Equal(["Stay put", "Replace"], d.Alternatives.OrderBy(a => a.SortOrder).Select(a => a.Option)); // blank row dropped
        var link = Assert.Single(d.Links);
        Assert.Equal("WMS", link.TargetName);
        Assert.Single(await _svc.GetDecisionsAsync(target));
        Assert.Empty(await _svc.GetDecisionsAsync(new DecisionTarget(DecisionTargetKind.Application, other)));

        // Editing replaces the alternatives.
        var draft = DecisionDraft.From(d);
        draft.Alternatives.RemoveAt(0);
        await _svc.SaveDecisionAsync(draft);
        Assert.Equal(["Replace"], (await _svc.GetDecisionAsync(id))!.Alternatives.Select(a => a.Option));
    }

    [Fact]
    public async Task A_link_must_point_at_exactly_one_record()
    {
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS" });
        var vendor = await _svc.SaveVendorAsync(new VendorDraft { Name = "Northwind" });
        var id = await Decide("Something");

        await using var db = _factory.CreateDbContext();
        db.DecisionLinks.Add(new DecisionLink { DecisionId = id, ApplicationId = app, VendorId = vendor });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.DecisionLinks.Add(new DecisionLink { DecisionId = id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Links_go_with_their_target_but_the_decision_stays()
    {
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS" });
        var id = await Decide("Upgrade");
        await _svc.LinkDecisionAsync(id, new DecisionTarget(DecisionTargetKind.Application, app));
        await _svc.DeleteApplicationAsync(app);

        var d = (await _svc.GetDecisionAsync(id))!;
        Assert.Empty(d.Links);
    }

    [Fact]
    public async Task Superseding_needs_the_right_status_and_cant_loop()
    {
        var first = await Decide("Phone tendering");
        var second = await Decide("EDI tendering");

        var wrongStatus = await Assert.ThrowsAsync<InvalidOperationException>(() => Decide("Phone tendering", DecisionStatus.Decided, second, first));
        Assert.Contains("Superseded", wrongStatus.Message);

        await Decide("Phone tendering", DecisionStatus.Superseded, second, first);
        var loop = await Assert.ThrowsAsync<InvalidOperationException>(() => Decide("EDI tendering", DecisionStatus.Superseded, first, second));
        Assert.Contains("loop", loop.Message);

        Assert.Equal("EDI tendering", (await _svc.GetDecisionAsync(first))!.SupersededBy!.Title);
        Assert.Equal([first], (await _svc.GetSupersededByAsync(second)).Select(d => d.Id));

        await _svc.DeleteDecisionAsync(second);
        Assert.Null((await _svc.GetDecisionAsync(first))!.SupersededById);
    }

    [Fact]
    public async Task Decision_changes_and_links_are_in_its_history()
    {
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "WMS" });
        var id = await Decide("Upgrade", DecisionStatus.Proposed);
        await Decide("Upgrade", DecisionStatus.Decided, id: id);
        await _svc.LinkDecisionAsync(id, new DecisionTarget(DecisionTargetKind.Application, app));

        var history = await _svc.GetActivityAsync(PortfolioEntityType.Decision, id);
        Assert.Contains(history, h => h is { Action: AuditAction.Created, Field: null, EntityName: "Upgrade" });
        Assert.Contains(history, h => h is { Field: "Status", OldValue: "Proposed", NewValue: "Decided" });
        Assert.Contains(history, h => h is { Field: "Linked to", NewValue: "Application: WMS" });
    }

    [Fact]
    public void Target_keys_round_trip()
    {
        foreach (var kind in Enum.GetValues<DecisionTargetKind>())
        {
            var t = new DecisionTarget(kind, 42);
            Assert.Equal(t, DecisionTarget.Parse(t.Key));
            Assert.Equal(t, t.ToLink(1).Target);
        }
        Assert.Equal("pr42", new DecisionTarget(DecisionTargetKind.Process, 42).Key);
        Assert.Null(DecisionTarget.Parse("x1"));
    }

    [Fact]
    public async Task The_sample_portfolio_fills_the_dashboard_and_impact_reports_show_decisions()
    {
        await DbInitializer.SeedSampleAsync(_roadmaps);
        await _svc.SeedSampleAsync();

        var d = await _svc.GetDashboardAsync();
        Assert.Equal(7, d.Applications.Count);
        Assert.Equal(1_680_000m, d.AnnualRunCost); // 480 + 310 + 650 + 96 + 120 + 24 thousand; the spreadsheet has no cost
        Assert.Equal(d.Applications.Count, Enum.GetValues<HealthRating>().Sum(d.Count));
        Assert.True(d.Applications[0].Health!.Score <= d.Applications[^1].Health!.Score); // worst first
        Assert.Contains(d.Renewals, r => r.VendorName == "Contoso EDI Services"); // notice in 15 days
        Assert.Contains(d.RiskyIntegrations, r => r.Integration.Name == "Acme ship confirmations (945)" && r.Risk == RiskLevel.High);
        Assert.Contains(d.Initiatives, i => i.Title == "Billing self-service" && i.Benefits == 2 && i.Applications.Contains("Litware ERP"));
        Assert.Equal(4, d.RecentDecisions.Count);
        Assert.Equal(93_280m, d.Benefits.ExpectedUsd);

        var edi = (await _svc.GetApplicationsAsync()).Single(a => a.Name == "Contoso EDI");
        var impact = (await _svc.GetImpactAsync(NodeRef.App(edi.Id)))!;
        Assert.Contains(impact.Decisions, x => x.Title.StartsWith("Keep Contoso")); // linked to the application
        Assert.Contains(impact.Decisions, x => x.Title.StartsWith("Tender loads")); // linked to the 204 flow it carries
        Assert.Contains("Past decisions", ImpactAnalyzer.ToText(impact));
    }

    [Fact]
    public async Task The_portfolio_workbook_has_a_sheet_per_kind_of_record()
    {
        await DbInitializer.SeedSampleAsync(_roadmaps);
        await _svc.SeedSampleAsync();

        using var wb = new XLWorkbook(new MemoryStream(await PortfolioExport.ToXlsxAsync(_svc)));
        Assert.Equal(["Applications", "Vendors", "Contracts", "Trading partners", "Integrations", "Risks", "Processes", "Pain points",
            "KPIs", "Benefits", "Decisions", "Contacts"], wb.Worksheets.Select(w => w.Name));
        var apps = wb.Worksheet("Applications");
        Assert.Equal("Health", apps.Cell(1, 17).GetString());
        Assert.Equal(8, apps.LastRowUsed()!.RowNumber()); // header + 7
        Assert.Equal(5, wb.Worksheet("Decisions").LastRowUsed()!.RowNumber());
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
