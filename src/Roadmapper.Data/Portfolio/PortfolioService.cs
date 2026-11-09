using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>A roadmap item linked to an application, read live from the roadmap (nothing is copied).</summary>
public record LinkedItem(int ItemId, int ProductId, string ProductName, ItemType ItemType, string Title, ItemStatus Status,
    int ProgressPct, DateOnly? StartDate, DateOnly? EndDate, string? Owner, InitiativeImpact Impact);

/// <summary>A roadmap item offered in a picker, with its ancestors' titles for context.</summary>
public record ItemOption(int Id, ItemType ItemType, string Title, string Path);

/// <summary>A contract whose renewal is coming up, with the applications it covers.</summary>
public record RenewalRow(Contract Contract, string VendorName, IReadOnlyList<string> Applications);

/// <summary>
/// Reads and writes for the Application Portfolio module. Works like <see cref="RoadmapService"/>: a short-lived
/// DbContext per call, and rule violations surface as <see cref="InvalidOperationException"/> with a user-facing message.
/// </summary>
public partial class PortfolioService(IDbContextFactory<RoadmapDbContext> dbFactory, TimeProvider? clock = null)
{
    public DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetLocalNow().DateTime);

    public async Task<PortfolioSettings> GetSettingsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.PortfolioSettings.AsNoTracking().FirstOrDefaultAsync() ?? new PortfolioSettings();
    }

    // ---- History ----

    public async Task<List<PortfolioAuditEntry>> GetActivityAsync(PortfolioEntityType type, int id, int take = 100)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.PortfolioAuditEntries.AsNoTracking()
            .Where(a => a.EntityType == type && a.EntityId == id)
            .OrderByDescending(a => a.ChangedAt).ThenByDescending(a => a.Id)
            .Take(take).ToListAsync();
    }

    public async Task<List<PortfolioAuditEntry>> GetRecentActivityAsync(int take = 50)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.PortfolioAuditEntries.AsNoTracking()
            .OrderByDescending(a => a.ChangedAt).ThenByDescending(a => a.Id)
            .Take(take).ToListAsync();
    }

    // ---- Helpers ----

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static InvalidOperationException NotFound(string what) => new($"{what} not found. It may have been deleted.");

    private static async Task EnsureContactAsync(RoadmapDbContext db, int? contactId, string role)
    {
        if (contactId is { } id && !await db.Contacts.AnyAsync(c => c.Id == id))
            throw new InvalidOperationException($"The {role} chosen no longer exists. Pick someone else.");
    }
}
