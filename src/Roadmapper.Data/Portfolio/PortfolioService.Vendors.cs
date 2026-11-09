using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

public partial class PortfolioService
{
    // ---- Vendors ----

    /// <summary>Every vendor with its contracts, account manager and applications.</summary>
    public async Task<List<Vendor>> GetVendorsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Vendors.AsNoTracking()
            .Include(v => v.Contracts)
            .Include(v => v.AccountManager)
            .Include(v => v.Applications)
            .OrderBy(v => v.Name)
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<Vendor?> GetVendorAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Vendors.AsNoTracking()
            .Include(v => v.Contracts.OrderBy(c => c.RenewalDate))
            .Include(v => v.Staff.OrderBy(c => c.Name))
            .Include(v => v.EscalationContacts.OrderBy(x => x.Level)).ThenInclude(x => x.Contact)
            .Include(v => v.AccountManager)
            .Include(v => v.TechnicalContact)
            .Include(v => v.Applications.OrderBy(a => a.Name)).ThenInclude(a => a.Contract)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<int> SaveVendorAsync(VendorDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var name = draft.Name.Trim();
        if (await db.Vendors.AnyAsync(v => v.Id != draft.Id && v.Name == name))
            throw new InvalidOperationException($"A vendor named “{name}” already exists.");
        await EnsureContactAsync(db, draft.AccountManagerId, "account manager");
        await EnsureContactAsync(db, draft.TechnicalContactId, "technical contact");

        Vendor vendor;
        if (draft.IsNew)
        {
            vendor = new Vendor();
            db.Vendors.Add(vendor);
        }
        else vendor = await db.Vendors.FindAsync(draft.Id) ?? throw NotFound("Vendor");

        draft.ApplyTo(vendor);
        vendor.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    /// <summary>Deletes the vendor with its contracts and its people. Its applications stay, with no vendor or contract.</summary>
    public async Task DeleteVendorAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Vendors.FindAsync(id) is not { } vendor) return;
        db.Vendors.Remove(vendor);
        await db.SaveChangesAsync();
    }

    // ---- Contracts ----

    public async Task<int> SaveContractAsync(ContractDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        Contract contract;
        if (draft.IsNew)
        {
            if (!await db.Vendors.AnyAsync(v => v.Id == draft.VendorId)) throw NotFound("Vendor");
            contract = new Contract { VendorId = draft.VendorId };
            db.Contracts.Add(contract);
        }
        else contract = await db.Contracts.FindAsync(draft.Id) ?? throw NotFound("Contract");

        draft.ApplyTo(contract);
        contract.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return contract.Id;
    }

    /// <summary>Applications covered by the contract keep their vendor but lose the contract (and so their renewal date).</summary>
    public async Task DeleteContractAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Contracts.FindAsync(id) is not { } contract) return;
        db.Contracts.Remove(contract);
        await db.SaveChangesAsync();
    }

    /// <summary>Contracts whose notice deadline (or renewal, when there's no notice period) falls within the next <paramref name="withinDays"/> days, soonest first. Missed notice deadlines whose renewal is still ahead are included.</summary>
    public async Task<List<RenewalRow>> GetRenewalsAsync(int? withinDays = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var days = withinDays ?? (await db.PortfolioSettings.AsNoTracking().FirstOrDefaultAsync())?.RenewalWarningDays ?? 120;
        var today = Today;
        var horizon = today.AddDays(days);

        var contracts = await db.Contracts.AsNoTracking().Include(c => c.Vendor)
            .Where(c => c.RenewalDate != null && c.RenewalDate >= today).ToListAsync();
        var due = contracts.Where(c => c.NoticeDeadline <= horizon).OrderBy(c => c.NoticeDeadline).ToList();
        var ids = due.Select(c => c.Id).ToList();
        var apps = await db.Applications.AsNoTracking().Where(a => a.ContractId != null && ids.Contains(a.ContractId.Value))
            .Select(a => new { a.ContractId, a.Name }).ToListAsync();

        return due.Select(c => new RenewalRow(c, c.Vendor?.Name ?? "",
            apps.Where(a => a.ContractId == c.Id).Select(a => a.Name).Order().ToList())).ToList();
    }

    // ---- Escalation path ----

    public async Task AddEscalationContactAsync(int vendorId, int contactId, string? note)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Vendors.AnyAsync(v => v.Id == vendorId)) throw NotFound("Vendor");
        await EnsureContactAsync(db, contactId, "contact");
        var path = await db.VendorEscalationContacts.Where(x => x.VendorId == vendorId).ToListAsync();
        if (path.Any(x => x.ContactId == contactId)) throw new InvalidOperationException("That person is already on the escalation path.");
        db.VendorEscalationContacts.Add(new VendorEscalationContact
        {
            VendorId = vendorId, ContactId = contactId, Note = Clean(note),
            Level = path.Count == 0 ? 1 : path.Max(x => x.Level) + 1,
        });
        await db.SaveChangesAsync();
    }

    public async Task RemoveEscalationContactAsync(int vendorId, int contactId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var path = await db.VendorEscalationContacts.Where(x => x.VendorId == vendorId).OrderBy(x => x.Level).ToListAsync();
        if (path.FirstOrDefault(x => x.ContactId == contactId) is not { } step) return;
        db.VendorEscalationContacts.Remove(step);
        path.Remove(step);
        Renumber(path);
        await db.SaveChangesAsync();
    }

    /// <summary>Moves a person one level earlier (-1) or later (+1) on the escalation path.</summary>
    public async Task MoveEscalationContactAsync(int vendorId, int contactId, int direction)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var path = await db.VendorEscalationContacts.Where(x => x.VendorId == vendorId).OrderBy(x => x.Level).ToListAsync();
        var index = path.FindIndex(x => x.ContactId == contactId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= path.Count) return;
        (path[index], path[target]) = (path[target], path[index]);
        Renumber(path);
        await db.SaveChangesAsync();
    }

    private static void Renumber(List<VendorEscalationContact> path)
    {
        for (var i = 0; i < path.Count; i++) path[i].Level = i + 1;
    }
}
