using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>The comparison matrix and rating rules (no database).</summary>
public class CapabilityMatrixTests
{
    private static Application App(int id, string name, AppCategory c = AppCategory.YMS, LifecycleStatus s = LifecycleStatus.Active) =>
        new() { Id = id, Name = name, Category = c, Status = s };

    private static CapabilityFeature Feature(int id, string name, int order, AppCategory c = AppCategory.YMS) =>
        new() { Id = id, Name = name, SortOrder = order, Category = c };

    private static FeatureAssessment Rate(int feature, int app, FeatureAvailability? av, FeatureStrength? st = null) =>
        new() { FeatureId = feature, ApplicationId = app, Availability = av, Strength = st };

    [Fact]
    public void Columns_are_the_categorys_apps_then_added_ones_and_rows_follow_the_feature_order()
    {
        var apps = new[]
        {
            App(1, "Opendock"), App(2, "Kaleris", s: LifecycleStatus.Evaluating), App(3, "Blue Yonder WMS", AppCategory.WMS),
            App(4, "Old yard tool", s: LifecycleStatus.Retired), App(5, "Geotab", AppCategory.Other), App(6, "Synapse", AppCategory.WMS),
        };
        var features = new[] { Feature(10, "Yard visibility", 1), Feature(11, "Dock scheduling", 0), Feature(12, "Freight audit", 0, AppCategory.TMS) };

        var m = CapabilityMatrix.Build(AppCategory.YMS, features, apps, addedIds: [3, 5], assessments: []);
        Assert.Equal(["Kaleris", "Opendock", "Blue Yonder WMS", "Geotab"], m.Apps.Select(a => a.Name));
        Assert.Equal([false, false, true, true], m.Apps.Select(a => a.Added));
        Assert.Equal(["Dock scheduling", "Yard visibility"], m.Features.Select(f => f.Name));

        var withRetired = CapabilityMatrix.Build(AppCategory.YMS, features, apps, [3, 5], [], includeRetired: true);
        Assert.Contains(withRetired.Apps, a => a.Name == "Old yard tool");
    }

    [Fact]
    public void Coverage_gaps_overlap_and_strongest()
    {
        var apps = new[] { App(1, "A"), App(2, "B"), App(3, "C") };
        var features = new[] { Feature(10, "Dock scheduling", 0), Feature(11, "Gate security", 1), Feature(12, "RTLS", 2) };
        var ratings = new[]
        {
            Rate(10, 1, FeatureAvailability.Yes, FeatureStrength.High), Rate(10, 2, FeatureAvailability.Yes, FeatureStrength.Medium),
            Rate(10, 3, FeatureAvailability.Partial, FeatureStrength.High),
            Rate(11, 1, FeatureAvailability.Partial, FeatureStrength.Low), Rate(11, 2, FeatureAvailability.No, FeatureStrength.None),
            Rate(12, 1, null, FeatureStrength.Low), // strength only
            Rate(99, 1, FeatureAvailability.Yes), // feature not in the category: ignored
        };

        var m = CapabilityMatrix.Build(AppCategory.YMS, features, apps, [], ratings);

        var dock = m.ByFeature[10];
        Assert.Equal((2, 1, 0, 0), (dock.Yes, dock.Partial, dock.No, dock.Unassessed));
        Assert.True(dock.IsOverlap);
        Assert.False(dock.IsGap);
        Assert.Equal([1, 3], dock.Strongest.Order());

        var gate = m.ByFeature[11];
        Assert.True(gate.IsGap);
        Assert.Equal((0, 1, 1, 1), (gate.Yes, gate.Partial, gate.No, gate.Unassessed));
        Assert.True(m.ByFeature[12].IsGap);
        Assert.Equal(2, m.Gaps);
        Assert.Equal(1, m.Overlaps);

        var a = m.ByApp[1];
        Assert.Equal((1, 1, 0, 1), (a.Yes, a.Partial, a.No, a.Unassessed));
        Assert.Equal(67, a.AssessedPct);
        Assert.Equal(FeatureStrength.Low, m.Cell(12, 1)!.Strength);
        Assert.Null(m.Cell(12, 2));
    }

    [Fact]
    public void Not_having_a_feature_means_no_strength()
    {
        FeatureAvailability? av = FeatureAvailability.No;
        FeatureStrength? st = null;
        string? note = "  ";
        Assert.Null(CapabilityRules.Normalize(ref av, ref st, ref note));
        Assert.Equal(FeatureStrength.None, st);
        Assert.Null(note);

        st = FeatureStrength.High;
        Assert.NotNull(CapabilityRules.Normalize(ref av, ref st, ref note));

        av = FeatureAvailability.Partial;
        st = FeatureStrength.High;
        Assert.Null(CapabilityRules.Normalize(ref av, ref st, ref note));
        Assert.True(CapabilityRules.IsEmpty(null, null, " "));
        Assert.False(CapabilityRules.IsEmpty(null, null, "x"));
    }
}

