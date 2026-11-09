using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Realization, dollar values and at-risk rules for benefits (no database).</summary>
public class BenefitMathTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static Benefit B(BenefitUnit unit, decimal? expected, decimal? baseline = null, DateOnly? by = null, params decimal[] readings)
    {
        var b = new Benefit { Unit = unit, ExpectedValue = expected, Baseline = baseline, ExpectedBy = by };
        for (var n = 0; n < readings.Length; n++)
            b.Measurements.Add(new BenefitMeasurement { Id = n + 1, MeasuredOn = Today.AddMonths(n - readings.Length), Value = readings[n] });
        return b;
    }

    [Fact]
    public void Actual_is_the_latest_measurement_and_savings_count_from_zero()
    {
        var b = B(BenefitUnit.USD, 60_000, readings: [9_000, 14_000]);
        Assert.Equal(14_000, BenefitMath.Actual(b));
        Assert.Equal(Today.AddMonths(-1), BenefitMath.ActualOn(b));
        Assert.Equal(23, BenefitMath.RealizationPct(b));
    }

    [Fact]
    public void A_baseline_measures_the_move_toward_the_expected_level()
    {
        var adoption = B(BenefitUnit.Percent, 60, baseline: 20, readings: [34]);
        Assert.Equal(35, BenefitMath.RealizationPct(adoption)); // (34 − 20) / (60 − 20)

        var errors = B(BenefitUnit.Percent, 2, baseline: 10, readings: [6]); // lower is better
        Assert.Equal(50, BenefitMath.RealizationPct(errors));
        var worse = B(BenefitUnit.Percent, 2, baseline: 10, readings: [12]);
        Assert.Equal(-25, BenefitMath.RealizationPct(worse));
    }

    [Fact]
    public void A_linked_kpi_stands_in_until_there_are_measurements()
    {
        var b = B(BenefitUnit.Percent, 98);
        b.Kpi = new ProcessKpi { Baseline = 92, Target = 98, Current = 95, MeasuredOn = Today.AddDays(-7) };
        Assert.Equal(95, BenefitMath.Actual(b));
        Assert.Equal(92, BenefitMath.Baseline(b));
        Assert.Equal(50, BenefitMath.RealizationPct(b));

        b.Measurements.Add(new BenefitMeasurement { MeasuredOn = Today, Value = 98 });
        Assert.Equal(100, BenefitMath.RealizationPct(b));
    }

    [Fact]
    public void Labor_hours_are_valued_at_the_labor_rate()
    {
        var hours = B(BenefitUnit.Hours, 1_040, readings: [520]);
        Assert.Equal(33_280m, BenefitMath.ExpectedUsd(hours, 32m));
        Assert.Equal(16_640m, BenefitMath.RealizedUsd(hours, 32m));
        Assert.Null(BenefitMath.ExpectedUsd(hours, null));
        Assert.Null(BenefitMath.ExpectedUsd(B(BenefitUnit.Percent, 60, 20), 32m)); // levels have no dollar value
    }

    [Fact]
    public void Totals_add_dollars_and_count_hours_without_a_rate_separately()
    {
        var list = new[]
        {
            B(BenefitUnit.USD, 60_000, readings: [15_000]),
            B(BenefitUnit.Hours, 1_000, readings: [500]),
            B(BenefitUnit.Percent, 60, baseline: 20),
        };
        var withRate = BenefitMath.Totals(list, 30m, Today);
        Assert.Equal(90_000m, withRate.ExpectedUsd);
        Assert.Equal(30_000m, withRate.RealizedUsd);
        Assert.Equal(33, withRate.RealizationPct);
        Assert.Equal(1, withRate.NotMeasured);

        var noRate = BenefitMath.Totals(list, null, Today);
        Assert.Equal(60_000m, noRate.ExpectedUsd);
        Assert.Equal(1, noRate.UnvaluedHours);
    }

    [Fact]
    public void A_realized_setback_counts_as_zero_dollars_not_negative()
    {
        var b = B(BenefitUnit.USD, 1_000, baseline: 5_000, readings: [6_000]); // cost went up instead of down
        Assert.Equal(0m, BenefitMath.RealizedUsd(b, null));
    }

    [Theory]
    [InlineData(null, 10, BenefitStatus.NotMeasured)]   // nothing measured, not due
    [InlineData(null, -10, BenefitStatus.AtRisk)]       // nothing measured, past due
    [InlineData(400, 10, BenefitStatus.Realizing)]      // 40%, not due
    [InlineData(400, -10, BenefitStatus.AtRisk)]        // 40%, past due
    [InlineData(600, -10, BenefitStatus.Realizing)]     // 60%, past due: behind but not at risk
    [InlineData(1000, 10, BenefitStatus.Realized)]
    public void Status_flags_past_due_benefits_under_half_realized(int? actual, int dueInDays, BenefitStatus expected)
    {
        var b = actual is { } a
            ? B(BenefitUnit.USD, 1_000, by: Today.AddDays(dueInDays), readings: [a])
            : B(BenefitUnit.USD, 1_000, by: Today.AddDays(dueInDays));
        Assert.Equal(expected, BenefitMath.Status(b, Today));
    }

    [Fact]
    public void Drafts_reject_bad_input()
    {
        Assert.NotNull(new BenefitDraft { Description = "x" }.Validate()); // no roadmap item
        Assert.NotNull(new BenefitDraft { RoadmapItemId = 1 }.Validate()); // no description
        Assert.NotNull(new BenefitDraft { RoadmapItemId = 1, Description = "x", Baseline = 5, ExpectedValue = 5 }.Validate());
        Assert.NotNull(new BenefitDraft { RoadmapItemId = 1, Description = "x", ExpectedValue = -5 }.Validate());
        Assert.Null(new BenefitDraft { RoadmapItemId = 1, Description = "x", Baseline = 10, ExpectedValue = 2 }.Validate());
    }
}

