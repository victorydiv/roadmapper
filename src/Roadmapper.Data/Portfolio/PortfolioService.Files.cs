using Microsoft.EntityFrameworkCore;
using Roadmapper.Core;
using Roadmapper.Core.Exchange;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>Restore: into an empty portfolio, with settings and history. Merge: adds to what's there; records that already exist are skipped.</summary>
public enum PortfolioImportMode { Restore, Merge }

/// <summary>What an import did: how many of each record it added, what it skipped as already there, and roadmap links it couldn't match.</summary>
public sealed class PortfolioImportResult
{
    public PortfolioImportMode Mode { get; init; }
    public Dictionary<string, int> Added { get; } = new();
    public List<string> Skipped { get; } = [];
    public List<string> Unmatched { get; } = [];

    internal void Count(string what, int n = 1) => Added[what] = Added.GetValueOrDefault(what) + n;

    public string Summary => Added.Count == 0
        ? "Nothing was added."
        : "Added " + string.Join(", ", Added.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}")) + ".";
}

public partial class PortfolioService
{
    /// <summary>The roadmap item paths of every product, for writing and resolving <see cref="PfRoadmapRef"/>s.</summary>
    private sealed class RoadmapIndex
    {
        private readonly Dictionary<int, PfRoadmapRef> _refs = new();
        private readonly Dictionary<(string Product, string Path), int> _ids = new();
        private readonly Dictionary<string, int> _products = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<RoadmapIndex> LoadAsync(RoadmapDbContext db)
        {
            var index = new RoadmapIndex();
            var products = await db.Products.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync();
            foreach (var p in products.OrderBy(p => p.Id)) index._products.TryAdd(p.Name, p.Id);
            var names = products.ToDictionary(p => p.Id, p => p.Name);
            var items = await db.Items.AsNoTracking().Select(i => new { i.Id, i.ProductId, i.ParentId, i.Title }).ToListAsync();
            var byId = items.ToDictionary(i => i.Id);
            foreach (var i in items.OrderBy(i => i.Id))
            {
                var parts = new List<string> { i.Title };
                for (var parent = i.ParentId; parent is { } pid && byId.TryGetValue(pid, out var p); parent = p.ParentId) parts.Insert(0, p.Title);
                var r = new PfRoadmapRef(names[i.ProductId], string.Join(RoadmapColumns.PathSeparator, parts));
                index._refs[i.Id] = r;
                index._ids.TryAdd((r.Product.ToUpperInvariant(), r.Path.ToUpperInvariant()), i.Id);
            }
            return index;
        }

        public PfRoadmapRef? RefOf(int itemId) => _refs.GetValueOrDefault(itemId);
        public int? ItemOf(PfRoadmapRef? r) => r is null ? null : _ids.TryGetValue((r.Product.ToUpperInvariant(), r.Path.ToUpperInvariant()), out var id) ? id : null;
        public int? ProductOf(string? name) => name is not null && _products.TryGetValue(name, out var id) ? id : null;
    }

    // ---- Export ----

    /// <summary>The whole portfolio as a <see cref="PortfolioFile"/>, with its settings and history.</summary>
    public async Task<PortfolioFile> ExportPortfolioAsync(string? appVersion = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var index = await RoadmapIndex.LoadAsync(db);
        var settings = await db.PortfolioSettings.AsNoTracking().FirstOrDefaultAsync() ?? new PortfolioSettings();
        var productNames = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);

