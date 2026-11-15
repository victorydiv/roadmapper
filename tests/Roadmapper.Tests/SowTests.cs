using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data;
using Roadmapper.Data.Portfolio;
using Roadmapper.Data.Sows;

namespace Roadmapper.Tests;

public class SowRulesTests
{
    private static SowLineDraft Line(SowCostCategory c, decimal qty, decimal rate, SowFrequency f = SowFrequency.OneTime, string name = "Line") =>
        new() { Category = c, Quantity = qty, Rate = rate, Frequency = f, Description = name };

    [Fact]
    public void Totals_sum_each_category_one_time_and_annual_and_list_every_category()
    {
        var totals = SowRules.Totals(
        [
            Line(SowCostCategory.LaborItPm, 26, 150), Line(SowCostCategory.LaborEdi, 240, 150), Line(SowCostCategory.LaborTesting, 84, 150),
            Line(SowCostCategory.Software, 1, 500, SowFrequency.Annual), Line(SowCostCategory.Software, 1, 2500),
            Line(SowCostCategory.LaborWms, 0.333m, 150), // rounds to the cent
        ]);
        Assert.Equal(10, totals.Rows.Count);
        Assert.Equal(SowCostCategory.ItInfrastructure, totals.Rows[0].Category);
        Assert.Equal(3_900m, totals.Row(SowCostCategory.LaborItPm).OneTime);
        Assert.Equal(36_000m, totals.Row(SowCostCategory.LaborEdi).OneTime);
        Assert.Equal((2_500m, 500m), (totals.Row(SowCostCategory.Software).OneTime, totals.Row(SowCostCategory.Software).Annual));
        Assert.Equal(49.95m, totals.Row(SowCostCategory.LaborWms).OneTime);
        Assert.Equal(3_900m + 36_000m + 12_600m + 2_500m + 49.95m, totals.OneTime);
        Assert.Equal(500m, totals.Annual);
    }

    [Fact]
    public void Description_text_becomes_paragraphs_and_indented_bullets()
    {
        var lines = SowRules.TextLines("Integration of 6 EDI documents.\r\n\r\n Outbound from McCormick to Kenco\n  • EDI 940 – Shipping Order\n\t- EDI 856\n* Testing\n Pasted from Excel\n-");
        Assert.Equal(
        [
            new SowTextLine("Integration of 6 EDI documents.", false, 0),
            new SowTextLine("Outbound from McCormick to Kenco", false, 0),
            new SowTextLine("EDI 940 – Shipping Order", true, 1),
            new SowTextLine("EDI 856", true, 1),
            new SowTextLine("Testing", true, 0),
            new SowTextLine("Pasted from Excel", true, 0),
        ], lines);
        Assert.Empty(SowRules.TextLines(null));
    }

    [Fact]
    public void Drafts_need_a_project_and_customer_and_valid_lines()
    {
        var ok = new SowDraft { ProjectName = "McCormick EDI", CustomerName = "McCormick", PreparedOn = new DateOnly(2026, 3, 12) };
        Assert.Null(ok.Validate());
        Assert.NotNull(new SowDraft { CustomerName = "McCormick" }.Validate());
        Assert.NotNull(new SowDraft { ProjectName = "X" }.Validate());
        ok.ExpiresOn = new DateOnly(2026, 3, 1);
        Assert.NotNull(ok.Validate());
        ok.ExpiresOn = null;
        ok.Lines.Add(Line(SowCostCategory.Travel, -1, 1800));
        Assert.NotNull(ok.Validate());
        ok.Lines[0] = Line(SowCostCategory.Travel, 1, 1800, name: " ");
        Assert.NotNull(ok.Validate());
    }

