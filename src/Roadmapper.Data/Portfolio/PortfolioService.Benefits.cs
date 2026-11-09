using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>A process KPI offered in the benefit editor, with its process for context.</summary>
public record KpiOption(int Id, string Name, string ProcessName, string? Unit);

public partial class PortfolioService
{
    private static IQueryable<Benefit> WithParts(IQueryable<Benefit> q) => q
        .Include(b => b.RoadmapItem).ThenInclude(i => i!.Product)
        .Include(b => b.Application)
        .Include(b => b.Kpi).ThenInclude(k => k!.Process)
        .Include(b => b.Measurements)
        .AsSplitQuery();

    /// <summary>Benefits, optionally only one product's or one roadmap item's, with their item, application, KPI and measurements.</summary>
    public async Task<List<Benefit>> GetBenefitsAsync(int? productId = null, int? itemId = null, int? applicationId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = WithParts(db.Benefits.AsNoTracking());
        if (productId is { } pid) q = q.Where(b => b.RoadmapItem!.ProductId == pid);
        if (itemId is { } iid) q = q.Where(b => b.RoadmapItemId == iid);
        if (applicationId is { } aid) q = q.Where(b => b.ApplicationId == aid);
        var list = await q.ToListAsync();
        return list.OrderBy(b => b.RoadmapItem!.Product!.Name).ThenBy(b => b.RoadmapItem!.Title).ThenBy(b => b.Category).ThenBy(b => b.Description).ToList();
    }

    public async Task<Benefit?> GetBenefitAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await WithParts(db.Benefits.AsNoTracking()).FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<int> SaveBenefitAsync(BenefitDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Items.AnyAsync(i => i.Id == draft.RoadmapItemId)) throw NotFound("Roadmap item");
        if (draft.ApplicationId is { } appId && !await db.Applications.AnyAsync(a => a.Id == appId)) throw NotFound("Application");
        if (draft.KpiId is { } kpiId && !await db.ProcessKpis.AnyAsync(k => k.Id == kpiId)) throw NotFound("KPI");

        Benefit benefit;
        if (draft.IsNew)
        {
            benefit = new Benefit();
            db.Benefits.Add(benefit);
        }
        else benefit = await db.Benefits.FindAsync(draft.Id) ?? throw NotFound("Benefit");

        draft.ApplyTo(benefit);
        benefit.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return benefit.Id;
    }

    public async Task DeleteBenefitAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Benefits.FindAsync(id) is not { } benefit) return;
        db.Benefits.Remove(benefit);
        await db.SaveChangesAsync();
    }

    /// <summary>Records what the benefit was running at on a date. A second reading on the same date replaces the first.</summary>
    public async Task SaveMeasurementAsync(int benefitId, DateOnly measuredOn, decimal value, string? source = null, string? note = null)
    {
        if (measuredOn > Today) throw new InvalidOperationException("A measurement can't be dated in the future.");
        if (source?.Trim().Length > 200) throw new InvalidOperationException("Source can be at most 200 characters.");
        if (note?.Trim().Length > 1000) throw new InvalidOperationException("Note can be at most 1000 characters.");
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Benefits.AnyAsync(b => b.Id == benefitId)) throw NotFound("Benefit");
        var row = await db.BenefitMeasurements.FirstOrDefaultAsync(m => m.BenefitId == benefitId && m.MeasuredOn == measuredOn);
        if (row is null)
        {
            row = new BenefitMeasurement { BenefitId = benefitId, MeasuredOn = measuredOn };
            db.BenefitMeasurements.Add(row);
        }
        row.Value = value;
        row.Source = Clean(source);
        row.Note = Clean(note);
        await db.SaveChangesAsync();
    }

    public async Task DeleteMeasurementAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.BenefitMeasurements.FindAsync(id) is not { } row) return;
        db.BenefitMeasurements.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>The product a roadmap item belongs to, or null when it doesn't exist.</summary>
    public async Task<int?> GetItemProductIdAsync(int itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Items.Where(i => i.Id == itemId).Select(i => (int?)i.ProductId).FirstOrDefaultAsync();
    }

    /// <summary>Every process KPI, for linking a benefit to the measure that tracks it.</summary>
    public async Task<List<KpiOption>> GetKpiOptionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.ProcessKpis.AsNoTracking()
            .Select(k => new KpiOption(k.Id, k.Name, k.Process!.Name, k.Unit)).ToListAsync();
        return rows.OrderBy(k => k.ProcessName).ThenBy(k => k.Name).ToList();
    }

    /// <summary>How many benefits a product's roadmap items carry: they're deleted with the product.</summary>
    public async Task<int> CountBenefitsForProductAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Benefits.CountAsync(b => b.RoadmapItem!.ProductId == productId);
    }

    /// <summary>Sets the dollars-per-hour rate used to value labor savings; null clears it.</summary>
    public async Task SetLaborRateAsync(decimal? rate)
    {
        if (rate is <= 0) throw new InvalidOperationException("The labor rate must be more than zero, or empty.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.PortfolioSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new PortfolioSettings();
            db.PortfolioSettings.Add(settings);
        }
        settings.LaborRate = rate;
        await db.SaveChangesAsync();
    }

    /// <summary>Saves the portfolio-wide settings (Portfolio › Settings).</summary>
    public async Task SaveSettingsAsync(decimal? laborRate, int renewalWarningDays, int defaultReviewIntervalMonths, string? jiraBaseUrl)
    {
        if (laborRate is <= 0) throw new InvalidOperationException("The labor rate must be more than zero, or empty.");
        if (renewalWarningDays is < 1 or > 730) throw new InvalidOperationException("The renewal warning window must be between 1 and 730 days.");
        if (defaultReviewIntervalMonths is < 1 or > 60) throw new InvalidOperationException("The review interval must be between 1 and 60 months.");
        var jira = Clean(jiraBaseUrl)?.TrimEnd('/');
        if (jira is not null && (jira.Length > ExternalLinks.MaxUrlLength || !ExternalLinks.IsWebUrl(jira)))
            throw new InvalidOperationException("The Jira site must be a web address like https://yourcompany.atlassian.net.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.PortfolioSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new PortfolioSettings();
            db.PortfolioSettings.Add(settings);
        }
        settings.LaborRate = laborRate;
        settings.RenewalWarningDays = renewalWarningDays;
        settings.DefaultReviewIntervalMonths = defaultReviewIntervalMonths;
        settings.JiraBaseUrl = jira;
        await db.SaveChangesAsync();
    }
}