/// <summary>Benefits and measurements against a real in-memory SQLite database built from the migrations.</summary>
public sealed class BenefitServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;
    private readonly RoadmapService _roadmaps;

    public BenefitServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        _svc = new PortfolioService(_factory, clock);
        _roadmaps = new RoadmapService(_factory, clock);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<(int Product, int Item)> InitiativeAsync(string product = "Billing")
    {
        var p = await _roadmaps.CreateProductAsync(product, null, ScoringModel.Wsjf);
        var goal = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = p.Id, ItemType = ItemType.Goal, Title = "Goal" });
        var item = await _roadmaps.SaveItemAsync(new ItemDraft { ProductId = p.Id, ItemType = ItemType.Initiative, ParentId = goal, Title = "Automate billing" });
        return (p.Id, item);
    }

    [Fact]
    public async Task Benefits_save_with_measurements_and_one_reading_per_day()
    {
        var (product, item) = await InitiativeAsync();
        var id = await _svc.SaveBenefitAsync(new BenefitDraft
        {
            RoadmapItemId = item, Category = BenefitCategory.LaborSavings, Unit = BenefitUnit.Hours, Description = "Hours saved", ExpectedValue = 1_040,
        });
        await _svc.SaveMeasurementAsync(id, _svc.Today.AddMonths(-1), 300, "Finance");
        await _svc.SaveMeasurementAsync(id, _svc.Today, 400);
        await _svc.SaveMeasurementAsync(id, _svc.Today, 520, "Finance close"); // replaces today's

        var b = (await _svc.GetBenefitAsync(id))!;
        Assert.Equal(2, b.Measurements.Count);
        Assert.Equal(520, BenefitMath.Actual(b));
        Assert.Equal("Automate billing", b.RoadmapItem!.Title);
        Assert.Equal("Billing", b.RoadmapItem.Product!.Name);
        Assert.Single(await _svc.GetBenefitsAsync(productId: product));
        Assert.Single(await _svc.GetBenefitsAsync(itemId: item));
        Assert.Empty(await _svc.GetBenefitsAsync(productId: product + 1));

        var future = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveMeasurementAsync(id, _svc.Today.AddDays(1), 1));
        Assert.Contains("future", future.Message);
    }

    [Fact]
    public async Task Benefits_go_with_their_roadmap_item_and_product()
    {
        var (product, item) = await InitiativeAsync();
        var id = await _svc.SaveBenefitAsync(new BenefitDraft { RoadmapItemId = item, Description = "Savings", ExpectedValue = 10 });
        await _svc.SaveMeasurementAsync(id, _svc.Today, 5);
        Assert.Equal(1, await _svc.CountBenefitsForProductAsync(product));

        await _roadmaps.DeleteProductAsync(product);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Benefits);
        Assert.Empty(db.BenefitMeasurements);
    }

    [Fact]
    public async Task Deleting_the_application_or_kpi_just_unlinks_them()
    {
        var (_, item) = await InitiativeAsync();
        var app = await _svc.SaveApplicationAsync(new ApplicationDraft { Name = "ERP" });
        var process = await _svc.SaveProcessAsync(new ProcessDraft { Name = "Invoicing" });
        var kpi = await _svc.SaveKpiAsync(new KpiDraft { ProcessId = process, Name = "Days to invoice" });
        var id = await _svc.SaveBenefitAsync(new BenefitDraft { RoadmapItemId = item, Description = "Faster", ApplicationId = app, KpiId = kpi });
        Assert.Equal("Days to invoice", (await _svc.GetKpiOptionsAsync()).Single().Name);

        await _svc.DeleteApplicationAsync(app);
        await _svc.DeleteProcessAsync(process);
        var b = (await _svc.GetBenefitAsync(id))!;
        Assert.Null(b.ApplicationId);
        Assert.Null(b.KpiId);
        Assert.Single(await _svc.GetBenefitsAsync()); // still there
    }

    [Fact]
    public async Task The_labor_rate_is_a_portfolio_setting()
    {
        await _svc.SetLaborRateAsync(32.5m);
        Assert.Equal(32.5m, (await _svc.GetSettingsAsync()).LaborRate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SetLaborRateAsync(0));
        await _svc.SetLaborRateAsync(null);
        Assert.Null((await _svc.GetSettingsAsync()).LaborRate);
    }

    [Fact]
    public async Task Changes_are_recorded_in_the_benefit_history()
    {
        var (_, item) = await InitiativeAsync();
        var id = await _svc.SaveBenefitAsync(new BenefitDraft { RoadmapItemId = item, Description = "Savings", ExpectedValue = 10 });
        await _svc.SaveBenefitAsync(new BenefitDraft { Id = id, RoadmapItemId = item, Description = "Savings", ExpectedValue = 20 });
        await _svc.SaveMeasurementAsync(id, new DateOnly(2026, 10, 1), 5);

        var history = await _svc.GetActivityAsync(PortfolioEntityType.Benefit, id);
        Assert.Contains(history, h => h is { Action: AuditAction.Created, Field: null, EntityName: "Savings" });
        Assert.Contains(history, h => h is { Field: "Expected value", OldValue: "10", NewValue: "20" });
        Assert.Contains(history, h => h is { Field: "Measurement", NewValue: "2026-10-01: 5" });
    }

    [Fact]
    public async Task The_sample_portfolio_has_benefits_including_one_at_risk()
    {
        await DbInitializer.SeedSampleAsync(_roadmaps);
        await _svc.SeedSampleAsync();

        var benefits = await _svc.GetBenefitsAsync();
        Assert.Equal(4, benefits.Count);
        var rate = (await _svc.GetSettingsAsync()).LaborRate;
        Assert.Equal(32m, rate);
        Assert.Contains(benefits, b => BenefitMath.Status(b, _svc.Today) == BenefitStatus.AtRisk);
        var kpiLinked = Assert.Single(benefits, b => b.KpiId is not null);
        Assert.Equal(50, BenefitMath.RealizationPct(kpiLinked));
        Assert.False(await _svc.CanSeedSampleBenefitsAsync());
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
