using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Data;

namespace Roadmapper.Tests;

/// <summary>The WSJF scoring rubric: the calculator on its own, then saving, recalculating and .rmp files.</summary>
public sealed class ScoringRubricTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly RoadmapService _svc;

    public ScoringRubricTests()
    {
        _connection.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        _svc = new RoadmapService(factory, _clock);
    }

    public void Dispose() => _connection.Dispose();

    // ---- The rubric Copilot used for the site-upgrade plan ----

    private static int _nextId = 1;

    private static ScoringCriterion Bands(WsjfFactor factor, string name, CriterionRole role, params (double From, double Value)[] bands) => new()
    {
        Id = _nextId++, Factor = factor, Role = role, Kind = CriterionKind.NumberBands, Name = name,
        Options = bands.Select((b, i) => new ScoringCriterionOption { Id = _nextId++, SortOrder = i, From = b.From, Value = b.Value }).ToList(),
    };

    private static ScoringCriterion Choice(WsjfFactor factor, string name, params (string Label, double Value)[] choices) => new()
    {
        Id = _nextId++, Factor = factor, Role = CriterionRole.Base, Kind = CriterionKind.Choice, Name = name,
        Options = choices.Select((c, i) => new ScoringCriterionOption { Id = _nextId++, SortOrder = i, Label = c.Label, Value = c.Value }).ToList(),
    };

    private sealed record SiteRubric(ScoringRubric Rubric, ScoringCriterion OrderLines, ScoringCriterion Start, ScoringCriterion Version,
        ScoringCriterion Integrations, ScoringCriterion Users);

    private static SiteRubric CopilotRubric()
    {
        var orderLines = Bands(WsjfFactor.BusinessValue, "Order lines", CriterionRole.Base, (0, 1), (1000, 3), (5000, 5), (15000, 8), (30000, 13));
        var start = Bands(WsjfFactor.TimeCriticality, "Start", CriterionRole.Base, (0, 13), (4, 8), (10, 5), (16, 3));
        start.Kind = CriterionKind.StartDate;
        var version = Choice(WsjfFactor.RiskReduction, "Version", ("4.2", 8), ("6.0.8", 5));
        var integrations = Bands(WsjfFactor.JobSize, "Integrations", CriterionRole.Base, (0, 2), (2, 3), (3, 5), (4, 8));
        var users = Bands(WsjfFactor.JobSize, "Named users", CriterionRole.Adjuster, (0, 0), (61, 1));
        return new(new ScoringRubric(RubricScale.Default, [orderLines, start, version, integrations, users]),
            orderLines, start, version, integrations, users);
    }

    private static double? Factor(IReadOnlyList<FactorResult> results, WsjfFactor f) => results.Single(r => r.Factor == f).Value;

    // ---- Calculator ----

    [Fact]
    public void Copilots_Chervon_NA_example_scores_3_6()
    {
        var r = CopilotRubric();
        var answers = new Dictionary<int, RubricAnswer>
        {
            [r.OrderLines.Id] = new(25065, null),
            [r.Version.Id] = new(null, r.Version.Options.Single(o => o.Label == "4.2").Id),
            [r.Integrations.Id] = new(3, null),
            [r.Users.Id] = new(116, null),
        };

        var results = RubricCalculator.Evaluate(r.Rubric, answers, new DateOnly(2026, 8, 1), Today);

        Assert.Equal(8, Factor(results, WsjfFactor.BusinessValue));
        Assert.Equal(13, Factor(results, WsjfFactor.TimeCriticality));
        Assert.Equal(8, Factor(results, WsjfFactor.RiskReduction));
        Assert.Equal(8, Factor(results, WsjfFactor.JobSize)); // 3 integrations = 5, one step up for 116 users
        var score = new ItemScore();
        RubricCalculator.Apply(score, results);
        Assert.Equal(3.625, new WsjfStrategy().Compute(score));
        Assert.Equal("Integrations 3 → 5 · Named users 116 → +1 step · = 8", results.Single(x => x.Factor == WsjfFactor.JobSize).Explanation);
    }

    [Theory]
    [InlineData(15000, 8)]   // exactly on a band's lower bound
    [InlineData(14999, 5)]
    [InlineData(999999, 13)] // above the top band
    [InlineData(-5, 1)]      // below the lowest band: the lowest band
    public void Number_bands_apply_from_their_lower_bound_upwards(double orderLines, double expected)
    {
        var r = CopilotRubric();
        var result = RubricCalculator.EvaluateFactor(r.Rubric, WsjfFactor.BusinessValue,
            new Dictionary<int, RubricAnswer> { [r.OrderLines.Id] = new(orderLines, null) }, null, Today);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("2026-01-15", 13)] // started months ago
    [InlineData("2026-11-30", 13)] // this month
    [InlineData("2027-02-01", 13)] // 3 months away
    [InlineData("2027-03-01", 8)]  // 4 months away
    [InlineData("2027-08-31", 8)]  // 9 months away
    [InlineData("2027-09-01", 5)]  // 10 months away
    [InlineData("2028-03-01", 3)]  // 16 months away
    public void Start_date_criteria_count_whole_months_from_this_month(string start, double expected)
    {
        var r = CopilotRubric();
        var result = RubricCalculator.EvaluateFactor(r.Rubric, WsjfFactor.TimeCriticality, new Dictionary<int, RubricAnswer>(), DateOnly.Parse(start), Today);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Without_a_start_date_the_start_criterion_is_unanswered()
    {
        var r = CopilotRubric();
        var result = RubricCalculator.EvaluateFactor(r.Rubric, WsjfFactor.TimeCriticality, new Dictionary<int, RubricAnswer>(), null, Today);
        Assert.Null(result.Value);
        Assert.False(result.Criteria.Single().Answered);
    }

    [Theory]
    [InlineData(8, 13, 13)] // average 10.5 is halfway: goes up
    [InlineData(3, 5, 5)]   // 4 is halfway: goes up
    [InlineData(2, 3, 3)]
    [InlineData(1, 5, 3)]   // 3 is on the scale
    [InlineData(5, 5, 5)]
    public void Several_base_criteria_average_and_snap_to_the_scale(double a, double b, double expected)
    {
        var first = Choice(WsjfFactor.BusinessValue, "Revenue", ("x", a));
        var second = Choice(WsjfFactor.BusinessValue, "Strategic", ("y", b));
        var rubric = new ScoringRubric(RubricScale.Default, [first, second]);
        var answers = new Dictionary<int, RubricAnswer>
        {
            [first.Id] = new(null, first.Options[0].Id),
            [second.Id] = new(null, second.Options[0].Id),
        };
        Assert.Equal(expected, RubricCalculator.EvaluateFactor(rubric, WsjfFactor.BusinessValue, answers, null, Today).Value);
    }

    [Theory]
    [InlineData(20, 1, 20)] // can't go above the top of the scale
    [InlineData(1, -1, 1)]  // or below the bottom
    [InlineData(5, -2, 2)]
    [InlineData(5, 2, 13)]
    public void Adjusters_move_along_the_scale_and_stop_at_the_ends(double baseValue, double steps, double expected)
    {
        var size = Choice(WsjfFactor.JobSize, "Size", ("s", baseValue));
        var adjust = Choice(WsjfFactor.JobSize, "Adjust", ("a", steps));
        adjust.Role = CriterionRole.Adjuster;
        var rubric = new ScoringRubric(RubricScale.Default, [size, adjust]);
        var answers = new Dictionary<int, RubricAnswer>
        {
            [size.Id] = new(null, size.Options[0].Id),
            [adjust.Id] = new(null, adjust.Options[0].Id),
        };
        Assert.Equal(expected, RubricCalculator.EvaluateFactor(rubric, WsjfFactor.JobSize, answers, null, Today).Value);
    }

    [Fact]
    public void A_factor_with_only_an_adjuster_answered_is_not_calculated_and_keeps_its_value()
    {
        var r = CopilotRubric();
        var results = RubricCalculator.Evaluate(r.Rubric, new Dictionary<int, RubricAnswer> { [r.Users.Id] = new(200, null) }, null, Today);
        Assert.Null(Factor(results, WsjfFactor.JobSize));

        var score = new ItemScore { JobSize = 5 };
        Assert.False(RubricCalculator.Apply(score, results));
        Assert.Equal(5, score.JobSize);
    }

    [Fact]
    public void Factors_entered_by_hand_are_never_overwritten()
    {
        var r = CopilotRubric();
        var results = RubricCalculator.Evaluate(r.Rubric, new Dictionary<int, RubricAnswer> { [r.OrderLines.Id] = new(50000, null) }, new DateOnly(2026, 12, 1), Today);
        var score = new ItemScore { BusinessValue = 2, TimeCriticality = 1, ManualFactors = WsjfFactors.BusinessValue };

        RubricCalculator.Apply(score, results);

        Assert.Equal(2, score.BusinessValue);       // by hand: kept
        Assert.Equal(13, score.TimeCriticality);    // calculated
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("1, 2, 3", true)]
    [InlineData("0.5;1;2", true)]
    [InlineData("1", false)]
    [InlineData("3, 2, 1", false)]
    [InlineData("1, 1, 2", false)]
    [InlineData("0, 1, 2", false)]
    [InlineData("1, two, 3", false)]
    public void The_scale_must_be_increasing_positive_numbers(string text, bool ok)
    {
        Assert.Equal(ok, RubricScale.TryParse(text, out _, out var error));
        Assert.Equal(ok, error is null);
    }

    [Fact]
    public void Rules_catch_scores_off_the_scale_and_bad_bands()
    {
        var scale = RubricScale.Default;
        Assert.Null(RubricRules.Validate(CopilotRubric().OrderLines, scale));

        var offScale = Choice(WsjfFactor.BusinessValue, "Value", ("High", 4));
        Assert.Contains("isn't on the scale", RubricRules.Validate(offScale, scale));

        var halfStep = Choice(WsjfFactor.JobSize, "Adjust", ("Big", 1.5));
        halfStep.Role = CriterionRole.Adjuster;
        Assert.Contains("whole number of steps", RubricRules.Validate(halfStep, scale));

        var twoAtZero = Bands(WsjfFactor.BusinessValue, "Lines", CriterionRole.Base, (0, 1), (0, 3));
        Assert.Contains("two bands starting at 0", RubricRules.Validate(twoAtZero, scale));

        var noLabel = Choice(WsjfFactor.BusinessValue, "Value", ("", 1));
        Assert.Contains("needs a label", RubricRules.Validate(noLabel, scale));
        Assert.Contains("needs a name", RubricRules.Validate(new ScoringCriterion { Options = [new() { Label = "x", Value = 1 }] }, scale));
    }

    // ---- Service ----

    private async Task<int> Add(int productId, ItemType type, int? parent, string title, Action<ItemDraft>? configure = null)
    {
        var d = ItemDraft.New(productId, type, parent);
        d.Title = title;
        configure?.Invoke(d);
        return await _svc.SaveItemAsync(d);
    }

    private async Task<ItemDraft> DraftOf(int productId, int itemId) =>
        ItemDraft.From((await _svc.GetRoadmapAsync(productId))!.Find(itemId)!);

    private async Task<ItemScore> ScoreOf(int productId, int itemId) => (await _svc.GetRoadmapAsync(productId))!.Find(itemId)!.Score!;

    /// <summary>A WSJF product with Copilot's site rubric saved through the service. Returns the product and saved criteria by name.</summary>
    private async Task<(int ProductId, Dictionary<string, ScoringCriterion> Criteria)> SiteProductAsync(ScoringModel model = ScoringModel.Wsjf)
    {
        var pid = (await _svc.CreateProductAsync("Sites", null, model)).Id;
        foreach (var c in new[] { CopilotRubric() }.SelectMany(r => new[] { r.OrderLines, r.Start, r.Version, r.Integrations, r.Users }))
        {
            var draft = RubricRules.Copy(c);
            draft.Id = 0;
            draft.ProductId = pid;
            foreach (var o in draft.Options) o.Id = 0;
            await _svc.SaveCriterionAsync(draft);
        }
        var rubric = await _svc.GetRubricAsync(pid);
        return (pid, rubric.Criteria.ToDictionary(c => c.Name));
    }

    [Fact]
    public async Task Saving_answers_calculates_the_factors_and_records_history()
    {
        var (pid, c) = await SiteProductAsync();
        var site = await Add(pid, ItemType.Goal, null, "Chervon NA", d =>
        {
            d.StartDate = new DateOnly(2026, 8, 1);
            d.Answers[c["Order lines"].Id] = new(25065, null);
            d.Answers[c["Version"].Id] = new(null, c["Version"].Options.Single(o => o.Label == "4.2").Id);
            d.Answers[c["Integrations"].Id] = new(3, null);
            d.Answers[c["Named users"].Id] = new(116, null);
        });

        var score = await ScoreOf(pid, site);
        Assert.Equal((8, 13, 8, 8), (score.BusinessValue, score.TimeCriticality, score.RiskReduction, score.JobSize));
        Assert.Equal(3.625, score.ComputedScore);
        Assert.Equal(WsjfFactors.None, score.ManualFactors);

        // Change one answer: the factor and the history both follow.
        var draft = await DraftOf(pid, site);
        draft.Answers[c["Order lines"].Id] = new(40000, null);
        await _svc.SaveItemAsync(draft);

        Assert.Equal(13, (await ScoreOf(pid, site)).BusinessValue);
        var history = await _svc.GetActivityAsync(pid, site);
        Assert.Contains(history, h => h is { Field: "Rubric: Order lines", OldValue: "25,065", NewValue: "40,000" });
        Assert.Contains(history, h => h is { Field: "Business value", OldValue: "8", NewValue: "13" });
    }

    [Fact]
    public async Task Values_typed_before_a_rubric_exists_are_kept_when_one_is_added()
    {
        var pid = (await _svc.CreateProductAsync("Old", null, ScoringModel.Wsjf)).Id;
        var item = await Add(pid, ItemType.Goal, null, "Typed", d =>
        {
            d.StartDate = new DateOnly(2026, 12, 1);
            d.Score.BusinessValue = 2; d.Score.TimeCriticality = 1; d.Score.RiskReduction = 1; d.Score.JobSize = 2;
        });
        var untouched = await Add(pid, ItemType.Goal, null, "Blank", d => d.StartDate = new DateOnly(2026, 12, 1));
        Assert.Equal(WsjfFactors.BusinessValue | WsjfFactors.TimeCriticality | WsjfFactors.RiskReduction | WsjfFactors.JobSize,
            (await ScoreOf(pid, item)).ManualFactors);

        await _svc.AddStarterRubricAsync(pid); // includes a start-date criterion for Time criticality

        var typed = await ScoreOf(pid, item);
        Assert.Equal((2.0, 1.0, 1.0, 2.0), (typed.BusinessValue!.Value, typed.TimeCriticality!.Value, typed.RiskReduction!.Value, typed.JobSize!.Value));
        Assert.Equal(13, (await ScoreOf(pid, untouched)).TimeCriticality); // nothing typed, so the rubric fills it in
    }

    [Fact]
    public async Task Switching_a_factor_back_to_the_rubric_recalculates_it()
    {
        var pid = (await _svc.CreateProductAsync("Switch", null, ScoringModel.Wsjf)).Id;
        var item = await Add(pid, ItemType.Goal, null, "Item", d => { d.StartDate = new DateOnly(2026, 12, 1); d.Score.TimeCriticality = 1; });
        await _svc.AddStarterRubricAsync(pid);
        Assert.Equal(1, (await ScoreOf(pid, item)).TimeCriticality);

        var draft = await DraftOf(pid, item);
        draft.Score.ManualFactors &= ~WsjfFactors.TimeCriticality;
        await _svc.SaveItemAsync(draft);

        Assert.Equal(13, (await ScoreOf(pid, item)).TimeCriticality);
    }

    [Fact]
    public async Task Start_date_scores_catch_up_with_the_calendar_without_counting_as_edits()
    {
        var pid = (await _svc.CreateProductAsync("Drift", null, ScoringModel.Wsjf)).Id;
        await _svc.AddStarterRubricAsync(pid);
        var item = await Add(pid, ItemType.Goal, null, "Later", d => d.StartDate = new DateOnly(2027, 9, 1)); // 10 months away
        Assert.Equal(5, (await ScoreOf(pid, item)).TimeCriticality);
        var updatedAt = (await _svc.GetRoadmapAsync(pid))!.Find(item)!.UpdatedAt;
        var historyCount = (await _svc.GetActivityAsync(pid, item)).Count;

        _clock.Now = _clock.Now.AddMonths(8); // July 2027: 2 months away
        await _svc.RecalculateAllAsync();

        Assert.Equal(13, (await ScoreOf(pid, item)).TimeCriticality);
        Assert.Equal(updatedAt, (await _svc.GetRoadmapAsync(pid))!.Find(item)!.UpdatedAt);
        Assert.Equal(historyCount, (await _svc.GetActivityAsync(pid, item)).Count);
    }

    [Fact]
    public async Task The_rubric_only_calculates_under_WSJF()
    {
        var (pid, c) = await SiteProductAsync(ScoringModel.Rice);
        var item = await Add(pid, ItemType.Goal, null, "Site", d => d.Answers[c["Order lines"].Id] = new(20000, null));
        Assert.Null((await ScoreOf(pid, item)).BusinessValue);

        var product = (await _svc.GetRoadmapAsync(pid))!.Product;
        await _svc.UpdateProductAsync(pid, product.Name, product.Description, ScoringModel.Wsjf);

        Assert.Equal(8, (await ScoreOf(pid, item)).BusinessValue);
    }

    [Fact]
    public async Task Removing_a_choice_or_changing_the_kind_removes_answers_that_no_longer_fit()
    {
        var (pid, c) = await SiteProductAsync();
        var version = c["Version"];
        var item = await Add(pid, ItemType.Goal, null, "Site", d =>
        {
            d.Answers[version.Id] = new(null, version.Options.Single(o => o.Label == "4.2").Id);
            d.Answers[c["Order lines"].Id] = new(20000, null);
        });

        var edit = RubricRules.Copy(version);
        edit.Options.RemoveAll(o => o.Label == "4.2");
        await _svc.SaveCriterionAsync(edit);
        Assert.DoesNotContain((await DraftOf(pid, item)).Answers.Keys, k => k == version.Id);

        var lines = RubricRules.Copy(c["Order lines"]);
        lines.Kind = CriterionKind.Choice;
        foreach (var o in lines.Options) o.Label = $"From {o.From}";
        await _svc.SaveCriterionAsync(lines);
        Assert.Empty((await DraftOf(pid, item)).Answers);
    }

    [Fact]
    public async Task Deleting_a_criterion_deletes_its_answers()
    {
        var (pid, c) = await SiteProductAsync();
        var item = await Add(pid, ItemType.Goal, null, "Site", d => d.Answers[c["Order lines"].Id] = new(20000, null));

        await _svc.DeleteCriterionAsync(c["Order lines"].Id);

        Assert.Empty((await DraftOf(pid, item)).Answers);
        Assert.Equal(8, (await ScoreOf(pid, item)).BusinessValue); // the last calculated value stays
    }

    [Fact]
    public async Task The_scale_cant_change_under_scores_that_use_it()
    {
        var (pid, _) = await SiteProductAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SetScaleAsync(pid, "1, 2, 4, 8"));
        Assert.Contains("Order lines (3)", ex.Message);

        await _svc.SetScaleAsync(pid, "1, 2, 3, 5, 8, 13, 20, 40");
        Assert.Equal(40, (await _svc.GetRubricAsync(pid)).Scale[^1]);
        await _svc.SetScaleAsync(pid, "");
        Assert.Equal(RubricScale.Default, (await _svc.GetRubricAsync(pid)).Scale);
    }

    [Fact]
    public async Task Spreadsheet_import_marks_WSJF_values_as_typed()
    {
        var pid = (await _svc.CreateProductAsync("Import", null, ScoringModel.Wsjf)).Id;
        await _svc.AddStarterRubricAsync(pid);
        var rows = new List<ImportRow>
        {
            new(2, new Dictionary<string, string>
            {
                [RoadmapColumns.Type] = "Goal", [RoadmapColumns.Title] = "From Excel", [RoadmapColumns.Start] = "2026-12-01",
                [RoadmapColumns.TimeCriticality] = "2", [RoadmapColumns.JobSize] = "3",
            }),
        };
        await _svc.ImportAsync(pid, ImportPlanner.Plan(rows, (await _svc.GetRoadmapAsync(pid))!));

        var item = (await _svc.GetRoadmapAsync(pid))!.Items.Single();
        Assert.Equal(WsjfFactors.TimeCriticality | WsjfFactors.JobSize, item.Score!.ManualFactors);
        Assert.Equal(2, item.Score.TimeCriticality); // not replaced by the start-date criterion
    }

    // ---- .rmp files ----

    [Fact]
    public async Task Rmp_files_carry_the_rubric_answers_and_hand_entered_factors()
    {
        var (pid, c) = await SiteProductAsync();
        await _svc.SetScaleAsync(pid, "1, 2, 3, 5, 8, 13, 20, 40");
        var item = await Add(pid, ItemType.Goal, null, "Chervon NA", d =>
        {
            d.StartDate = new DateOnly(2026, 8, 1);
            d.Answers[c["Order lines"].Id] = new(25065, null);
            d.Answers[c["Version"].Id] = new(null, c["Version"].Options.Single(o => o.Label == "6.0.8").Id);
            d.Answers[c["Integrations"].Id] = new(3, null);
            d.Score.JobSize = 13;
            d.Score.ManualFactors = WsjfFactors.JobSize;
        });

        var file = (await _svc.ExportFileAsync(pid))!;
        var bytes = file.ToBytes();
        var copy = await _svc.ImportFileAsync(RoadmapFile.Read(new MemoryStream(bytes)));

        var roadmap = (await _svc.GetRoadmapAsync(copy.Id))!;
        Assert.Equal(40, roadmap.Rubric.Scale[^1]);
        Assert.Equal(c.Keys.Order(), roadmap.Rubric.Criteria.Select(x => x.Name).Order());
        var imported = roadmap.Items.Single();
        var newVersion = roadmap.Rubric.Criteria.Single(x => x.Name == "Version");
        var answers = RubricCalculator.AnswersOf(imported);
        Assert.Equal("6.0.8", newVersion.Options.Single(o => o.Id == answers[newVersion.Id].OptionId).Label);
        Assert.Equal(WsjfFactors.JobSize, imported.Score!.ManualFactors);
        var original = await ScoreOf(pid, item);
        Assert.Equal((original.BusinessValue, original.TimeCriticality, original.RiskReduction, original.JobSize),
            (imported.Score.BusinessValue, imported.Score.TimeCriticality, imported.Score.RiskReduction, imported.Score.JobSize));
        Assert.Contains("\"manualFactors\": \"JobSize\"", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Older_rmp_files_without_a_rubric_still_open_and_keep_typed_values()
    {
        const string json = """
            { "format": "roadmapper", "version": 1, "product": { "name": "Old file", "scoringModel": "Wsjf" },
              "items": [ { "id": 1, "type": "Goal", "title": "G", "score": { "businessValue": 5, "jobSize": 2 } } ] }
            """;
        var file = RoadmapFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.Empty(file.Rubric);
        Assert.Null(file.Items.Single().ManualFactors);
    }

    [Fact]
    public async Task Older_rmp_items_count_their_WSJF_values_as_typed()
    {
        const string json = """
            { "format": "roadmapper", "version": 1, "product": { "name": "Old file", "scoringModel": "Wsjf" },
              "items": [ { "id": 1, "type": "Goal", "title": "G", "score": { "businessValue": 5, "jobSize": 2 } } ] }
            """;
        var product = await _svc.ImportFileAsync(RoadmapFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        var score = (await _svc.GetRoadmapAsync(product.Id))!.Items.Single().Score!;
        Assert.Equal(WsjfFactors.BusinessValue | WsjfFactors.JobSize, score.ManualFactors);
    }

    [Theory]
    [InlineData("""{ "criterionId": 99, "number": 5 }""", "isn't in the file")]
    [InlineData("""{ "criterionId": 1, "optionId": 99 }""", "isn't one of its choices")]
    [InlineData("""{ "criterionId": 2 }""", "has no number")]
    [InlineData("""{ "criterionId": 3, "number": 1 }""", "worked out from the start date")]
    public void Rmp_rubric_answers_must_match_the_rubric(string answer, string expected)
    {
        var json = $$"""
            { "format": "roadmapper", "version": 1, "product": { "name": "P", "scoringModel": "Wsjf" },
              "rubric": [
                { "id": 1, "factor": "RiskReduction", "role": "Base", "kind": "Choice", "name": "Version",
                  "options": [ { "id": 10, "label": "4.2", "value": 8 } ] },
                { "id": 2, "factor": "BusinessValue", "role": "Base", "kind": "NumberBands", "name": "Lines",
                  "options": [ { "id": 20, "from": 0, "value": 1 } ] },
                { "id": 3, "factor": "TimeCriticality", "role": "Base", "kind": "StartDate", "name": "Start",
                  "options": [ { "id": 30, "from": 0, "value": 13 } ] } ],
              "items": [ { "id": 1, "type": "Goal", "title": "G", "rubricAnswers": [ {{answer}} ] } ] }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => RoadmapFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Rmp_rubric_scores_must_be_on_the_files_scale()
    {
        const string json = """
            { "format": "roadmapper", "version": 1, "product": { "name": "P", "wsjfScale": "1, 2, 4" },
              "rubric": [ { "id": 1, "factor": "BusinessValue", "role": "Base", "kind": "Choice", "name": "Value",
                            "options": [ { "id": 10, "label": "High", "value": 3 } ] } ] }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => RoadmapFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        Assert.Contains("isn't on the scale", ex.Message);
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