        var file = new PortfolioFile
        {
            AppVersion = appVersion,
            Settings = new PfSettings
            {
                LaborRate = settings.LaborRate, RenewalWarningDays = settings.RenewalWarningDays,
                DefaultReviewIntervalMonths = settings.DefaultReviewIntervalMonths, JiraBaseUrl = settings.JiraBaseUrl,
                DocumentFolders = (await db.DocumentFolders.AsNoTracking().ToListAsync())
                    .Where(f => f.ProductId is null || productNames.ContainsKey(f.ProductId.Value))
                    .OrderBy(f => f.ProductId is { } fp ? productNames[fp] : "").ThenBy(f => f.Category)
                    .Select(f => new PfDocumentFolder(f.Category, f.LocalPath, f.SharePointUrl, f.ProductId is { } fp ? productNames[fp] : null)).ToList(),
            },
            Contacts = (await db.Contacts.AsNoTracking().OrderBy(c => c.Id).ToListAsync()).Select(c => new PfContact
            {
                Id = c.Id, Name = c.Name, Title = c.Title, Email = c.Email, Phone = c.Phone, VendorId = c.VendorId, PartnerId = c.TradingPartnerId,
                Department = c.Department, Notes = c.Notes, IsActive = c.IsActive,
            }).ToList(),
            Vendors = (await db.Vendors.AsNoTracking().Include(v => v.Contracts).Include(v => v.EscalationContacts).AsSplitQuery()
                .OrderBy(v => v.Id).ToListAsync()).Select(v => new PfVendor
            {
                Id = v.Id, Name = v.Name, Website = v.Website, AccountManagerId = v.AccountManagerId, TechnicalContactId = v.TechnicalContactId,
                SupportPortalUrl = v.SupportPortalUrl, SupportPhone = v.SupportPhone, Notes = v.Notes,
                Contracts = v.Contracts.OrderBy(c => c.Id).Select(c => new PfContract
                {
                    Id = c.Id, Name = c.Name, ContractNumber = c.ContractNumber, StartDate = c.StartDate, EndDate = c.EndDate,
                    RenewalDate = c.RenewalDate, NoticePeriodDays = c.NoticePeriodDays, AutoRenews = c.AutoRenews, AnnualValue = c.AnnualValue, Notes = c.Notes,
                }).ToList(),
                Escalation = v.EscalationContacts.OrderBy(e => e.Level).Select(e => new PfEscalation(e.ContactId, e.Level, e.Note)).ToList(),
            }).ToList(),
            Partners = (await db.TradingPartners.AsNoTracking().OrderBy(p => p.Id).ToListAsync()).Select(p => new PfPartner
            {
                Id = p.Id, Name = p.Name, Type = p.Type, Status = p.Status, Importance = p.Importance, AccountCode = p.AccountCode,
                EdiQualifier = p.EdiQualifier, EdiId = p.EdiId, GsId = p.GsId, RelationshipOwnerId = p.RelationshipOwnerId, Notes = p.Notes,
            }).ToList(),
            Applications = (await db.Applications.AsNoTracking().Include(a => a.SupportContacts).Include(a => a.Initiatives).AsSplitQuery()
                .OrderBy(a => a.Id).ToListAsync()).Select(a => new PfApplication
            {
                Id = a.Id, Name = a.Name, Description = a.Description, Category = a.Category, VendorId = a.VendorId, ContractId = a.ContractId,
                Roadmap = a.ProductId is { } pid ? productNames.GetValueOrDefault(pid) : null,
                BusinessOwnerId = a.BusinessOwnerId, TechnicalOwnerId = a.TechnicalOwnerId, Criticality = a.Criticality, Status = a.Status,
                Hosting = a.Hosting, AnnualCost = a.AnnualCost, BusinessFit = a.BusinessFit, TechnicalFit = a.TechnicalFit,
                GoLiveDate = a.GoLiveDate, RetireDate = a.RetireDate, Notes = a.Notes,
                Support = a.SupportContacts.Select(s => new PfSupport(s.ContactId, s.Role, s.Note)).ToList(),
                Initiatives = a.Initiatives.Where(l => index.RefOf(l.RoadmapItemId) is not null)
                    .Select(l => new PfInitiativeLink(index.RefOf(l.RoadmapItemId)!, l.ImpactType)).ToList(),
            }).ToList(),
            Integrations = (await db.Integrations.AsNoTracking().Include(i => i.Risks).AsSplitQuery().OrderBy(i => i.Id).ToListAsync())
                .Select(i => new PfIntegration
            {
                Id = i.Id, Name = i.Name, SourceApplicationId = i.SourceApplicationId, SourcePartnerId = i.SourcePartnerId,
                TargetApplicationId = i.TargetApplicationId, TargetPartnerId = i.TargetPartnerId, ViaApplicationId = i.ViaApplicationId,
                Type = i.Type, Direction = i.Direction, Frequency = i.Frequency, MessageFormat = i.MessageFormat, BusinessPurpose = i.BusinessPurpose,
                Criticality = i.Criticality, OwnerId = i.OwnerId, Status = i.Status, LastReviewDate = i.LastReviewDate,
                ReviewIntervalMonths = i.ReviewIntervalMonths, Notes = i.Notes,
                Risks = i.Risks.OrderBy(r => r.Id).Select(r => new PfRisk
                {
                    Description = r.Description, Likelihood = r.Likelihood, Impact = r.Impact, Mitigation = r.Mitigation, Status = r.Status,
                    OwnerId = r.OwnerId, RaisedOn = r.RaisedOn,
                }).ToList(),
            }).ToList(),
            Processes = (await db.BusinessProcesses.AsNoTracking().Include(p => p.Applications).Include(p => p.Integrations)
                .Include(p => p.PainPoints).Include(p => p.Kpis).AsSplitQuery().OrderBy(p => p.Id).ToListAsync()).Select(p => new PfProcess
            {
                Id = p.Id, Name = p.Name, Description = p.Description, Domain = p.Domain, OwnerId = p.ProcessOwnerId, ParentId = p.ParentProcessId,
                SortOrder = p.SortOrder,
                Applications = p.Applications.Select(a => new PfProcessApp(a.ApplicationId, a.Usage, a.IsPrimary)).ToList(),
                IntegrationIds = p.Integrations.Select(i => i.IntegrationId).Order().ToList(),
                PainPoints = p.PainPoints.OrderBy(x => x.Id).Select(x => new PfPainPoint
                {
                    Description = x.Description, Severity = x.Severity, ApplicationId = x.ApplicationId, Status = x.Status,
                    AddressedBy = x.AddressedByItemId is { } item ? index.RefOf(item) : null,
                }).ToList(),
                Kpis = p.Kpis.OrderBy(k => k.Id).Select(k => new PfKpi
                {
                    Id = k.Id, Name = k.Name, Unit = k.Unit, Direction = k.Direction, Baseline = k.Baseline, Target = k.Target,
                    Current = k.Current, MeasuredOn = k.MeasuredOn,
                }).ToList(),
            }).ToList(),
            Benefits = (await db.Benefits.AsNoTracking().Include(b => b.Measurements).AsSplitQuery().OrderBy(b => b.Id).ToListAsync())
                .Where(b => index.RefOf(b.RoadmapItemId) is not null).Select(b => new PfBenefit
            {
                Id = b.Id, Item = index.RefOf(b.RoadmapItemId)!, ApplicationId = b.ApplicationId, KpiId = b.KpiId, Category = b.Category,
                Description = b.Description, Unit = b.Unit, ExpectedValue = b.ExpectedValue, ExpectedBy = b.ExpectedBy, Baseline = b.Baseline, Notes = b.Notes,
                Measurements = b.Measurements.OrderBy(m => m.MeasuredOn).Select(m => new PfMeasurement(m.MeasuredOn, m.Value, m.Source, m.Note)).ToList(),
            }).ToList(),
            Decisions = (await db.Decisions.AsNoTracking().Include(d => d.Alternatives).Include(d => d.Links).AsSplitQuery()
                .OrderBy(d => d.Id).ToListAsync()).Select(d => new PfDecision
            {
                Id = d.Id, Title = d.Title, DecidedOn = d.DecidedOn, Status = d.Status, Context = d.Context, Decision = d.DecisionText,
                Outcome = d.Outcome, DecidedBy = d.DecidedBy, SupersededById = d.SupersededById,
                Alternatives = d.Alternatives.OrderBy(a => a.SortOrder).Select(a => new PfAlternative(a.Option, a.Pros, a.Cons, a.WhyRejected)).ToList(),
                Links = d.Links.Select(l => l.Target)
                    .Select(t => t.Kind == DecisionTargetKind.RoadmapItem ? new PfDecisionLink(t.Kind, null, index.RefOf(t.Id)) : new PfDecisionLink(t.Kind, t.Id, null))
                    .Where(l => l.Kind != DecisionTargetKind.RoadmapItem || l.Item is not null).ToList(),
            }).ToList(),
            Documents = (await db.Documents.AsNoTracking().OrderBy(d => d.Id).ToListAsync()).Select(d => new PfDocument
            {
                Id = d.Id, Title = d.Title, Category = d.Category, FileName = d.FileName, LocalPath = d.LocalPath, Url = d.Url,
                SizeBytes = d.SizeBytes, DocumentDate = d.DocumentDate, Notes = d.Notes, CreatedAt = d.CreatedAt,
                AttachedTo = d.Target?.Kind,
                TargetId = d.ApplicationId ?? d.VendorId ?? d.ContractId,
                Product = d.ProductId is { } dp ? productNames.GetValueOrDefault(dp) : null,
                Item = d.RoadmapItemId is { } di ? index.RefOf(di) : null,
            }).ToList(),
            Features = (await db.CapabilityFeatures.AsNoTracking().Include(f => f.Assessments).OrderBy(f => f.Category).ThenBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync())
                .Select(f => new PfFeature
                {
                    Id = f.Id, Category = f.Category, Name = f.Name, Description = f.Description, SortOrder = f.SortOrder,
                    Ratings = f.Assessments.OrderBy(a => a.ApplicationId).Select(a => new PfRating(a.ApplicationId, a.Availability, a.Strength, a.Note)).ToList(),
                }).ToList(),
            ComparisonMembers = (await db.ComparisonMembers.AsNoTracking().ToListAsync()).OrderBy(m => m.Category).ThenBy(m => m.ApplicationId)
                .Select(m => new PfComparisonMember(m.Category, m.ApplicationId)).ToList(),
            History = (await db.PortfolioAuditEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync())
                .Select(a => new PfHistory(a.EntityType, a.EntityId, a.EntityName, a.Action, a.Field, a.OldValue, a.NewValue, a.ChangedAt)).ToList(),
        };
        return file;
    }

    // ---- Import ----

    /// <summary>
    /// Imports a portfolio file in one transaction. <see cref="PortfolioImportMode.Restore"/> needs an empty portfolio
    /// and brings the settings and history along. <see cref="PortfolioImportMode.Merge"/> adds to what's there: a record
    /// whose name already exists (application, vendor, partner, process; contact by name and company; integration by
    /// ends, type and name; benefit by item and description; decision by title and date) is skipped, along with what
    /// belongs to it, and references to it point at the existing one. Roadmap links that match no product or item path
    /// are reported in <see cref="PortfolioImportResult.Unmatched"/>.
    /// </summary>
    public async Task<PortfolioImportResult> ImportPortfolioAsync(PortfolioFile file, PortfolioImportMode mode)
    {
        if (file.Validate() is { Count: > 0 } errors) throw new InvalidOperationException(errors[0]);
        if (mode == PortfolioImportMode.Restore && !await IsEmptyAsync())
            throw new InvalidOperationException("Restore only works into an empty portfolio. Use Merge to add this file to what's already here.");

        var result = new PortfolioImportResult { Mode = mode };
        var merge = mode == PortfolioImportMode.Merge;
        await using var db = await dbFactory.CreateDbContextAsync();
        // A restore brings its own history; a merge records what it adds like any other change.
        db.AuditEnabled = merge;
        await using var tx = await db.Database.BeginTransactionAsync();
        var index = await RoadmapIndex.LoadAsync(db);
        var now = DateTime.UtcNow;

        int? Item(PfRoadmapRef? r, string what)
        {
            if (r is null) return null;
            if (index.ItemOf(r) is { } id) return id;
            result.Unmatched.Add($"{what}: {r}");
            return null;
        }
        static int? Map(Dictionary<int, int> map, int? id) => id is { } x && map.TryGetValue(x, out var n) ? n : null;

        // Settings.
        if (!merge && file.Settings is { } s)
        {
            var settings = await db.PortfolioSettings.FirstOrDefaultAsync() ?? db.PortfolioSettings.Add(new PortfolioSettings()).Entity;
            settings.LaborRate = s.LaborRate;
            settings.RenewalWarningDays = s.RenewalWarningDays;
            settings.DefaultReviewIntervalMonths = s.DefaultReviewIntervalMonths;
            settings.JiraBaseUrl = s.JiraBaseUrl;
            db.DocumentFolders.RemoveRange(await db.DocumentFolders.ToListAsync());
            var folders = new HashSet<(int?, DocumentCategory)>();
            foreach (var f in s.DocumentFolders)
            {
                int? product = null;
                if (f.Product is not null && (product = index.ProductOf(f.Product)) is null)
                {
                    result.Unmatched.Add($"{PortfolioText.Label(f.Category)} document folder: roadmap {f.Product}");
                    continue;
                }
                if (folders.Add((product, f.Category)))
                    db.DocumentFolders.Add(new DocumentFolder { ProductId = product, Category = f.Category, LocalPath = f.LocalPath, SharePointUrl = f.SharePointUrl });
            }
        }

        // Vendors and partners first (contacts belong to them), then contacts, then the people they point at.
        var vendorMap = new Dictionary<int, int>();
        var newVendors = new List<(PfVendor File, Vendor Row)>();
        var existingVendors = await db.Vendors.ToListAsync();
        foreach (var v in file.Vendors)
        {
            if (merge && existingVendors.FirstOrDefault(x => string.Equals(x.Name, v.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                vendorMap[v.Id] = found.Id;
                result.Skipped.Add($"Vendor {v.Name} (already here)");
                continue;
            }
            var row = new Vendor
            {
                Name = v.Name.Trim(), Website = v.Website, SupportPortalUrl = v.SupportPortalUrl, SupportPhone = v.SupportPhone, Notes = v.Notes,
                CreatedAt = now, UpdatedAt = now,
            };
            db.Vendors.Add(row);
            newVendors.Add((v, row));
        }
        var partnerMap = new Dictionary<int, int>();
        var newPartners = new List<(PfPartner File, TradingPartner Row)>();
        var existingPartners = await db.TradingPartners.ToListAsync();
        foreach (var p in file.Partners)
        {
            if (merge && existingPartners.FirstOrDefault(x => string.Equals(x.Name, p.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                partnerMap[p.Id] = found.Id;
                result.Skipped.Add($"Trading partner {p.Name} (already here)");
                continue;
            }
            var row = new TradingPartner
            {
                Name = p.Name.Trim(), Type = p.Type, Status = p.Status, Importance = p.Importance, AccountCode = p.AccountCode,
                EdiQualifier = p.EdiQualifier, EdiId = p.EdiId, GsId = p.GsId, Notes = p.Notes, CreatedAt = now, UpdatedAt = now,
            };
            db.TradingPartners.Add(row);
            newPartners.Add((p, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newVendors) vendorMap[f.Id] = row.Id;
        foreach (var (f, row) in newPartners) partnerMap[f.Id] = row.Id;
        result.Count("vendors", newVendors.Count);
        result.Count("trading partners", newPartners.Count);

        var contactMap = new Dictionary<int, int>();
        var newContacts = new List<(PfContact File, Contact Row)>();
        var existingContacts = await db.Contacts.AsNoTracking().ToListAsync();
        foreach (var c in file.Contacts)
        {
            var vendorId = Map(vendorMap, c.VendorId);
            var partnerId = Map(partnerMap, c.PartnerId);
            if (merge && existingContacts.FirstOrDefault(x => string.Equals(x.Name, c.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    && x.VendorId == vendorId && x.TradingPartnerId == partnerId) is { } found)
            {
                contactMap[c.Id] = found.Id;
                continue; // people are matched quietly: the same person in two files is the usual case
            }
            var row = new Contact
            {
                Name = c.Name.Trim(), Title = c.Title, Email = c.Email, Phone = c.Phone, VendorId = vendorId, TradingPartnerId = partnerId,
                Department = c.Department, Notes = c.Notes, IsActive = c.IsActive, CreatedAt = now, UpdatedAt = now,
            };
            db.Contacts.Add(row);
            newContacts.Add((c, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newContacts) contactMap[f.Id] = row.Id;
        result.Count("contacts", newContacts.Count);

        var contractMap = new Dictionary<int, int>();
        foreach (var (f, row) in newVendors)
        {
            row.AccountManagerId = Map(contactMap, f.AccountManagerId);
            row.TechnicalContactId = Map(contactMap, f.TechnicalContactId);
            foreach (var e in f.Escalation)
                if (Map(contactMap, e.ContactId) is { } cid && row.EscalationContacts.All(x => x.ContactId != cid))
                    row.EscalationContacts.Add(new VendorEscalationContact { VendorId = row.Id, ContactId = cid, Level = e.Level, Note = e.Note });
        }
        foreach (var (f, row) in newPartners) row.RelationshipOwnerId = Map(contactMap, f.RelationshipOwnerId);
        var newContracts = new List<(PfContract File, Contract Row)>();
        foreach (var (f, row) in newVendors)
            foreach (var c in f.Contracts)
            {
                var contract = new Contract
                {
                    VendorId = row.Id, Name = c.Name.Trim(), ContractNumber = c.ContractNumber, StartDate = c.StartDate, EndDate = c.EndDate,
                    RenewalDate = c.RenewalDate, NoticePeriodDays = c.NoticePeriodDays, AutoRenews = c.AutoRenews, AnnualValue = c.AnnualValue,
                    Notes = c.Notes, CreatedAt = now, UpdatedAt = now,
                };
                db.Contracts.Add(contract);
                newContracts.Add((c, contract));
            }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newContracts) contractMap[f.Id] = row.Id;
        result.Count("contracts", newContracts.Count);
        // Contracts of vendors that were already here: match by name.
        var existingContracts = await db.Contracts.AsNoTracking().ToListAsync();
        foreach (var v in file.Vendors.Where(v => !newVendors.Any(n => n.File == v)))
            foreach (var c in v.Contracts)
                if (existingContracts.FirstOrDefault(x => x.VendorId == vendorMap[v.Id] && string.Equals(x.Name, c.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
                    contractMap[c.Id] = found.Id;

        // Applications.
        var appMap = new Dictionary<int, int>();
        var newApps = new List<(PfApplication File, Application Row)>();
        var existingApps = await db.Applications.AsNoTracking().ToListAsync();
        var takenProducts = existingApps.Where(a => a.ProductId is not null).Select(a => a.ProductId!.Value).ToHashSet();
        foreach (var a in file.Applications)
        {
            if (merge && existingApps.FirstOrDefault(x => string.Equals(x.Name, a.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                appMap[a.Id] = found.Id;
                result.Skipped.Add($"Application {a.Name} (already here)");
                continue;
            }
            var vendorId = Map(vendorMap, a.VendorId);
            var contractId = Map(contractMap, a.ContractId);
            if (contractId is { } cid && existingContracts.Concat(newContracts.Select(n => n.Row)).FirstOrDefault(c => c.Id == cid)?.VendorId != vendorId)
                contractId = null;
            int? productId = null;
            if (a.Roadmap is { } roadmap)
            {
                productId = index.ProductOf(roadmap);
                if (productId is null) result.Unmatched.Add($"Roadmap of {a.Name}: {roadmap}");
                else if (!takenProducts.Add(productId.Value)) { result.Unmatched.Add($"Roadmap of {a.Name}: {roadmap} already belongs to another application"); productId = null; }
            }
            var row = new Application
            {
                Name = a.Name.Trim(), Description = a.Description, Category = a.Category, VendorId = vendorId, ContractId = contractId, ProductId = productId,
                BusinessOwnerId = Map(contactMap, a.BusinessOwnerId), TechnicalOwnerId = Map(contactMap, a.TechnicalOwnerId),
                Criticality = a.Criticality, Status = a.Status, Hosting = a.Hosting, AnnualCost = a.AnnualCost,
                BusinessFit = a.BusinessFit, TechnicalFit = a.TechnicalFit, GoLiveDate = a.GoLiveDate, RetireDate = a.RetireDate, Notes = a.Notes,
                CreatedAt = now, UpdatedAt = now,
            };
            db.Applications.Add(row);
            newApps.Add((a, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newApps)
        {
            appMap[f.Id] = row.Id;
            foreach (var sc in f.Support.DistinctBy(x => (x.ContactId, x.Role)))
                if (Map(contactMap, sc.ContactId) is { } cid)
                    db.ApplicationSupportContacts.Add(new ApplicationSupportContact { ApplicationId = row.Id, ContactId = cid, Role = sc.Role, Note = sc.Note });
            foreach (var l in f.Initiatives)
                if (Item(l.Item, $"Roadmap item linked to {f.Name}") is { } itemId && !db.ApplicationInitiatives.Local.Any(x => x.ApplicationId == row.Id && x.RoadmapItemId == itemId))
                    db.ApplicationInitiatives.Add(new ApplicationInitiative { ApplicationId = row.Id, RoadmapItemId = itemId, ImpactType = l.Impact });
        }
        await db.SaveChangesAsync();
        result.Count("applications", newApps.Count);

        // Integrations.
        var flowMap = new Dictionary<int, int>();
        var newFlows = new List<(PfIntegration File, Integration Row)>();
        var existingFlows = await db.Integrations.AsNoTracking().ToListAsync();
        foreach (var i in file.Integrations)
        {
            var row = new Integration
            {
                Name = i.Name.Trim(), SourceApplicationId = Map(appMap, i.SourceApplicationId), SourcePartnerId = Map(partnerMap, i.SourcePartnerId),
                TargetApplicationId = Map(appMap, i.TargetApplicationId), TargetPartnerId = Map(partnerMap, i.TargetPartnerId),
                ViaApplicationId = Map(appMap, i.ViaApplicationId), Type = i.Type, Direction = i.Direction, Frequency = i.Frequency,
                MessageFormat = i.MessageFormat, BusinessPurpose = i.BusinessPurpose, Criticality = i.Criticality, OwnerId = Map(contactMap, i.OwnerId),
                Status = i.Status, LastReviewDate = i.LastReviewDate, ReviewIntervalMonths = i.ReviewIntervalMonths, Notes = i.Notes,
                CreatedAt = now, UpdatedAt = now,
            };
            if (merge && existingFlows.FirstOrDefault(x => x.SourceApplicationId == row.SourceApplicationId && x.SourcePartnerId == row.SourcePartnerId
                    && x.TargetApplicationId == row.TargetApplicationId && x.TargetPartnerId == row.TargetPartnerId && x.Type == row.Type
                    && string.Equals(x.Name, row.Name, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                flowMap[i.Id] = found.Id;
                result.Skipped.Add($"Integration {i.Name} (already here)");
                continue;
            }
            foreach (var r in i.Risks)
                row.Risks.Add(new IntegrationRisk
                {
                    Description = r.Description, Likelihood = Math.Clamp(r.Likelihood, 1, 5), Impact = Math.Clamp(r.Impact, 1, 5), Mitigation = r.Mitigation,
                    Status = r.Status, OwnerId = Map(contactMap, r.OwnerId), RaisedOn = r.RaisedOn, CreatedAt = now, UpdatedAt = now,
                });
            db.Integrations.Add(row);
            newFlows.Add((i, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newFlows) flowMap[f.Id] = row.Id;
        result.Count("integrations", newFlows.Count);

        // Processes: rows first, then parents, links, pain points and KPIs.
        var processMap = new Dictionary<int, int>();
        var newProcesses = new List<(PfProcess File, BusinessProcess Row)>();
        var existingProcesses = await db.BusinessProcesses.AsNoTracking().ToListAsync();
        foreach (var p in file.Processes)
        {
            if (merge && existingProcesses.FirstOrDefault(x => string.Equals(x.Name, p.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                processMap[p.Id] = found.Id;
                result.Skipped.Add($"Process {p.Name} (already here)");
                continue;
            }
            var row = new BusinessProcess
            {
                Name = p.Name.Trim(), Description = p.Description, Domain = p.Domain, ProcessOwnerId = Map(contactMap, p.OwnerId),
                SortOrder = p.SortOrder, CreatedAt = now, UpdatedAt = now,
            };
            db.BusinessProcesses.Add(row);
            newProcesses.Add((p, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newProcesses) processMap[f.Id] = row.Id;
        var kpiMap = new Dictionary<int, int>();
        var newKpis = new List<(PfKpi File, ProcessKpi Row)>();
        var parentOf = existingProcesses.ToDictionary(p => p.Id, p => p.ParentProcessId);
        foreach (var (f, row) in newProcesses)
        {
            var parent = Map(processMap, f.ParentId);
            if (parent is { } pid && PortfolioRules.ParentError(row.Id, pid, parentOf) is null) { row.ParentProcessId = pid; parentOf[row.Id] = pid; }
            else parentOf[row.Id] = null;
            foreach (var a in f.Applications.DistinctBy(a => a.ApplicationId))
                if (Map(appMap, a.ApplicationId) is { } aid)
                    db.ProcessApplications.Add(new ProcessApplication { ProcessId = row.Id, ApplicationId = aid, Usage = a.Usage, IsPrimary = a.IsPrimary });
            foreach (var iid in f.IntegrationIds.Distinct())
                if (Map(flowMap, iid) is { } fid)
                    db.ProcessIntegrations.Add(new ProcessIntegration { ProcessId = row.Id, IntegrationId = fid });
            foreach (var pp in f.PainPoints)
                db.ProcessPainPoints.Add(new ProcessPainPoint
                {
                    ProcessId = row.Id, Description = pp.Description, Severity = pp.Severity, ApplicationId = Map(appMap, pp.ApplicationId),
                    AddressedByItemId = Item(pp.AddressedBy, $"Pain point fix in {f.Name}"), Status = pp.Status, CreatedAt = now, UpdatedAt = now,
                });
            foreach (var k in f.Kpis)
            {
                var kpi = new ProcessKpi
                {
                    ProcessId = row.Id, Name = k.Name, Unit = k.Unit, Direction = k.Direction, Baseline = k.Baseline, Target = k.Target,
                    Current = k.Current, MeasuredOn = k.MeasuredOn, CreatedAt = now, UpdatedAt = now,
                };
                db.ProcessKpis.Add(kpi);
                newKpis.Add((k, kpi));
            }
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newKpis) kpiMap[f.Id] = row.Id;
        result.Count("processes", newProcesses.Count);
        // KPIs of processes that were already here: match by name.
        var existingKpis = await db.ProcessKpis.AsNoTracking().ToListAsync();
        foreach (var p in file.Processes.Where(p => !newProcesses.Any(n => n.File == p)))
            foreach (var k in p.Kpis)
                if (existingKpis.FirstOrDefault(x => x.ProcessId == processMap[p.Id] && string.Equals(x.Name, k.Name, StringComparison.OrdinalIgnoreCase)) is { } found)
                    kpiMap[k.Id] = found.Id;

        // Benefits.
        var benefitMap = new Dictionary<int, int>();
        var newBenefits = new List<(PfBenefit File, Benefit Row)>();
        var existingBenefits = await db.Benefits.AsNoTracking().ToListAsync();
        foreach (var b in file.Benefits)
        {
            if (Item(b.Item, $"Benefit “{b.Description}”") is not { } itemId) continue;
            if (merge && existingBenefits.FirstOrDefault(x => x.RoadmapItemId == itemId && string.Equals(x.Description, b.Description.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                benefitMap[b.Id] = found.Id;
                result.Skipped.Add($"Benefit {b.Description} (already here)");
                continue;
            }
            var row = new Benefit
            {
                RoadmapItemId = itemId, ApplicationId = Map(appMap, b.ApplicationId), KpiId = Map(kpiMap, b.KpiId), Category = b.Category,
                Description = b.Description.Trim(), Unit = b.Unit, ExpectedValue = b.ExpectedValue, ExpectedBy = b.ExpectedBy, Baseline = b.Baseline,
                Notes = b.Notes, CreatedAt = now, UpdatedAt = now,
                Measurements = b.Measurements.DistinctBy(m => m.MeasuredOn)
                    .Select(m => new BenefitMeasurement { MeasuredOn = m.MeasuredOn, Value = m.Value, Source = m.Source, Note = m.Note, CreatedAt = now }).ToList(),
            };
            db.Benefits.Add(row);
            newBenefits.Add((b, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newBenefits) benefitMap[f.Id] = row.Id;
        result.Count("benefits", newBenefits.Count);

        // Decisions: rows and alternatives, then "superseded by" and links.
        var decisionMap = new Dictionary<int, int>();
        var newDecisions = new List<(PfDecision File, Decision Row)>();
        var existingDecisions = await db.Decisions.AsNoTracking().ToListAsync();
        foreach (var d in file.Decisions)
        {
            if (merge && existingDecisions.FirstOrDefault(x => x.DecidedOn == d.DecidedOn && string.Equals(x.Title, d.Title.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                decisionMap[d.Id] = found.Id;
                result.Skipped.Add($"Decision {d.Title} (already here)");
                continue;
            }
            var row = new Decision
            {
                Title = d.Title.Trim(), DecidedOn = d.DecidedOn, Status = d.Status, Context = d.Context, DecisionText = d.Decision,
                Outcome = d.Outcome, DecidedBy = d.DecidedBy, CreatedAt = now, UpdatedAt = now,
                Alternatives = d.Alternatives.Select((a, n) => new DecisionAlternative { Option = a.Option, Pros = a.Pros, Cons = a.Cons, WhyRejected = a.WhyRejected, SortOrder = n }).ToList(),
            };
            db.Decisions.Add(row);
            newDecisions.Add((d, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newDecisions) decisionMap[f.Id] = row.Id;
        foreach (var (f, row) in newDecisions)
        {
            row.SupersededById = Map(decisionMap, f.SupersededById);
            var targets = new HashSet<DecisionTarget>();
            foreach (var l in f.Links)
            {
                int? id = l.Kind switch
                {
                    DecisionTargetKind.Application => Map(appMap, l.Id),
                    DecisionTargetKind.Integration => Map(flowMap, l.Id),
                    DecisionTargetKind.Vendor => Map(vendorMap, l.Id),
                    DecisionTargetKind.Contract => Map(contractMap, l.Id),
                    DecisionTargetKind.Process => Map(processMap, l.Id),
                    DecisionTargetKind.TradingPartner => Map(partnerMap, l.Id),
                    _ => Item(l.Item, $"Decision “{f.Title}” link"),
                };
                if (id is { } x && targets.Add(new DecisionTarget(l.Kind, x))) db.DecisionLinks.Add(new DecisionTarget(l.Kind, x).ToLink(row.Id));
            }
        }
        await db.SaveChangesAsync();
        result.Count("decisions", newDecisions.Count);

        // Documents: the files themselves stay in SharePoint; this brings back the list and what each is attached to.
        var documentMap = new Dictionary<int, int>();
        var newDocuments = new List<(PfDocument File, PortfolioDocument Row)>();
        var existingDocuments = await db.Documents.AsNoTracking().ToListAsync();
        foreach (var d in file.Documents)
        {
            if (merge && existingDocuments.FirstOrDefault(x =>
                    (d.Url is not null && string.Equals(x.Url, d.Url, StringComparison.OrdinalIgnoreCase))
                    || (d.LocalPath is not null && string.Equals(x.LocalPath, d.LocalPath, StringComparison.OrdinalIgnoreCase))) is { } found)
            {
                documentMap[d.Id] = found.Id;
                result.Skipped.Add($"Document {d.Title} (already here)");
                continue;
            }
            var row = new PortfolioDocument
            {
                Title = d.Title.Trim(), Category = d.Category, FileName = d.FileName, LocalPath = d.LocalPath, Url = d.Url, SizeBytes = d.SizeBytes,
                DocumentDate = d.DocumentDate, Notes = d.Notes, CreatedAt = d.CreatedAt == default ? now : d.CreatedAt, UpdatedAt = now,
            };
            if (row.LocalPath is null && row.Url is null) continue;
            if (d.Product is not null)
            {
                row.ProductId = index.ProductOf(d.Product);
                if (row.ProductId is null) result.Unmatched.Add($"Document “{d.Title}”: roadmap {d.Product}");
            }
            switch (d.AttachedTo)
            {
                case DocumentTargetKind.Application: row.ApplicationId = Map(appMap, d.TargetId); break;
                case DocumentTargetKind.Vendor: row.VendorId = Map(vendorMap, d.TargetId); break;
                case DocumentTargetKind.Contract: row.ContractId = Map(contractMap, d.TargetId); break;
                case DocumentTargetKind.RoadmapItem:
                    row.RoadmapItemId = Item(d.Item, $"Document “{d.Title}”");
                    if (row.RoadmapItemId is { } ri) row.ProductId = await db.Items.Where(i => i.Id == ri).Select(i => (int?)i.ProductId).FirstAsync();
                    break;
            }
            db.Documents.Add(row);
            newDocuments.Add((d, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newDocuments) documentMap[f.Id] = row.Id;
        result.Count("documents", newDocuments.Count);

        // Feature comparisons: features (matched by category and name on a merge), their ratings, and added applications.
        var featureMap = new Dictionary<int, int>();
        var newFeatures = new List<(PfFeature File, CapabilityFeature Row)>();
        var existingFeatures = await db.CapabilityFeatures.AsNoTracking().ToListAsync();
        foreach (var f in file.Features)
        {
            if (merge && existingFeatures.FirstOrDefault(x => x.Category == f.Category && string.Equals(x.Name, f.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                featureMap[f.Id] = found.Id;
                result.Skipped.Add($"{PortfolioText.Label(f.Category)} feature {f.Name} (already here)");
                continue;
            }
            var row = new CapabilityFeature
            {
                Category = f.Category, Name = f.Name.Trim(), Description = f.Description, CreatedAt = now, UpdatedAt = now,
                SortOrder = f.SortOrder + (merge ? existingFeatures.Where(x => x.Category == f.Category).Select(x => x.SortOrder + 1).DefaultIfEmpty(0).Max() : 0),
            };
            db.CapabilityFeatures.Add(row);
            newFeatures.Add((f, row));
        }
        await db.SaveChangesAsync();
        foreach (var (f, row) in newFeatures) featureMap[f.Id] = row.Id;
        result.Count("features", newFeatures.Count);

        var appCategories = await db.Applications.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Category);
        var members = (await db.ComparisonMembers.AsNoTracking().ToListAsync()).Select(m => (m.Category, m.ApplicationId)).ToHashSet();
        foreach (var m in file.ComparisonMembers)
            if (Map(appMap, m.ApplicationId) is { } aid && appCategories.GetValueOrDefault(aid) != m.Category && members.Add((m.Category, aid)))
                db.ComparisonMembers.Add(new ComparisonMember { Category = m.Category, ApplicationId = aid });

        var rated = (await db.FeatureAssessments.AsNoTracking().Select(a => new { a.FeatureId, a.ApplicationId }).ToListAsync())
            .Select(a => (a.FeatureId, a.ApplicationId)).ToHashSet();
        var ratingCount = 0;
        foreach (var f in file.Features)
        {
            if (!featureMap.TryGetValue(f.Id, out var fid)) continue;
            foreach (var r in f.Ratings)
            {
                if (Map(appMap, r.ApplicationId) is not { } aid || !rated.Add((fid, aid))) continue;
                FeatureAvailability? av = r.Availability;
                FeatureStrength? st = r.Strength;
                string? note = r.Note;
                if (CapabilityRules.Normalize(ref av, ref st, ref note) is not null || CapabilityRules.IsEmpty(av, st, note)) continue;
                db.FeatureAssessments.Add(new FeatureAssessment { FeatureId = fid, ApplicationId = aid, Availability = av, Strength = st, Note = note, UpdatedAt = now });
                ratingCount++;
            }
        }
        await db.SaveChangesAsync();
        result.Count("feature ratings", ratingCount);

        // History, on a restore: re-pointed at the new ids. Entries for records that no longer exist keep id 0.
        if (!merge)
        {
            var maps = new Dictionary<PortfolioEntityType, Dictionary<int, int>>
            {
                [PortfolioEntityType.Application] = appMap, [PortfolioEntityType.Vendor] = vendorMap, [PortfolioEntityType.Contact] = contactMap,
                [PortfolioEntityType.Integration] = flowMap, [PortfolioEntityType.TradingPartner] = partnerMap, [PortfolioEntityType.Process] = processMap,
                [PortfolioEntityType.Benefit] = benefitMap, [PortfolioEntityType.Decision] = decisionMap,
                [PortfolioEntityType.Document] = documentMap, [PortfolioEntityType.Feature] = featureMap,
            };
            foreach (var h in file.History)
                db.PortfolioAuditEntries.Add(new PortfolioAuditEntry
                {
                    EntityType = h.EntityType, EntityId = maps.TryGetValue(h.EntityType, out var m) && m.TryGetValue(h.EntityId, out var id) ? id : 0,
                    EntityName = h.EntityName, Action = h.Action, Field = h.Field, OldValue = h.OldValue, NewValue = h.NewValue, ChangedAt = h.ChangedAt,
                });
            await db.SaveChangesAsync();
            result.Count("history entries", file.History.Count);
        }

        await tx.CommitAsync();
        return result;
    }
}
