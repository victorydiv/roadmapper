using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

public partial class PortfolioService
{
    public async Task<List<Contact>> GetContactsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Contacts.AsNoTracking().Include(c => c.Vendor).OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<int> SaveContactAsync(ContactDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (draft.VendorId is { } vid && !await db.Vendors.AnyAsync(v => v.Id == vid)) throw NotFound("Vendor");

        Contact contact;
        if (draft.IsNew)
        {
            contact = new Contact();
            db.Contacts.Add(contact);
        }
        else contact = await db.Contacts.FindAsync(draft.Id) ?? throw NotFound("Contact");

        draft.ApplyTo(contact);
        contact.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return contact.Id;
    }

    /// <summary>Removes the person from every role they held (owner, account manager, support, escalation).</summary>
    public async Task DeleteContactAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Contacts.FindAsync(id) is not { } contact) return;
        db.Contacts.Remove(contact);
        await db.SaveChangesAsync();
    }

    /// <summary>Where a person is named, e.g. "Business owner of Northwind WMS", so deleting them isn't a surprise.</summary>
    public async Task<List<string>> GetContactRolesAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var roles = new List<string>();
        roles.AddRange((await db.Applications.Where(a => a.BusinessOwnerId == id).Select(a => a.Name).ToListAsync()).Select(n => $"Business owner of {n}"));
        roles.AddRange((await db.Applications.Where(a => a.TechnicalOwnerId == id).Select(a => a.Name).ToListAsync()).Select(n => $"Technical owner of {n}"));
        roles.AddRange((await db.Vendors.Where(v => v.AccountManagerId == id).Select(v => v.Name).ToListAsync()).Select(n => $"Account manager for {n}"));
        roles.AddRange((await db.Vendors.Where(v => v.TechnicalContactId == id).Select(v => v.Name).ToListAsync()).Select(n => $"Technical contact for {n}"));
        roles.AddRange((await db.ApplicationSupportContacts.Where(s => s.ContactId == id)
            .Join(db.Applications, s => s.ApplicationId, a => a.Id, (s, a) => new { a.Name, s.Role }).ToListAsync())
            .Select(x => $"{PortfolioText.Label(x.Role)} for {x.Name}"));
        roles.AddRange((await db.VendorEscalationContacts.Where(x => x.ContactId == id)
            .Join(db.Vendors, x => x.VendorId, v => v.Id, (x, v) => new { v.Name, x.Level }).ToListAsync())
            .Select(x => $"Escalation level {x.Level} for {x.Name}"));
        return roles;
    }
}
