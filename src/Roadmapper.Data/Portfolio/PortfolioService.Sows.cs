using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>Names offered while filling in a SOW: the customer's known sites, and SOW managers used before.</summary>
public sealed record SowSuggestions(IReadOnlyList<string> Sites, IReadOnlyList<string> Managers);

public partial class PortfolioService
{
    // ---- Statements of work ----

    /// <summary>Every SOW with its cost lines, most recently prepared first.</summary>
    public async Task<List<Sow>> GetSowsAsync(int? customerId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.Sows.AsNoTracking().Include(s => s.Lines).AsQueryable();
        if (customerId is { } cid) q = q.Where(s => s.CustomerId == cid);
        return (await q.ToListAsync()).OrderByDescending(s => s.PreparedOn).ThenByDescending(s => s.Id).ToList();
    }

    public async Task<Sow?> GetSowAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sow = await db.Sows.AsNoTracking().Include(s => s.Lines).Include(s => s.Customer).FirstOrDefaultAsync(s => s.Id == id);
        sow?.Lines.Sort((a, b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : a.Id.CompareTo(b.Id));
        return sow;
    }

    /// <summary>Creates or updates a SOW, replacing its cost lines with the draft's. Returns its id.</summary>
    public async Task<int> SaveSowAsync(SowDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (draft.CustomerId is { } cid && !await db.TradingPartners.AnyAsync(p => p.Id == cid))
            throw new InvalidOperationException("The customer chosen is no longer a trading partner. Pick another, or choose none and type the name.");

        Sow sow;
        if (draft.IsNew)
        {
            sow = new Sow();
            db.Sows.Add(sow);
        }
        else
        {
            sow = await db.Sows.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == draft.Id) ?? throw NotFound("SOW");
            db.SowLines.RemoveRange(sow.Lines);
            sow.Lines.Clear();
        }
        draft.ApplyTo(sow);
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var line = new SowLine();
            draft.Lines[i].ApplyTo(line, i);
            sow.Lines.Add(line);
        }
        sow.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return sow.Id;
    }

    public async Task SetSowStatusAsync(int id, SowStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sow = await db.Sows.FirstOrDefaultAsync(s => s.Id == id) ?? throw NotFound("SOW");
        sow.Status = status;
        sow.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteSowAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Sows.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Sites recorded for the customer (in the customer matrix and on its other SOWs), and managers named on SOWs.</summary>
    public async Task<SowSuggestions> GetSowSuggestionsAsync(int? customerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cmp = StringComparer.OrdinalIgnoreCase;
        var sites = new List<string>();
        if (customerId is { } cid)
        {
            sites.AddRange(await db.Deployments.AsNoTracking().Where(d => d.TradingPartnerId == cid && d.Site != null).Select(d => d.Site!).ToListAsync());
            sites.AddRange(await db.Sows.AsNoTracking().Where(s => s.CustomerId == cid && s.Site != null).Select(s => s.Site!).ToListAsync());
        }
        var managers = await db.Sows.AsNoTracking().Where(s => s.Manager != null).Select(s => s.Manager!).ToListAsync();
        return new SowSuggestions(sites.Distinct(cmp).Order(cmp).ToList(), managers.Distinct(cmp).Order(cmp).ToList());
    }

    // ---- Rate card ----

    /// <summary>The rate card in print order (category, then the order they were added).</summary>
    public async Task<List<SowRate>> GetSowRatesAsync(bool includeInactive = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.SowRates.AsNoTracking();
        if (!includeInactive) q = q.Where(r => r.IsActive);
        return (await q.ToListAsync()).OrderBy(r => r.Category).ThenBy(r => r.SortOrder).ThenBy(r => r.Id).ToList();
    }

    public async Task<int> SaveSowRateAsync(SowRateDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        SowRate rate;
        if (draft.IsNew)
        {
            var last = await db.SowRates.Where(r => r.Category == draft.Category).Select(r => (int?)r.SortOrder).MaxAsync();
            rate = new SowRate { SortOrder = (last ?? -1) + 1 };
            db.SowRates.Add(rate);
        }
        else rate = await db.SowRates.FirstOrDefaultAsync(r => r.Id == draft.Id) ?? throw NotFound("Rate");
        if (!draft.IsNew && rate.Category != draft.Category)
            rate.SortOrder = ((await db.SowRates.Where(r => r.Category == draft.Category).Select(r => (int?)r.SortOrder).MaxAsync()) ?? -1) + 1;
        draft.ApplyTo(rate);
        await db.SaveChangesAsync();
        return rate.Id;
    }

    /// <summary>Removes a rate from the card. SOWs that used it keep their lines (rates are copied onto them).</summary>
    public async Task DeleteSowRateAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SowRates.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    // ---- Settings ----

    public async Task<SowSettings> GetSowSettingsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SowSettings.AsNoTracking().FirstOrDefaultAsync() ?? new SowSettings();
    }

    public async Task SaveSowSettingsAsync(SowSettingsDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.SowSettings.FirstOrDefaultAsync() ?? db.SowSettings.Add(new SowSettings()).Entity;
        draft.ApplyTo(settings);
        await db.SaveChangesAsync();
    }
}
