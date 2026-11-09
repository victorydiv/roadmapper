using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

public partial class PortfolioService
{
    /// <summary>Every trading partner with its relationship owner and people.</summary>
    public async Task<List<TradingPartner>> GetPartnersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TradingPartners.AsNoTracking()
            .Include(p => p.RelationshipOwner)
            .Include(p => p.Staff)
            .OrderBy(p => p.Name)
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<TradingPartner?> GetPartnerAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TradingPartners.AsNoTracking()
            .Include(p => p.RelationshipOwner)
            .Include(p => p.Staff)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<int> SavePartnerAsync(TradingPartnerDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var name = draft.Name.Trim();
        if (await db.TradingPartners.AnyAsync(p => p.Id != draft.Id && p.Name == name))
            throw new InvalidOperationException($"A trading partner named “{name}” already exists.");
        await EnsureContactAsync(db, draft.RelationshipOwnerId, "relationship owner");

        TradingPartner partner;
        if (draft.IsNew)
        {
            partner = new TradingPartner();
            db.TradingPartners.Add(partner);
        }
        else partner = await db.TradingPartners.FindAsync(draft.Id) ?? throw NotFound("Trading partner");

        draft.ApplyTo(partner);
        partner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return partner.Id;
    }

    /// <summary>
    /// Deletes the partner and its people. Refused while integrations still connect to it: delete those first,
    /// or set the partner to Inactive to keep its history.
    /// </summary>
    public async Task DeletePartnerAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.TradingPartners.FindAsync(id) is not { } partner) return;
        var used = await db.Integrations.Where(i => i.SourcePartnerId == id || i.TargetPartnerId == id)
            .OrderBy(i => i.Name).Select(i => i.Name).ToListAsync();
        if (used.Count > 0)
            throw new InvalidOperationException(
                $"{partner.Name} has {used.Count} integration{(used.Count == 1 ? "" : "s")}: {string.Join(", ", used.Take(5))}{(used.Count > 5 ? ", …" : "")}. " +
                "Delete those integrations first, or set the partner to Inactive to keep its history.");
        db.TradingPartners.Remove(partner);
        await db.SaveChangesAsync();
    }
}
