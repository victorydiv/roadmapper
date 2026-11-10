using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>One category's comparison at a glance: how many features and applications, and how much is rated.</summary>
public record CapabilityOverview(AppCategory Category, int Features, int Applications, int AssessedPct, int Gaps);

/// <summary>An application's ratings in one category's comparison, feature by feature (null: not rated).</summary>
public record AppFeatureGroup(AppCategory Category, bool Added, IReadOnlyList<(CapabilityFeature Feature, FeatureAssessment? Rating)> Features,
    IReadOnlyList<PricingLine> Pricing, CostEstimate Cost, SiteProfile Site);

public partial class PortfolioService
{
    private async Task<(List<CapabilityFeature> Features, List<Application> Apps, List<ComparisonMember> Members, List<FeatureAssessment> Ratings)>
        LoadCapabilitiesAsync(RoadmapDbContext db, AppCategory? category)
    {
        var features = await db.CapabilityFeatures.AsNoTracking().Where(f => category == null || f.Category == category).ToListAsync();
        var apps = await db.Applications.AsNoTracking().Include(a => a.Vendor).ToListAsync();
        var members = await db.ComparisonMembers.AsNoTracking().Where(m => category == null || m.Category == category).ToListAsync();
        var ids = features.Select(f => f.Id).ToList();
        var ratings = await db.FeatureAssessments.AsNoTracking().Where(a => ids.Contains(a.FeatureId)).ToListAsync();
        return (features, apps, members, ratings);
    }