    [Fact]
    public void New_and_copied_drafts_take_the_settings_defaults_and_a_fresh_expiration()
    {
        var settings = new SowSettings { ExpirationDays = 90, DefaultManager = "Sean Estes" };
        var today = new DateOnly(2026, 3, 12);
        var d = SowDraft.New(settings, today);
        Assert.Equal((today, new DateOnly(2026, 6, 10), "Sean Estes"), (d.PreparedOn, d.ExpiresOn!.Value, d.Manager));
        Assert.Equal(SowRules.DefaultScheduleText, d.Schedule);

        var sow = new Sow
        {
            Id = 4, Number = "953100", ProjectName = "McCormick EDI Implementation", CustomerName = "McCormick", Status = SowStatus.Approved,
            PreparedOn = new DateOnly(2025, 1, 1), Lines = [new SowLine { Category = SowCostCategory.LaborEdi, Description = "Development", Quantity = 240, Rate = 150 }],
        };
        var copy = SowDraft.Copy(sow, settings, today);
        Assert.Equal((0, null, SowStatus.Draft, "McCormick EDI Implementation (copy)"), (copy.Id, copy.Number, copy.Status, copy.ProjectName));
        Assert.Equal((today, today.AddDays(90)), (copy.PreparedOn, copy.ExpiresOn!.Value));
        Assert.Equal(36_000m, copy.Totals.OneTime);

        Assert.True(SowRules.IsExpired(new Sow { Status = SowStatus.Sent, ExpiresOn = today.AddDays(-1) }, today));
        Assert.False(SowRules.IsExpired(new Sow { Status = SowStatus.Approved, ExpiresOn = today.AddDays(-1) }, today));
    }

