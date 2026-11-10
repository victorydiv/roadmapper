using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

/// <summary>
/// Field-level history for portfolio records, the same way <see cref="AuditTrail"/> works for roadmap items:
/// capture before saving (original values are still known), resolve after (new rows have ids) and store
/// names instead of foreign keys so history stays readable after renames and deletes.
/// </summary>
internal static class PortfolioAuditTrail
{
    internal sealed record Pending(object Entity, AuditAction Action, string? Property, object? Old, object? New);

    private static readonly Dictionary<Type, Dictionary<string, string>> Fields = new()
    {
        [typeof(Application)] = new()
        {
            [nameof(Application.Name)] = "Name",
            [nameof(Application.Description)] = "Description",
            [nameof(Application.Category)] = "Category",
            [nameof(Application.VendorId)] = "Vendor",
            [nameof(Application.ContractId)] = "Contract",
            [nameof(Application.ProductId)] = "Roadmap",
            [nameof(Application.BusinessOwnerId)] = "Business owner",
            [nameof(Application.TechnicalOwnerId)] = "Technical owner",
            [nameof(Application.Criticality)] = "Criticality",
            [nameof(Application.Status)] = "Status",
            [nameof(Application.Hosting)] = "Hosting",
            [nameof(Application.AnnualCost)] = "Annual cost",
            [nameof(Application.BusinessFit)] = "Business fit",
            [nameof(Application.TechnicalFit)] = "Technical fit",
            [nameof(Application.GoLiveDate)] = "Go-live date",
            [nameof(Application.RetireDate)] = "Retire date",
            [nameof(Application.Notes)] = "Notes",
        },
        [typeof(Vendor)] = new()
        {
            [nameof(Vendor.Name)] = "Name",
            [nameof(Vendor.Website)] = "Website",
            [nameof(Vendor.AccountManagerId)] = "Account manager",
            [nameof(Vendor.TechnicalContactId)] = "Technical contact",
            [nameof(Vendor.SupportPortalUrl)] = "Support portal",
            [nameof(Vendor.SupportPhone)] = "Support phone",
            [nameof(Vendor.Notes)] = "Notes",
        },
        [typeof(Contact)] = new()
        {
            [nameof(Contact.Name)] = "Name",
            [nameof(Contact.Title)] = "Title",
            [nameof(Contact.Email)] = "Email",
            [nameof(Contact.Phone)] = "Phone",
            [nameof(Contact.VendorId)] = "Company",
            [nameof(Contact.TradingPartnerId)] = "Partner company",
            [nameof(Contact.Department)] = "Department",
            [nameof(Contact.IsActive)] = "Active",
            [nameof(Contact.Notes)] = "Notes",
        },
        [typeof(Contract)] = new()
        {
            [nameof(Contract.Name)] = "Name",
            [nameof(Contract.ContractNumber)] = "Number",
            [nameof(Contract.StartDate)] = "Start date",
            [nameof(Contract.EndDate)] = "End date",
            [nameof(Contract.RenewalDate)] = "Renewal date",
            [nameof(Contract.NoticePeriodDays)] = "Notice period (days)",
            [nameof(Contract.AutoRenews)] = "Auto-renews",
            [nameof(Contract.AnnualValue)] = "Annual value",
            [nameof(Contract.Notes)] = "Notes",
        },
        [typeof(ApplicationInitiative)] = new() { [nameof(ApplicationInitiative.ImpactType)] = "Impact" },
        [typeof(Integration)] = new()
        {
            [nameof(Integration.Name)] = "Name",
            [nameof(Integration.SourceApplicationId)] = "Source application",
            [nameof(Integration.SourcePartnerId)] = "Source partner",
            [nameof(Integration.TargetApplicationId)] = "Target application",
            [nameof(Integration.TargetPartnerId)] = "Target partner",
            [nameof(Integration.ViaApplicationId)] = "Via",
            [nameof(Integration.Type)] = "Type",
            [nameof(Integration.Direction)] = "Direction",
            [nameof(Integration.Frequency)] = "Frequency",
            [nameof(Integration.MessageFormat)] = "Message format",
            [nameof(Integration.BusinessPurpose)] = "Business purpose",
            [nameof(Integration.Criticality)] = "Criticality",
            [nameof(Integration.OwnerId)] = "Owner",
            [nameof(Integration.Status)] = "Status",
            [nameof(Integration.LastReviewDate)] = "Last review",
            [nameof(Integration.ReviewIntervalMonths)] = "Review interval (months)",
            [nameof(Integration.Notes)] = "Notes",
        },
        [typeof(TradingPartner)] = new()
        {
            [nameof(TradingPartner.Name)] = "Name",
            [nameof(TradingPartner.Type)] = "Type",
            [nameof(TradingPartner.Status)] = "Status",
            [nameof(TradingPartner.Importance)] = "Importance",
            [nameof(TradingPartner.AccountCode)] = "Account code",
            [nameof(TradingPartner.EdiQualifier)] = "EDI qualifier",
            [nameof(TradingPartner.EdiId)] = "EDI ID",
            [nameof(TradingPartner.GsId)] = "GS ID",
            [nameof(TradingPartner.RelationshipOwnerId)] = "Relationship owner",
            [nameof(TradingPartner.Notes)] = "Notes",
        },
        [typeof(BusinessProcess)] = new()
        {
            [nameof(BusinessProcess.Name)] = "Name",
            [nameof(BusinessProcess.Description)] = "Description",
            [nameof(BusinessProcess.Domain)] = "Domain",
            [nameof(BusinessProcess.ProcessOwnerId)] = "Process owner",
            [nameof(BusinessProcess.ParentProcessId)] = "Part of",
        },
        [typeof(ProcessApplication)] = new()
        {
            [nameof(ProcessApplication.Usage)] = "Usage",
            [nameof(ProcessApplication.IsPrimary)] = "Primary system",
        },
        [typeof(ProcessPainPoint)] = new()
        {
            [nameof(ProcessPainPoint.Description)] = "Description",
            [nameof(ProcessPainPoint.Severity)] = "Severity",
            [nameof(ProcessPainPoint.ApplicationId)] = "Application",
            [nameof(ProcessPainPoint.AddressedByItemId)] = "Addressed by",
            [nameof(ProcessPainPoint.Status)] = "Status",
        },
        [typeof(ProcessKpi)] = new()
        {
            [nameof(ProcessKpi.Name)] = "Name",
            [nameof(ProcessKpi.Unit)] = "Unit",
            [nameof(ProcessKpi.Direction)] = "Direction",
            [nameof(ProcessKpi.Baseline)] = "Baseline",
            [nameof(ProcessKpi.Target)] = "Target",
            [nameof(ProcessKpi.Current)] = "Current",
            [nameof(ProcessKpi.MeasuredOn)] = "Measured on",
        },
        [typeof(Benefit)] = new()
        {
            [nameof(Benefit.RoadmapItemId)] = "Roadmap item",
            [nameof(Benefit.ApplicationId)] = "Application",
            [nameof(Benefit.KpiId)] = "KPI",
            [nameof(Benefit.Category)] = "Category",
            [nameof(Benefit.Description)] = "Description",
            [nameof(Benefit.Unit)] = "Unit",
            [nameof(Benefit.ExpectedValue)] = "Expected value",
            [nameof(Benefit.ExpectedBy)] = "Expected by",
            [nameof(Benefit.Baseline)] = "Baseline",
            [nameof(Benefit.Notes)] = "Notes",
        },
        [typeof(BenefitMeasurement)] = new()
        {
            [nameof(BenefitMeasurement.Value)] = "Value",
            [nameof(BenefitMeasurement.Source)] = "Source",
            [nameof(BenefitMeasurement.Note)] = "Note",
        },
        [typeof(Decision)] = new()
        {
            [nameof(Decision.Title)] = "Title",
            [nameof(Decision.DecidedOn)] = "Decided on",
            [nameof(Decision.Status)] = "Status",
            [nameof(Decision.Context)] = "Context",
            [nameof(Decision.DecisionText)] = "Decision",
            [nameof(Decision.Outcome)] = "Outcome",
            [nameof(Decision.DecidedBy)] = "Decided by",
            [nameof(Decision.SupersededById)] = "Superseded by",
        },
        [typeof(PortfolioDocument)] = new()
        {
            [nameof(PortfolioDocument.Title)] = "Title",
            [nameof(PortfolioDocument.Category)] = "Type",
            [nameof(PortfolioDocument.FileName)] = "File",
            [nameof(PortfolioDocument.Url)] = "Link",
            [nameof(PortfolioDocument.DocumentDate)] = "Date",
            [nameof(PortfolioDocument.Notes)] = "Notes",
            [nameof(PortfolioDocument.ProductId)] = "Product",
            [nameof(PortfolioDocument.ApplicationId)] = "Application",
            [nameof(PortfolioDocument.VendorId)] = "Vendor",
            [nameof(PortfolioDocument.ContractId)] = "Contract",
            [nameof(PortfolioDocument.RoadmapItemId)] = "Roadmap item",
        },
        [typeof(CapabilityFeature)] = new()
        {
            [nameof(CapabilityFeature.Name)] = "Name",
            [nameof(CapabilityFeature.Description)] = "Description",
        },
        [typeof(FeatureAssessment)] = new()
        {
            [nameof(FeatureAssessment.Availability)] = "Availability",
            [nameof(FeatureAssessment.Strength)] = "Strength",
            [nameof(FeatureAssessment.Note)] = "Note",
        },
        [typeof(IntegrationRisk)] = new()
        {
            [nameof(IntegrationRisk.Description)] = "Description",
            [nameof(IntegrationRisk.Likelihood)] = "Likelihood",
            [nameof(IntegrationRisk.Impact)] = "Impact",
            [nameof(IntegrationRisk.Mitigation)] = "Mitigation",
            [nameof(IntegrationRisk.Status)] = "Status",
            [nameof(IntegrationRisk.OwnerId)] = "Owner",
        },
    };