    /// <summary>Every category, with its feature and application counts, how much is rated, and its gaps.</summary>
    public async Task<List<CapabilityOverview>> GetCapabilityOverviewAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var (features, apps, members, ratings) = await LoadCapabilitiesAsync(db, null);
        return Enum.GetValues<AppCategory>().Select(c =>
        {
            var m = CapabilityMatrix.Build(c, features, apps, members.Where(x => x.Category == c).Select(x => x.ApplicationId), ratings);
            var cells = m.Features.Count * m.Apps.Count;
            var rated = m.ByApp.Values.Sum(v => v.Assessed);
            return new CapabilityOverview(c, m.Features.Count, m.Apps.Count, cells == 0 ? 0 : (int)Math.Round(100.0 * rated / cells), m.Features.Count == 0 ? 0 : m.Gaps);
        }).ToList();
    }

    /// <summary>
    /// A category's comparison: its features × its applications and those added to it, with each application's cost
    /// for a site. Retired applications only when asked. The site is <paramref name="site"/> when given (unsaved, "try
    /// your site"), else profile <paramref name="profileId"/>, else the category's default profile, else one site with
    /// unknown sizes. <paramref name="only"/> narrows it to some of its applications.
    /// </summary>
    public async Task<CapabilityMatrix> GetCapabilityMatrixAsync(AppCategory category, bool includeRetired = false, int? profileId = null,
        SiteProfile? site = null, IEnumerable<int>? only = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var (features, apps, members, ratings) = await LoadCapabilitiesAsync(db, category);
        var pricing = await db.PricingLines.AsNoTracking().Where(l => l.Category == category).ToListAsync();
        site ??= await ResolveSiteProfileAsync(db, category, profileId);
        return CapabilityMatrix.Build(category, features, apps, members.Select(m => m.ApplicationId), ratings, includeRetired, pricing, site, only);
    }

    private static async Task<SiteProfile> ResolveSiteProfileAsync(RoadmapDbContext db, AppCategory category, int? profileId)
    {
        var profiles = await db.SiteProfiles.AsNoTracking().Where(p => p.Category == category).ToListAsync();
        return profiles.FirstOrDefault(p => p.Id == profileId) ?? profiles.FirstOrDefault(p => p.IsDefault)
            ?? profiles.OrderBy(p => p.Name).FirstOrDefault() ?? SiteProfile.OneSite(category);
    }

    // ---- Cost of adoption ----

    /// <summary>A category's site profiles, the default first, then by name.</summary>
    public async Task<List<SiteProfile>> GetSiteProfilesAsync(AppCategory category)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.SiteProfiles.AsNoTracking().Where(p => p.Category == category).ToListAsync())
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Creates or updates a site profile. Names are unique within a category (ignoring case). The first profile of a
    /// category, or one saved as default, becomes the default.
    /// </summary>
    public async Task<int> SaveSiteProfileAsync(SiteProfileDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var name = draft.Name.Trim();
        var siblings = await db.SiteProfiles.Where(p => p.Category == draft.Category).ToListAsync();
        if (siblings.Any(p => p.Id != draft.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{PortfolioText.Label(draft.Category)} already has a site profile named “{name}”.");
        SiteProfile profile;
        if (draft.IsNew)
        {
            profile = db.SiteProfiles.Add(new SiteProfile()).Entity;
        }
        else
        {
            profile = siblings.FirstOrDefault(p => p.Id == draft.Id) ?? throw NotFound("Site profile");
        }
        draft.ApplyTo(profile);
        profile.UpdatedAt = DateTime.UtcNow;
        if (draft.IsDefault || !siblings.Any(p => p.Id != profile.Id && p.IsDefault))
        {
            foreach (var other in siblings.Where(p => p != profile && p.IsDefault)) other.IsDefault = false;
            profile.IsDefault = true;
        }
        await db.SaveChangesAsync();
        return profile.Id;
    }

    /// <summary>Makes a site profile its category's default.</summary>
    public async Task SetDefaultSiteProfileAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.SiteProfiles.FindAsync(id) is not { } profile) throw NotFound("Site profile");
        foreach (var p in await db.SiteProfiles.Where(p => p.Category == profile.Category).ToListAsync()) p.IsDefault = p.Id == id;
        await db.SaveChangesAsync();
    }

    /// <summary>Deletes a site profile. If it was the default, the next by name becomes the default.</summary>
    public async Task DeleteSiteProfileAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.SiteProfiles.FindAsync(id) is not { } profile) return;
        db.SiteProfiles.Remove(profile);
        if (profile.IsDefault
            && (await db.SiteProfiles.Where(p => p.Category == profile.Category && p.Id != id).ToListAsync()).OrderBy(p => p.Name).FirstOrDefault() is { } next)
            next.IsDefault = true;
        await db.SaveChangesAsync();
    }

    /// <summary>An application's pricing in a category's comparison, in order.</summary>
    public async Task<List<PricingLine>> GetPricingAsync(AppCategory category, int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.PricingLines.AsNoTracking().Where(l => l.Category == category && l.ApplicationId == applicationId).ToListAsync())
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();
    }

    /// <summary>Adds or changes a line of an application's pricing. The application must be in the category's comparison.</summary>
    public async Task<int> SavePricingLineAsync(PricingLineDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.ApplicationId) ?? throw NotFound("Application");
        if (app.Category != draft.Category && !await db.ComparisonMembers.AnyAsync(m => m.Category == draft.Category && m.ApplicationId == app.Id))
            throw new InvalidOperationException($"{app.Name} isn't in the {PortfolioText.Label(draft.Category)} comparison. Add it there first.");
        PricingLine line;
        if (draft.IsNew)
        {
            var last = await db.PricingLines.Where(l => l.Category == draft.Category && l.ApplicationId == app.Id).Select(l => (int?)l.SortOrder).MaxAsync();
            line = db.PricingLines.Add(new PricingLine { SortOrder = (last ?? -1) + 1 }).Entity;
        }
        else
        {
            line = await db.PricingLines.FirstOrDefaultAsync(l => l.Id == draft.Id) ?? throw NotFound("Price");
        }
        draft.ApplyTo(line);
        line.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return line.Id;
    }

    public async Task DeletePricingLineAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.PricingLines.FindAsync(id) is not { } line) return;
        db.PricingLines.Remove(line);
        await db.SaveChangesAsync();
    }

    /// <summary>Creates or renames a feature. Names are unique within a category (ignoring case). New features go last.</summary>
    public async Task<int> SaveFeatureAsync(FeatureDraft draft)
    {
        if (draft.Validate() is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var name = draft.Name.Trim();
        var siblings = await db.CapabilityFeatures.Where(f => f.Category == draft.Category).ToListAsync();
        if (siblings.Any(f => f.Id != draft.Id && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{PortfolioText.Label(draft.Category)} already has a feature named “{name}”.");
        CapabilityFeature feature;
        if (draft.IsNew)
        {
            feature = new CapabilityFeature { SortOrder = siblings.Select(f => f.SortOrder).DefaultIfEmpty(-1).Max() + 1 };
            db.CapabilityFeatures.Add(feature);
        }
        else
        {
            feature = siblings.FirstOrDefault(f => f.Id == draft.Id) ?? throw NotFound("Feature");
        }
        draft.ApplyTo(feature);
        feature.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return feature.Id;
    }

    /// <summary>Deletes a feature and every rating on it.</summary>
    public async Task DeleteFeatureAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.CapabilityFeatures.Include(f => f.Assessments).FirstOrDefaultAsync(f => f.Id == id) is not { } feature) return;
        db.FeatureAssessments.RemoveRange(feature.Assessments);
        db.CapabilityFeatures.Remove(feature);
        await db.SaveChangesAsync();
    }

    /// <summary>Moves a feature one place up (<paramref name="delta"/> −1) or down (+1) in its category's order.</summary>
    public async Task MoveFeatureAsync(int id, int delta)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.CapabilityFeatures.FindAsync(id) is not { } feature) return;
        var order = (await db.CapabilityFeatures.Where(f => f.Category == feature.Category).ToListAsync())
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList();
        var at = order.FindIndex(f => f.Id == id);
        var to = Math.Clamp(at + Math.Sign(delta), 0, order.Count - 1);
        if (to == at) return;
        order.RemoveAt(at);
        order.Insert(to, feature);
        for (var i = 0; i < order.Count; i++) order[i].SortOrder = i;
        await db.SaveChangesAsync();
    }

    /// <summary>Adds an application from another category to this category's comparison. Adding twice is fine.</summary>
    public async Task AddComparisonMemberAsync(AppCategory category, int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId) ?? throw NotFound("Application");
        if (app.Category == category) throw new InvalidOperationException($"{app.Name} is a {PortfolioText.Label(category)} application, so it's already compared.");
        if (await db.ComparisonMembers.AnyAsync(m => m.Category == category && m.ApplicationId == applicationId)) return;
        db.ComparisonMembers.Add(new ComparisonMember { Category = category, ApplicationId = applicationId });
        await db.SaveChangesAsync();
    }

    /// <summary>Takes an added application out of the comparison. Its ratings are kept, and come back if it's added again.</summary>
    public async Task RemoveComparisonMemberAsync(AppCategory category, int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ComparisonMembers.FindAsync(category, applicationId) is not { } member) return;
        db.ComparisonMembers.Remove(member);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Rates an application on a feature. Availability No sets strength None (a higher strength is refused). Clearing
    /// everything removes the rating. The application must be in the feature's comparison.
    /// </summary>
    public async Task SaveAssessmentAsync(int featureId, int applicationId, FeatureAvailability? availability, FeatureStrength? strength, string? note)
    {
        if (CapabilityRules.Normalize(ref availability, ref strength, ref note) is { } error) throw new InvalidOperationException(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var feature = await db.CapabilityFeatures.AsNoTracking().FirstOrDefaultAsync(f => f.Id == featureId) ?? throw NotFound("Feature");
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId) ?? throw NotFound("Application");
        if (app.Category != feature.Category && !await db.ComparisonMembers.AnyAsync(m => m.Category == feature.Category && m.ApplicationId == applicationId))
            throw new InvalidOperationException($"{app.Name} isn't in the {PortfolioText.Label(feature.Category)} comparison. Add it there first.");

        var row = await db.FeatureAssessments.FindAsync(featureId, applicationId);
        if (CapabilityRules.IsEmpty(availability, strength, note))
        {
            if (row is not null) db.FeatureAssessments.Remove(row);
        }
        else
        {
            row ??= db.FeatureAssessments.Add(new FeatureAssessment { FeatureId = featureId, ApplicationId = applicationId }).Entity;
            (row.Availability, row.Strength, row.Note, row.UpdatedAt) = (availability, strength, note, DateTime.UtcNow);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The comparisons an application is in (its own category, then those it was added to, skipping categories with no
    /// features) with its rating on each feature.
    /// </summary>
    public async Task<List<AppFeatureGroup>> GetFeaturesForApplicationAsync(int applicationId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId);
        if (app is null) return [];
        var added = await db.ComparisonMembers.AsNoTracking().Where(m => m.ApplicationId == applicationId).Select(m => m.Category).ToListAsync();
        var categories = new[] { app.Category }.Concat(added.OrderBy(c => c)).Distinct().ToList();
        var features = await db.CapabilityFeatures.AsNoTracking().Where(f => categories.Contains(f.Category)).ToListAsync();
        var ratings = await db.FeatureAssessments.AsNoTracking().Where(a => a.ApplicationId == applicationId).ToDictionaryAsync(a => a.FeatureId);
        var pricing = await db.PricingLines.AsNoTracking().Where(l => l.ApplicationId == applicationId).ToListAsync();
        var sites = new Dictionary<AppCategory, SiteProfile>();
        foreach (var c in categories) sites[c] = await ResolveSiteProfileAsync(db, c, null);
        return categories
            .Select(c =>
            {
                var lines = pricing.Where(l => l.Category == c).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();
                return new AppFeatureGroup(c, c != app.Category, features.Where(f => f.Category == c).OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                    .Select(f => (f, ratings.GetValueOrDefault(f.Id))).ToList(), lines, lines.Count > 0 ? CostEstimate.For(lines, sites[c]) : CostEstimate.NotPriced, sites[c]);
            })
            .Where(g => g.Features.Count > 0 || g.Pricing.Count > 0).ToList();
    }
}