    [Fact]
    public void Rates_and_quantities_load_without_trailing_zeros()
    {
        Assert.Equal("1200", SowRules.Plain(1200.0m).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("0.25", SowLineDraft.From(new SowLine { Quantity = 0.250m, Rate = 150.00m }).Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("215", SowRateDraft.From(new SowRate { Rate = 215.0m }).Rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void File_names_drop_characters_windows_refuses()
    {
        Assert.Equal("SOW 953100 - McCormick EDI Implementation.pdf",
            SowRules.FileName(new Sow { Number = "953100", ProjectName = "McCormick EDI Implementation" }));
        Assert.Equal("SOW - EDI 940-945 - phase 2.pdf", SowRules.FileName(new Sow { ProjectName = "EDI 940/945 - phase 2" }));
    }
}

public sealed class SowServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestDbFactory _factory;
    private readonly PortfolioService _svc;

    public SowServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(_connection).Options);
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        _svc = new PortfolioService(_factory);
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>The McCormick EDI SOW: 26 h PM, 240 h EDI, 84 h testing at $150.</summary>
    private static SowDraft McCormick(int? customerId = null) => new()
    {
        Number = "953100", ProjectName = "McCormick EDI Implementation", CustomerId = customerId, CustomerName = "McCormick", Site = "Hutchins 1, TX",
        Manager = "Sean Estes", PreparedOn = new DateOnly(2026, 3, 12), ExpiresOn = new DateOnly(2026, 6, 10),
        Overview = "Integration of existing Hutchins 1 customer McCormick",
        Scope = "- Project management\n- Configuration of Synapse WMS\n- Unit Testing, Quality Assurance Testing\n- Production Deployment\n- Go Live support",
        DetailedDescription = "Integration of the basic 6 EDI documents identified below according to map specifications provided by McCormick.\n" +
            "Outbound from McCormick to Kenco\n  - EDI 940 – Warehouse Shipping Order\n  - EDI 856 – Advance Ship Notice\n  - EDI 888 – Item Maintenance\n" +
            "Inbound from Kenco to McCormick\n  - EDI 945 – Warehouse Shipping Advice\n  - EDI 846 – Inventory Snapshot\n  - EDI 944 – Goods Receipt Confirmation",
        Assumptions = "- Testing to include SIT and UAT utilizing the Kenco Test environment.\n- McCormick to provide specifications aligned with established processes.",
        Schedule = SowRules.DefaultScheduleText,
        IncludeCostDetail = true,
        Lines =
        [
            new() { Category = SowCostCategory.LaborItPm, Description = "Project management", Quantity = 26, Unit = "hour", Rate = 150 },
            new() { Category = SowCostCategory.LaborEdi, Description = "Development", Quantity = 240, Unit = "hour", Rate = 150, Note = "Cleo hours" },
            new() { Category = SowCostCategory.LaborTesting, Description = "Solution end-to-end", Quantity = 84, Unit = "hour", Rate = 150 },
            new() { Category = SowCostCategory.Software, Description = "AS2 license maintenance", Quantity = 0, Unit = "each", Rate = 500, Frequency = SowFrequency.Annual },
        ],
    };

    [Fact]
    public async Task The_rate_card_and_settings_start_from_the_estimation_template()
    {
        var rates = await _svc.GetSowRatesAsync();
        Assert.Equal(41, rates.Count);
        Assert.Contains(rates, r => r is { Category: SowCostCategory.LaborWms, Name: "Development", Rate: 215m, Unit: "hour" });
        Assert.Contains(rates, r => r is { Category: SowCostCategory.Software, Frequency: SowFrequency.Annual, Name: "WMS license", Rate: 1200m });
        Assert.Equal(12, rates.Count(r => r.Category == SowCostCategory.LaborTesting));
        Assert.Equal(rates.OrderBy(r => r.Category).ThenBy(r => r.SortOrder).Select(r => r.Id), rates.Select(r => r.Id));

        var settings = await _svc.GetSowSettingsAsync();
        Assert.Equal(90, settings.ExpirationDays);
        Assert.Equal(["Kenco Management Services, LLC", "Kenco Logistics Services, LLC"], SowRules.SignatoryList(settings.Signatories));
        Assert.StartsWith("Total costs above do not include", settings.CostFootnote);
    }

    [Fact]
    public async Task Saving_replaces_the_lines_and_a_deleted_customer_keeps_the_name()
    {
        var partner = await _svc.SavePartnerAsync(new TradingPartnerDraft { Name = "McCormick" });
        var id = await _svc.SaveSowAsync(McCormick(partner));
        var sow = (await _svc.GetSowAsync(id))!;
        Assert.Equal(52_500m, sow.Totals.OneTime);
        Assert.Equal(["Project management", "Development", "Solution end-to-end", "AS2 license maintenance"], sow.Lines.Select(l => l.Description));

        var draft = SowDraft.From(sow);
        draft.Lines.RemoveAt(0);
        draft.Lines[^1].Quantity = 1;
        draft.Status = SowStatus.Sent;
        await _svc.SaveSowAsync(draft);
        sow = (await _svc.GetSowAsync(id))!;
        Assert.Equal((48_600m, 500m, SowStatus.Sent), (sow.Totals.OneTime, sow.Totals.Annual, sow.Status));
        Assert.Equal(3, sow.Lines.Count);
        Assert.Single(await _svc.GetSowsAsync(partner));

        await _svc.DeletePartnerAsync(partner);
        sow = (await _svc.GetSowAsync(id))!;
        Assert.Equal((null, "McCormick"), (sow.CustomerId, sow.CustomerName));

        await _svc.SetSowStatusAsync(id, SowStatus.Approved);
        Assert.Equal(SowStatus.Approved, (await _svc.GetSowAsync(id))!.Status);
        await _svc.DeleteSowAsync(id);
        Assert.Empty(await _svc.GetSowsAsync());
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.SowLines);
    }

    [Fact]
    public async Task Rates_and_settings_can_be_changed()
    {
        var id = await _svc.SaveSowRateAsync(new SowRateDraft { Category = SowCostCategory.LaborInfrastructure, Name = "Network engineer", Rate = 165, Unit = "hour" });
        var rate = (await _svc.GetSowRatesAsync()).Single(r => r.Id == id);
        Assert.Equal(0, rate.SortOrder);
        var edit = SowRateDraft.From(rate);
        edit.IsActive = false;
        await _svc.SaveSowRateAsync(edit);
        Assert.DoesNotContain(await _svc.GetSowRatesAsync(includeInactive: false), r => r.Id == id);
        await _svc.DeleteSowRateAsync(id);
        Assert.Equal(41, (await _svc.GetSowRatesAsync()).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveSowRateAsync(new SowRateDraft { Name = "Bad", Rate = -1 }));

        var settings = SowSettingsDraft.From(await _svc.GetSowSettingsAsync());
        settings.DefaultManager = "Sean Estes";
        settings.Signatories = "  Kenco Management Services, LLC \n\n";
        await _svc.SaveSowSettingsAsync(settings);
        var saved = await _svc.GetSowSettingsAsync();
        Assert.Equal(("Sean Estes", "Kenco Management Services, LLC"), (saved.DefaultManager, saved.Signatories));
        settings.Signatories = " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SaveSowSettingsAsync(settings));
    }

    [Fact]
    public async Task The_pdf_renders_with_the_cost_detail_page()
    {
        var id = await _svc.SaveSowAsync(McCormick());
        var sow = (await _svc.GetSowAsync(id))!;
        var bytes = SowPdf.Render(sow, await _svc.GetSowSettingsAsync());
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(bytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.InRange(pdf.PageCount, 2, 4);

        sow.IncludeCostDetail = false;
        sow.Overview = sow.Scope = sow.DetailedDescription = sow.Assumptions = sow.Schedule = null;
        using var plain = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(SowPdf.Render(sow, new SowSettings { CostFootnote = null })), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.InRange(plain.PageCount, 1, 2); // summary, costs and signatures, with no description
    }

    [Fact]
    public async Task Sows_and_the_rate_card_travel_in_the_portfolio_file_and_merge_once()
    {
        var partner = await _svc.SavePartnerAsync(new TradingPartnerDraft { Name = "McCormick" });
        await _svc.SaveSowAsync(McCormick(partner));
        await _svc.SaveSowRateAsync(new SowRateDraft { Category = SowCostCategory.LaborInfrastructure, Name = "Network engineer", Rate = 165, Unit = "hour" });
        var s = SowSettingsDraft.From(await _svc.GetSowSettingsAsync());
        s.DefaultManager = "Sean Estes";
        await _svc.SaveSowSettingsAsync(s);

        using var buffer = new MemoryStream((await _svc.ExportPortfolioAsync()).ToBytes());
        var file = PortfolioFile.Read(buffer);
        Assert.Single(file.Sows);
        Assert.Equal(42, file.SowRates.Count);

        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<RoadmapDbContext>().UseSqlite(other).Options);
        using (var db = factory.CreateDbContext()) db.Database.Migrate();
        var target = new PortfolioService(factory);
        var result = await target.ImportPortfolioAsync(file, PortfolioImportMode.Restore);
        Assert.Equal(1, result.Added["SOWs"]);
        var sow = (await target.GetSowsAsync()).Single();
        Assert.Equal((52_500m, "McCormick", "Hutchins 1, TX", 4), (sow.Totals.OneTime, sow.CustomerName, sow.Site, sow.Lines.Count));
        Assert.Equal((await target.GetPartnersAsync()).Single().Id, sow.CustomerId);
        Assert.Equal("Cleo hours", sow.Lines.Single(l => l.Description == "Development").Note);
        Assert.Equal(42, (await target.GetSowRatesAsync()).Count);
        Assert.Equal("Sean Estes", (await target.GetSowSettingsAsync()).DefaultManager);

        var again = await target.ImportPortfolioAsync(file, PortfolioImportMode.Merge);
        Assert.Equal(0, again.Added.GetValueOrDefault("SOWs"));
        Assert.Equal(0, again.Added.GetValueOrDefault("SOW rates"));
        Assert.Single(await target.GetSowsAsync());
    }

    private sealed class TestDbFactory(DbContextOptions<RoadmapDbContext> options) : IDbContextFactory<RoadmapDbContext>
    {
        public RoadmapDbContext CreateDbContext() => new(options);
    }
}