    public static List<Pending> Capture(ChangeTracker tracker)
    {
        var pending = new List<Pending>();
        foreach (var entry in tracker.Entries())
        {
            var entity = entry.Entity;
            if (entity is not (Application or Vendor or Contact or Contract or ApplicationInitiative
                or ApplicationSupportContact or VendorEscalationContact or Integration or IntegrationRisk or TradingPartner
                or BusinessProcess or ProcessApplication or ProcessIntegration or ProcessPainPoint or ProcessKpi
                or Benefit or BenefitMeasurement or Decision or DecisionLink or PortfolioDocument
                or CapabilityFeature or FeatureAssessment or ComparisonMember)) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    pending.Add(new(entity, AuditAction.Created, null, null, null));
                    break;
                case EntityState.Deleted:
                    pending.Add(new(entity, AuditAction.Deleted, null, null, null));
                    break;
                case EntityState.Modified when Fields.TryGetValue(entity.GetType(), out var fields):
                    pending.AddRange(entry.Properties
                        .Where(p => p.IsModified && fields.ContainsKey(p.Metadata.Name) && !Equals(p.OriginalValue, p.CurrentValue))
                        .Select(p => new Pending(entity, AuditAction.Updated, p.Metadata.Name, p.OriginalValue, p.CurrentValue)));
                    break;
            }
        }
        return pending;
    }

    public static async Task<List<PortfolioAuditEntry>> ResolveAsync(RoadmapDbContext db, List<Pending> pending, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entries = new List<PortfolioAuditEntry>();
        foreach (var p in pending)
        {
            var entry = new PortfolioAuditEntry { Action = p.Action, ChangedAt = now };
            var field = p.Property is null ? null : Fields[p.Entity.GetType()][p.Property];
            switch (p.Entity)
            {
                case Application a:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Application, a.Id, a.Name, field);
                    break;
                case Vendor v:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Vendor, v.Id, v.Name, field);
                    break;
                case Contact c:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Contact, c.Id, c.Name, field);
                    break;
                case Contract k:
                    // Contracts are filed under their vendor.
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Vendor, k.VendorId, await VendorNameAsync(db, k.VendorId, ct));
                    entry.Field = field is null ? "Contract" : $"Contract “{k.Name}” · {field}";
                    if (p.Action == AuditAction.Created) entry.NewValue = k.Name;
                    if (p.Action == AuditAction.Deleted) entry.OldValue = k.Name;
                    break;
                case ApplicationInitiative ai:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Application, ai.ApplicationId, await AppNameAsync(db, ai.ApplicationId, ct));
                    var title = await ItemTitleAsync(db, ai.RoadmapItemId, ct);
                    entry.Field = field is null ? "Roadmap item" : $"Impact on “{title}”";
                    if (p.Action == AuditAction.Created) entry.NewValue = title;
                    if (p.Action == AuditAction.Deleted) entry.OldValue = title;
                    break;
                }
                case ApplicationSupportContact s:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Application, s.ApplicationId, await AppNameAsync(db, s.ApplicationId, ct));
                    entry.Field = "Support contact";
                    var who = $"{await ContactNameAsync(db, s.ContactId, ct)} ({PortfolioText.Label(s.Role)})";
                    if (p.Action == AuditAction.Created) entry.NewValue = who; else entry.OldValue = who;
                    break;
                }
                case VendorEscalationContact x:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Vendor, x.VendorId, await VendorNameAsync(db, x.VendorId, ct));
                    entry.Field = "Escalation contact";
                    var who = await ContactNameAsync(db, x.ContactId, ct);
                    if (p.Action == AuditAction.Created) entry.NewValue = who; else entry.OldValue = who;
                    break;
                }
                case TradingPartner tp:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.TradingPartner, tp.Id, tp.Name, field);
                    break;
                case Integration i:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Integration, i.Id, i.Name, field);
                    break;
                case IntegrationRisk r:
                {
                    // Risks are filed under their integration.
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Integration, r.IntegrationId, await IntegrationNameAsync(db, r.IntegrationId, ct));
                    var risk = Truncate(r.Description.Length > 80 ? r.Description[..77] + "…" : r.Description);
                    entry.Field = field is null ? "Risk" : $"Risk “{risk}” · {field}";
                    if (p.Action == AuditAction.Created) entry.NewValue = $"{risk} (score {r.Score})";
                    if (p.Action == AuditAction.Deleted) entry.OldValue = risk;
                    break;
                }
                case BusinessProcess bp:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Process, bp.Id, bp.Name, field);
                    break;
                // A process's applications, integrations, pain points and KPIs are filed under the process.
                case ProcessApplication pa:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Process, pa.ProcessId, await ProcessNameAsync(db, pa.ProcessId, ct));
                    var app = await AppNameAsync(db, pa.ApplicationId, ct);
                    entry.Field = field is null ? "Application used" : $"Use of {app} · {field}";
                    if (p.Action == AuditAction.Created) entry.NewValue = app;
                    if (p.Action == AuditAction.Deleted) entry.OldValue = app;
                    break;
                }
                case ProcessIntegration pi:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Process, pi.ProcessId, await ProcessNameAsync(db, pi.ProcessId, ct));
                    entry.Field = "Relies on integration";
                    var name = await IntegrationNameAsync(db, pi.IntegrationId, ct);
                    if (p.Action == AuditAction.Created) entry.NewValue = name; else entry.OldValue = name;
                    break;
                }
                case ProcessPainPoint pp:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Process, pp.ProcessId, await ProcessNameAsync(db, pp.ProcessId, ct));
                    var pain = Truncate(pp.Description.Length > 80 ? pp.Description[..77] + "…" : pp.Description);
                    entry.Field = field is null ? "Pain point" : $"Pain point “{pain}” · {field}";
                    if (p.Action == AuditAction.Created) entry.NewValue = $"{pain} ({pp.Severity})";
                    if (p.Action == AuditAction.Deleted) entry.OldValue = pain;
                    break;
                }
                case ProcessKpi k:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Process, k.ProcessId, await ProcessNameAsync(db, k.ProcessId, ct));
                    entry.Field = field is null ? "KPI" : $"KPI “{k.Name}” · {field}";
                    if (p.Action == AuditAction.Created) entry.NewValue = k.Name;
                    if (p.Action == AuditAction.Deleted) entry.OldValue = k.Name;
                    break;
                }
                case Benefit bn:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Benefit, bn.Id, Short(bn.Description), field);
                    break;
                case BenefitMeasurement m:
                {
                    // Measurements are filed under their benefit.
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Benefit, m.BenefitId, await BenefitNameAsync(db, m.BenefitId, ct));
                    var when = PortfolioText.Format(m.MeasuredOn);
                    entry.Field = field is null ? "Measurement" : $"Measurement {when} · {field}";
                    if (p.Action == AuditAction.Created) entry.NewValue = $"{when}: {PortfolioText.Format(m.Value)}";
                    if (p.Action == AuditAction.Deleted) entry.OldValue = $"{when}: {PortfolioText.Format(m.Value)}";
                    break;
                }
                case Decision d:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Decision, d.Id, d.Title, field);
                    break;
                case DecisionLink l:
                {
                    // Links are filed under their decision.
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Decision, l.DecisionId, await DecisionTitleAsync(db, l.DecisionId, ct));
                    entry.Field = "Linked to";
                    var what = $"{PortfolioText.Label(l.Target.Kind)}: {await TargetNameAsync(db, l, ct)}";
                    if (p.Action == AuditAction.Created) entry.NewValue = what; else entry.OldValue = what;
                    break;
                }
                case CapabilityFeature cf:
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Feature, cf.Id, $"{PortfolioText.Label(cf.Category)}: {cf.Name}");
                    entry.Field = field;
                    break;
                case FeatureAssessment fa:
                {
                    // Ratings are filed under the application.
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Application, fa.ApplicationId, await AppNameAsync(db, fa.ApplicationId, ct));
                    var feature = await FeatureNameAsync(db, fa.FeatureId, ct);
                    entry.Field = field is null ? $"Feature “{feature}”" : $"Feature “{feature}” · {field}";
                    var rating = string.Join(" · ", new[] { fa.Availability is { } av ? PortfolioText.Label(av) : null, fa.Strength is { } st ? $"strength {PortfolioText.Label(st)}" : null }.Where(x => x is not null));
                    if (p.Action == AuditAction.Created) entry.NewValue = rating;
                    if (p.Action == AuditAction.Deleted) entry.OldValue = rating;
                    break;
                }
                case ComparisonMember cm:
                {
                    (entry.EntityType, entry.EntityId, entry.EntityName) = (PortfolioEntityType.Application, cm.ApplicationId, await AppNameAsync(db, cm.ApplicationId, ct));
                    entry.Field = "Compared with";
                    var what = $"{PortfolioText.Label(cm.Category)} applications";
                    if (p.Action == AuditAction.Created) entry.NewValue = what; else entry.OldValue = what;
                    break;
                }
                case PortfolioDocument doc:
                    (entry.EntityType, entry.EntityId, entry.EntityName, entry.Field) = (PortfolioEntityType.Document, doc.Id, doc.Title, field);
                    break;
                default:
                    continue;
            }
            if (p.Action == AuditAction.Updated)
            {
                entry.OldValue = await FormatAsync(db, p.Property!, p.Old, ct);
                entry.NewValue = await FormatAsync(db, p.Property!, p.New, ct);
            }
            entry.EntityName = Truncate(entry.EntityName);
            entries.Add(entry);
        }
        return entries;
    }

    private static async Task<string?> FormatAsync(RoadmapDbContext db, string property, object? value, CancellationToken ct)
    {
        if (value is null) return null;
        var text = property switch
        {
            nameof(Application.VendorId) => await VendorNameAsync(db, (int)value, ct),
            nameof(Application.ContractId) => db.Contracts.Local.FirstOrDefault(c => c.Id == (int)value)?.Name
                ?? await db.Contracts.AsNoTracking().Where(c => c.Id == (int)value).Select(c => c.Name).FirstOrDefaultAsync(ct),
            nameof(Application.ProductId) => await db.Products.AsNoTracking().Where(p => p.Id == (int)value).Select(p => p.Name).FirstOrDefaultAsync(ct),
            nameof(Application.BusinessOwnerId) or nameof(Application.TechnicalOwnerId)
                or nameof(Vendor.AccountManagerId) or nameof(Vendor.TechnicalContactId)
                or nameof(Integration.OwnerId) or nameof(TradingPartner.RelationshipOwnerId)
                or nameof(BusinessProcess.ProcessOwnerId) => await ContactNameAsync(db, (int)value, ct),
            nameof(BusinessProcess.ParentProcessId) => await ProcessNameAsync(db, (int)value, ct),
            nameof(Decision.SupersededById) => await DecisionTitleAsync(db, (int)value, ct),
            nameof(ProcessPainPoint.AddressedByItemId) or nameof(Benefit.RoadmapItemId) => await ItemTitleAsync(db, (int)value, ct),
            nameof(Benefit.KpiId) => await db.ProcessKpis.AsNoTracking().Where(k => k.Id == (int)value).Select(k => k.Name).FirstOrDefaultAsync(ct),
            nameof(Integration.SourcePartnerId) or nameof(Integration.TargetPartnerId)
                or nameof(Contact.TradingPartnerId) => await PartnerNameAsync(db, (int)value, ct),
            nameof(Integration.SourceApplicationId) or nameof(Integration.TargetApplicationId)
                or nameof(Integration.ViaApplicationId) or nameof(ProcessPainPoint.ApplicationId) => await AppNameAsync(db, (int)value, ct),
            _ => PortfolioText.Format(value),
        };
        return Truncate(text ?? $"#{value}");
    }

    private static string? Truncate(string? text) => text is { Length: > 300 } ? text[..297] + "…" : text;

    private static async Task<string> AppNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.Applications.Local.FirstOrDefault(a => a.Id == id)?.Name
        ?? await db.Applications.AsNoTracking().Where(a => a.Id == id).Select(a => a.Name).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> VendorNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.Vendors.Local.FirstOrDefault(v => v.Id == id)?.Name
        ?? await db.Vendors.AsNoTracking().Where(v => v.Id == id).Select(v => v.Name).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> ContactNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.Contacts.Local.FirstOrDefault(c => c.Id == id)?.Name
        ?? await db.Contacts.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> PartnerNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.TradingPartners.Local.FirstOrDefault(p => p.Id == id)?.Name
        ?? await db.TradingPartners.AsNoTracking().Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> IntegrationNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.Integrations.Local.FirstOrDefault(i => i.Id == id)?.Name
        ?? await db.Integrations.AsNoTracking().Where(i => i.Id == id).Select(i => i.Name).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> DecisionTitleAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.Decisions.Local.FirstOrDefault(d => d.Id == id)?.Title
        ?? await db.Decisions.AsNoTracking().Where(d => d.Id == id).Select(d => d.Title).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> TargetNameAsync(RoadmapDbContext db, DecisionLink l, CancellationToken ct)
    {
        var t = l.Target;
        return t.Kind switch
        {
            DecisionTargetKind.Application => await AppNameAsync(db, t.Id, ct),
            DecisionTargetKind.Integration => await IntegrationNameAsync(db, t.Id, ct),
            DecisionTargetKind.Vendor => await VendorNameAsync(db, t.Id, ct),
            DecisionTargetKind.Contract => await db.Contracts.AsNoTracking().Where(c => c.Id == t.Id).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? $"#{t.Id}",
            DecisionTargetKind.Process => await ProcessNameAsync(db, t.Id, ct),
            DecisionTargetKind.RoadmapItem => await ItemTitleAsync(db, t.Id, ct),
            _ => await PartnerNameAsync(db, t.Id, ct),
        };
    }

    private static string Short(string text) => text.Length > 80 ? text[..77] + "…" : text;

    private static async Task<string> BenefitNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        Short(db.Benefits.Local.FirstOrDefault(b => b.Id == id)?.Description
            ?? await db.Benefits.AsNoTracking().Where(b => b.Id == id).Select(b => b.Description).FirstOrDefaultAsync(ct)
            ?? $"#{id}");

    private static async Task<string> ProcessNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.BusinessProcesses.Local.FirstOrDefault(p => p.Id == id)?.Name
        ?? await db.BusinessProcesses.AsNoTracking().Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> FeatureNameAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        db.CapabilityFeatures.Local.FirstOrDefault(f => f.Id == id)?.Name
        ?? await db.CapabilityFeatures.AsNoTracking().Where(f => f.Id == id).Select(f => f.Name).FirstOrDefaultAsync(ct)
        ?? $"#{id}";

    private static async Task<string> ItemTitleAsync(RoadmapDbContext db, int id, CancellationToken ct) =>
        await db.Items.AsNoTracking().Where(i => i.Id == id).Select(i => i.Title).FirstOrDefaultAsync(ct) ?? $"#{id}";
}