/// <summary>Features, ratings, added applications, Evaluating status and backups, through the service.</summary>
public sealed class CapabilityServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;

    public CapabilityServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        _svc = new PortfolioService(_factory, new FixedClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose() => _connection.Dispose();

    private Task<int> AppAsync(string name, AppCategory c = AppCategory.YMS, LifecycleStatus s = LifecycleStatus.Active, decimal? cost = null) =>
        _svc.SaveApplicationAsync(new ApplicationDraft { Name = name, Category = c, Status = s, AnnualCost = cost });

    private Task<int> FeatureAsync(string name, AppCategory c = AppCategory.YMS) => _svc.SaveFeatureAsync(new FeatureDraft { Category = c, Name = name });

    [Fact]
    public async Task Features_are_unique_per_category_ordered_and_movable()
    {
        var dock = await FeatureAsync("Dock scheduling");
        var yard = await FeatureAsync("Yard visibility");
        var gate = await FeatureAsync("Gate security");
        await FeatureAsync("Dock scheduling", AppCategory.TMS); // same name, other category: fine
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => FeatureAsync("dock SCHEDULING"));
        Assert.Contains("already has a feature", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => FeatureAsync("  "));

        await _svc.MoveFeatureAsync(gate, -1);
        await _svc.MoveFeatureAsync(dock, -1); // already first: no change
        Assert.Equal(["Dock scheduling", "Gate security", "Yard visibility"], (await _svc.GetCapabilityMatrixAsync(AppCategory.YMS)).Features.Select(f => f.Name));

        await _svc.SaveFeatureAsync(new FeatureDraft { Id = yard, Category = AppCategory.YMS, Name = "Yard visibility & RTLS", Description = "Trailer locations" });
        var renamed = (await _svc.GetCapabilityMatrixAsync(AppCategory.YMS)).Features.Last();
        Assert.Equal(("Yard visibility & RTLS", "Trailer locations"), (renamed.Name, renamed.Description));
    }

    [Fact]
    public async Task Ratings_follow_the_rules_and_only_for_compared_applications()
    {
        var feature = await FeatureAsync("Dock scheduling");
        var opendock = await AppAsync("Opendock");
        var wms = await AppAsync("Blue Yonder WMS", AppCategory.WMS);

        // A WMS isn't in the YMS comparison until it's added.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveAssessmentAsync(feature, wms, FeatureAvailability.Partial, null, null));
        Assert.Contains("isn't in the YMS comparison", ex.Message);
        await _svc.AddComparisonMemberAsync(AppCategory.YMS, wms);
        await _svc.AddComparisonMemberAsync(AppCategory.YMS, wms); // twice is fine
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AddComparisonMemberAsync(AppCategory.YMS, opendock)); // already a YMS app
        await _svc.SaveAssessmentAsync(feature, wms, FeatureAvailability.Partial, FeatureStrength.Medium, "Dock appointments module");

        // No ⇒ None; a higher strength with No is refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveAssessmentAsync(feature, opendock, FeatureAvailability.No, FeatureStrength.High, null));
        await _svc.SaveAssessmentAsync(feature, opendock, FeatureAvailability.No, null, null);
        var m = await _svc.GetCapabilityMatrixAsync(AppCategory.YMS);
        Assert.Equal(FeatureStrength.None, m.Cell(feature, opendock)!.Strength);
        Assert.Equal("Dock appointments module", m.Cell(feature, wms)!.Note);

        // History is filed under the application.
        Assert.Contains(await _svc.GetActivityAsync(PortfolioEntityType.Application, wms), a => a.Field == "Feature “Dock scheduling”" && a.NewValue == "Partial · strength Medium");

        // Clearing everything removes the rating.
        await _svc.SaveAssessmentAsync(feature, opendock, null, null, " ");
        Assert.Null((await _svc.GetCapabilityMatrixAsync(AppCategory.YMS)).Cell(feature, opendock));

        // Removing an added application hides it but keeps its rating for when it comes back.
        await _svc.RemoveComparisonMemberAsync(AppCategory.YMS, wms);
        Assert.DoesNotContain((await _svc.GetCapabilityMatrixAsync(AppCategory.YMS)).Apps, a => a.Id == wms);
        await _svc.AddComparisonMemberAsync(AppCategory.YMS, wms);
        Assert.NotNull((await _svc.GetCapabilityMatrixAsync(AppCategory.YMS)).Cell(feature, wms));

        // Deleting a feature deletes its ratings.
        await _svc.DeleteFeatureAsync(feature);
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.FeatureAssessments);
    }

    [Fact]
    public async Task An_application_page_shows_each_comparison_it_is_in()
    {
        var yms1 = await FeatureAsync("Dock scheduling");
        await FeatureAsync("Yard visibility");
        var wmsFeature = await FeatureAsync("Wave planning", AppCategory.WMS);
        var wms = await AppAsync("Blue Yonder WMS", AppCategory.WMS);
        await _svc.AddComparisonMemberAsync(AppCategory.YMS, wms);
        await _svc.SaveAssessmentAsync(wmsFeature, wms, FeatureAvailability.Yes, FeatureStrength.High, null);
        await _svc.SaveAssessmentAsync(yms1, wms, FeatureAvailability.Partial, null, null);

        var groups = await _svc.GetFeaturesForApplicationAsync(wms);
        Assert.Equal([(AppCategory.WMS, false), (AppCategory.YMS, true)], groups.Select(g => (g.Category, g.Added)));
        Assert.Equal(FeatureAvailability.Partial, groups[1].Features[0].Rating!.Availability);
        Assert.Null(groups[1].Features[1].Rating);

        var overview = await _svc.GetCapabilityOverviewAsync();
        var yms = overview.Single(o => o.Category == AppCategory.YMS);
        Assert.Equal((2, 1, 50, 2), (yms.Features, yms.Applications, yms.AssessedPct, yms.Gaps));
    }

    [Fact]
    public async Task Evaluating_applications_stay_out_of_portfolio_totals()
    {
        await AppAsync("Opendock", cost: 50_000m);
        var kaleris = await AppAsync("Kaleris", s: LifecycleStatus.Evaluating, cost: 200_000m);

        var d = await _svc.GetDashboardAsync();
        Assert.Equal(50_000m, d.AnnualRunCost);
        Assert.DoesNotContain(d.Applications, h => h.Application.Id == kaleris);
        Assert.Contains((await _svc.GetCapabilityMatrixAsync(AppCategory.YMS)).Apps, a => a.Id == kaleris);
        Assert.False(PortfolioRules.CountsInPortfolio(new Application { Status = LifecycleStatus.Evaluating }));
        Assert.True(PortfolioRules.CountsInPortfolio(new Application { Status = LifecycleStatus.Planned }));

        // Integrations can't be "Evaluating".
        Assert.NotNull(new IntegrationDraft { SourceApplicationId = 1, TargetApplicationId = 2, BusinessPurpose = "x", Status = LifecycleStatus.Evaluating }.Validate());
    }

    [Fact]
    public async Task Comparisons_travel_in_the_portfolio_file()
    {
        var dock = await FeatureAsync("Dock scheduling");
        var gate = await FeatureAsync("Gate security");
        var opendock = await AppAsync("Opendock");
        var wms = await AppAsync("Blue Yonder WMS", AppCategory.WMS);
        await _svc.AddComparisonMemberAsync(AppCategory.YMS, wms);
        await _svc.SaveAssessmentAsync(dock, opendock, FeatureAvailability.Yes, FeatureStrength.High, "Core product");
        await _svc.SaveAssessmentAsync(gate, wms, FeatureAvailability.No, null, null);

        using var buffer = new MemoryStream((await _svc.ExportPortfolioAsync()).ToBytes());
        var file = PortfolioFile.Read(buffer);
        Assert.Equal(2, file.Features.Count);
        Assert.Single(file.ComparisonMembers);

        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(other).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var target = new PortfolioService(factory);
        var result = await target.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal((2, 2), (result.Added["features"], result.Added["feature ratings"]));

        var m = await target.GetCapabilityMatrixAsync(AppCategory.YMS);
        Assert.Equal(["Dock scheduling", "Gate security"], m.Features.Select(f => f.Name));
        Assert.Equal(["Opendock", "Blue Yonder WMS"], m.Apps.Select(a => a.Name));
        Assert.Equal("Core product", m.Cell(m.Features[0].Id, m.Apps[0].Id)!.Note);
        Assert.Equal(FeatureStrength.None, m.Cell(m.Features[1].Id, m.Apps[1].Id)!.Strength);

        // Merging again adds nothing.
        var again = await target.ImportPortfolioAsync(file, PortfolioImportMode.Merge);
        Assert.Equal(0, again.Added.GetValueOrDefault("features"));
        Assert.Equal(0, again.Added.GetValueOrDefault("feature ratings"));
        Assert.Equal(2, (await target.GetCapabilityMatrixAsync(AppCategory.YMS)).Features.Count);
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
