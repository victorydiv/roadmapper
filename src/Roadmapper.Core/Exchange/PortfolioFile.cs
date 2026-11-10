using System.Text.Json;
using System.Text.Json.Serialization;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Core.Exchange;

/// <summary>
/// The whole application portfolio as one JSON document: the ".rpf" file, the portfolio's counterpart to the
/// ".rmp" roadmap file. Records refer to each other by <c>id</c>; ids are local to the file and replaced on import.
/// Links into roadmaps (an application's own roadmap, initiative links, benefits, pain points, decision links) are
/// stored as product name + item path, e.g. "Customer Portal" + "Grow revenue › Self-service tracking", and re-linked
/// when a matching roadmap exists.
/// </summary>
public sealed class PortfolioFile
{
    public const string FormatName = "roadmapper-portfolio";
    public const int CurrentVersion = 1;
    public const string Extension = ".rpf";
    public const string ContentType = "application/json";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string? AppVersion { get; set; }

    public PfSettings? Settings { get; set; }
    public List<PfContact> Contacts { get; set; } = [];
    public List<PfVendor> Vendors { get; set; } = [];
    public List<PfPartner> Partners { get; set; } = [];
    public List<PfApplication> Applications { get; set; } = [];
    public List<PfIntegration> Integrations { get; set; } = [];
    public List<PfProcess> Processes { get; set; } = [];
    public List<PfBenefit> Benefits { get; set; } = [];
    public List<PfDecision> Decisions { get; set; } = [];
    public List<PfDocument> Documents { get; set; } = [];
    public List<PfHistory> History { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Reads and checks a file; throws <see cref="InvalidOperationException"/> with a user-facing message when it can't be used.</summary>
    public static PortfolioFile Read(Stream stream)
    {
        PortfolioFile? file;
        try { file = JsonSerializer.Deserialize<PortfolioFile>(stream, Json); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"This isn't a readable portfolio file{(ex.LineNumber is { } line ? $" (problem near line {line + 1})" : "")}.");
        }
        if (file is null) throw new InvalidOperationException("This file is empty.");
        if (file.Validate() is { Count: > 0 } errors)
            throw new InvalidOperationException(errors.Count == 1
                ? errors[0]
                : $"This file has {errors.Count} problems: {string.Join(" ", errors.Take(5))}{(errors.Count > 5 ? " …" : "")}");
        return file;
    }

    /// <summary>Every reason the file can't be imported; empty when it's fine.</summary>
    public List<string> Validate()
    {
        if (string.Equals(Format, RoadmapFile.FormatName, StringComparison.OrdinalIgnoreCase))
            return ["This is a roadmap (.rmp) file. Open it from the Products page instead."];
        if (!string.Equals(Format, FormatName, StringComparison.OrdinalIgnoreCase)) return ["This isn't a Roadmapper portfolio file."];
        if (Version > CurrentVersion) return ["This file was made by a newer version of Roadmapper. Update the app to open it."];
        if (Version < 1) return ["This file's version number isn't valid."];

        // Null lists can come from hand-edited or older files; treat them as empty.
        Contacts ??= []; Vendors ??= []; Partners ??= []; Applications ??= []; Integrations ??= []; Processes ??= [];
        Benefits ??= []; Decisions ??= []; Documents ??= []; History ??= [];
        if (Settings is not null) Settings.DocumentFolders ??= [];
        foreach (var v in Vendors) { v.Contracts ??= []; v.Escalation ??= []; }
        foreach (var a in Applications) { a.Support ??= []; a.Initiatives ??= []; }
        foreach (var i in Integrations) i.Risks ??= [];
        foreach (var p in Processes) { p.Applications ??= []; p.IntegrationIds ??= []; p.PainPoints ??= []; p.Kpis ??= []; }
        foreach (var b in Benefits) b.Measurements ??= [];
        foreach (var d in Decisions) { d.Alternatives ??= []; d.Links ??= []; }

        var errors = new List<string>();
        void Unique(IEnumerable<int> ids, string what)
        {
            if (ids.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1) is { } dup) errors.Add($"Two {what} records share id {dup.Key}.");
        }
        void Named(IEnumerable<string?> names, string what)
        {
            if (names.Any(string.IsNullOrWhiteSpace)) errors.Add($"A {what} has no name.");
        }
        Unique(Contacts.Select(c => c.Id), "contact");
        Unique(Vendors.Select(v => v.Id), "vendor");
        Unique(Vendors.SelectMany(v => v.Contracts).Select(c => c.Id), "contract");
        Unique(Partners.Select(p => p.Id), "trading partner");
        Unique(Applications.Select(a => a.Id), "application");
        Unique(Integrations.Select(i => i.Id), "integration");
        Unique(Processes.Select(p => p.Id), "process");
        Unique(Processes.SelectMany(p => p.Kpis).Select(k => k.Id), "KPI");
        Unique(Decisions.Select(d => d.Id), "decision");
        Named(Contacts.Select(c => c.Name), "contact");
        Named(Vendors.Select(v => v.Name), "vendor");
        Named(Partners.Select(p => p.Name), "trading partner");
        Named(Applications.Select(a => a.Name), "application");
        Named(Processes.Select(p => p.Name), "process");
        Named(Decisions.Select(d => d.Title), "decision");
        Named(Documents.Select(d => d.Title), "document");
        if (errors.Count > 0) return errors;

        var contacts = Contacts.Select(c => c.Id).ToHashSet();
        var vendors = Vendors.Select(v => v.Id).ToHashSet();
        var contracts = Vendors.SelectMany(v => v.Contracts).Select(c => c.Id).ToHashSet();
        var partners = Partners.Select(p => p.Id).ToHashSet();
        var apps = Applications.Select(a => a.Id).ToHashSet();
        var flows = Integrations.Select(i => i.Id).ToHashSet();
        var processes = Processes.Select(p => p.Id).ToHashSet();
        var kpis = Processes.SelectMany(p => p.Kpis).Select(k => k.Id).ToHashSet();
        var decisions = Decisions.Select(d => d.Id).ToHashSet();
        void Ref(int? id, HashSet<int> known, string what, string where)
        {
            if (id is { } x && !known.Contains(x)) errors.Add($"{where} refers to {what} {x}, which isn't in the file.");
        }

        foreach (var c in Contacts)
        {
            Ref(c.VendorId, vendors, "vendor", $"Contact “{c.Name}”");
            Ref(c.PartnerId, partners, "trading partner", $"Contact “{c.Name}”");
        }
        foreach (var v in Vendors)
        {
            Ref(v.AccountManagerId, contacts, "contact", $"Vendor “{v.Name}”");
            Ref(v.TechnicalContactId, contacts, "contact", $"Vendor “{v.Name}”");
            foreach (var e in v.Escalation) Ref(e.ContactId, contacts, "contact", $"Vendor “{v.Name}”");
        }
        foreach (var p in Partners) Ref(p.RelationshipOwnerId, contacts, "contact", $"Partner “{p.Name}”");
        foreach (var a in Applications)
        {
            var where = $"Application “{a.Name}”";
            Ref(a.VendorId, vendors, "vendor", where);
            Ref(a.ContractId, contracts, "contract", where);
            Ref(a.BusinessOwnerId, contacts, "contact", where);
            Ref(a.TechnicalOwnerId, contacts, "contact", where);
            foreach (var s in a.Support) Ref(s.ContactId, contacts, "contact", where);
        }
        foreach (var i in Integrations)
        {
            var where = $"Integration “{i.Name}”";
            if ((i.SourceApplicationId is null) == (i.SourcePartnerId is null) || (i.TargetApplicationId is null) == (i.TargetPartnerId is null))
                errors.Add($"{where} needs exactly one source and one target.");
            Ref(i.SourceApplicationId, apps, "application", where);
            Ref(i.TargetApplicationId, apps, "application", where);
            Ref(i.ViaApplicationId, apps, "application", where);
            Ref(i.SourcePartnerId, partners, "trading partner", where);
            Ref(i.TargetPartnerId, partners, "trading partner", where);
            Ref(i.OwnerId, contacts, "contact", where);
            foreach (var r in i.Risks) Ref(r.OwnerId, contacts, "contact", where);
        }
        foreach (var p in Processes)
        {
            var where = $"Process “{p.Name}”";
            Ref(p.ParentId, processes, "process", where);
            Ref(p.OwnerId, contacts, "contact", where);
            foreach (var a in p.Applications) Ref(a.ApplicationId, apps, "application", where);
            foreach (var i in p.IntegrationIds) Ref(i, flows, "integration", where);
            foreach (var pp in p.PainPoints) Ref(pp.ApplicationId, apps, "application", where);
        }
        foreach (var b in Benefits)
        {
            Ref(b.ApplicationId, apps, "application", $"Benefit “{b.Description}”");
            Ref(b.KpiId, kpis, "KPI", $"Benefit “{b.Description}”");
        }
        foreach (var d in Documents)
        {
            var where = $"Document “{d.Title}”";
            if (d.FileName is null && d.Url is null) errors.Add($"{where} has neither a file nor a link.");
            switch (d.AttachedTo)
            {
                case DocumentTargetKind.Application: Ref(d.TargetId ?? 0, apps, "application", where); break;
                case DocumentTargetKind.Vendor: Ref(d.TargetId ?? 0, vendors, "vendor", where); break;
                case DocumentTargetKind.Contract: Ref(d.TargetId ?? 0, contracts, "contract", where); break;
                case DocumentTargetKind.RoadmapItem when d.Item is null: errors.Add($"{where} is attached to a roadmap item with no path."); break;
            }
        }
        foreach (var d in Decisions)
        {
            var where = $"Decision “{d.Title}”";
            Ref(d.SupersededById, decisions, "decision", where);
            foreach (var l in d.Links)
            {
                var known = l.Kind switch
                {
                    DecisionTargetKind.Application => apps, DecisionTargetKind.Integration => flows, DecisionTargetKind.Vendor => vendors,
                    DecisionTargetKind.Contract => contracts, DecisionTargetKind.Process => processes, DecisionTargetKind.TradingPartner => partners,
                    _ => null,
                };
                if (known is null) { if (l.Item is null) errors.Add($"{where} has a roadmap link with no item."); }
                else Ref(l.Id, known, PortfolioText.Label(l.Kind).ToLowerInvariant(), where);
            }
        }
        return errors;
    }
}

/// <summary>A roadmap item by product name and path: the titles of its ancestors and itself, joined with " › ".</summary>
public sealed record PfRoadmapRef(string Product, string Path)
{
    public override string ToString() => $"{Product}: {Path}";
}

public sealed class PfSettings
{
    public decimal? LaborRate { get; set; }
    public int RenewalWarningDays { get; set; } = 120;
    public int DefaultReviewIntervalMonths { get; set; } = 12;
    public string? JiraBaseUrl { get; set; }
    public List<PfDocumentFolder> DocumentFolders { get; set; } = [];
}

/// <summary>
/// Where one document type is kept, for the product named <see cref="Product"/> or shared (null). The local path is
/// this PC's; on another PC it only works if that folder exists there too.
/// </summary>
public sealed record PfDocumentFolder(DocumentCategory Category, string? LocalPath, string? SharePointUrl, string? Product = null);

public sealed class PfContact
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int? VendorId { get; set; }
    public int? PartnerId { get; set; }
    public string? Department { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PfVendor
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Website { get; set; }
    public int? AccountManagerId { get; set; }
    public int? TechnicalContactId { get; set; }
    public string? SupportPortalUrl { get; set; }
    public string? SupportPhone { get; set; }
    public string? Notes { get; set; }
    public List<PfContract> Contracts { get; set; } = [];
    public List<PfEscalation> Escalation { get; set; } = [];
}

public sealed class PfContract
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? ContractNumber { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? RenewalDate { get; set; }
    public int? NoticePeriodDays { get; set; }
    public bool AutoRenews { get; set; }
    public decimal? AnnualValue { get; set; }
    public string? Notes { get; set; }
}

public sealed record PfEscalation(int ContactId, int Level, string? Note);

public sealed class PfPartner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public PartnerType Type { get; set; }
    public PartnerStatus Status { get; set; }
    public PartnerImportance Importance { get; set; }
    public string? AccountCode { get; set; }
    public string? EdiQualifier { get; set; }
    public string? EdiId { get; set; }
    public string? GsId { get; set; }
    public int? RelationshipOwnerId { get; set; }
    public string? Notes { get; set; }
}

public sealed class PfApplication
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public AppCategory Category { get; set; }
    public int? VendorId { get; set; }
    public int? ContractId { get; set; }
    /// <summary>The name of the application's own roadmap (product).</summary>
    public string? Roadmap { get; set; }
    public int? BusinessOwnerId { get; set; }
    public int? TechnicalOwnerId { get; set; }
    public Criticality Criticality { get; set; }
    public LifecycleStatus Status { get; set; }
    public Hosting Hosting { get; set; }
    public decimal? AnnualCost { get; set; }
    public int? BusinessFit { get; set; }
    public int? TechnicalFit { get; set; }
    public DateOnly? GoLiveDate { get; set; }
    public DateOnly? RetireDate { get; set; }
    public string? Notes { get; set; }
    public List<PfSupport> Support { get; set; } = [];
    public List<PfInitiativeLink> Initiatives { get; set; } = [];
}

public sealed record PfSupport(int ContactId, SupportRole Role, string? Note);

public sealed record PfInitiativeLink(PfRoadmapRef Item, InitiativeImpact Impact);

public sealed class PfIntegration
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? SourceApplicationId { get; set; }
    public int? SourcePartnerId { get; set; }
    public int? TargetApplicationId { get; set; }
    public int? TargetPartnerId { get; set; }
    public int? ViaApplicationId { get; set; }
    public IntegrationType Type { get; set; }
    public FlowDirection Direction { get; set; }
    public IntegrationFrequency Frequency { get; set; }
    public string? MessageFormat { get; set; }
    public string BusinessPurpose { get; set; } = "";
    public Criticality Criticality { get; set; }
    public int? OwnerId { get; set; }
    public LifecycleStatus Status { get; set; }
    public DateOnly? LastReviewDate { get; set; }
    public int ReviewIntervalMonths { get; set; } = 12;
    public string? Notes { get; set; }
    public List<PfRisk> Risks { get; set; } = [];
}

public sealed class PfRisk
{
    public string Description { get; set; } = "";
    public int Likelihood { get; set; }
    public int Impact { get; set; }
    public string? Mitigation { get; set; }
    public RiskStatus Status { get; set; }
    public int? OwnerId { get; set; }
    public DateOnly? RaisedOn { get; set; }
}

public sealed class PfProcess
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ProcessDomain Domain { get; set; }
    public int? OwnerId { get; set; }
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    public List<PfProcessApp> Applications { get; set; } = [];
    public List<int> IntegrationIds { get; set; } = [];
    public List<PfPainPoint> PainPoints { get; set; } = [];
    public List<PfKpi> Kpis { get; set; } = [];
}

public sealed record PfProcessApp(int ApplicationId, string? Usage, bool IsPrimary);

public sealed class PfPainPoint
{
    public string Description { get; set; } = "";
    public PainSeverity Severity { get; set; }
    public int? ApplicationId { get; set; }
    public PfRoadmapRef? AddressedBy { get; set; }
    public PainPointStatus Status { get; set; }
}

public sealed class PfKpi
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public KpiDirection Direction { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? Target { get; set; }
    public decimal? Current { get; set; }
    public DateOnly? MeasuredOn { get; set; }
}

public sealed class PfBenefit
{
    public int Id { get; set; }
    public PfRoadmapRef Item { get; set; } = new("", "");
    public int? ApplicationId { get; set; }
    public int? KpiId { get; set; }
    public BenefitCategory Category { get; set; }
    public string Description { get; set; } = "";
    public BenefitUnit Unit { get; set; }
    public decimal? ExpectedValue { get; set; }
    public DateOnly? ExpectedBy { get; set; }
    public decimal? Baseline { get; set; }
    public string? Notes { get; set; }
    public List<PfMeasurement> Measurements { get; set; } = [];
}

public sealed record PfMeasurement(DateOnly MeasuredOn, decimal Value, string? Source, string? Note);

public sealed class PfDecision
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateOnly DecidedOn { get; set; }
    public DecisionStatus Status { get; set; }
    public string? Context { get; set; }
    public string Decision { get; set; } = "";
    public string? Outcome { get; set; }
    public string? DecidedBy { get; set; }
    public int? SupersededById { get; set; }
    public List<PfAlternative> Alternatives { get; set; } = [];
    public List<PfDecisionLink> Links { get; set; } = [];
}

public sealed record PfAlternative(string Option, string? Pros, string? Cons, string? WhyRejected);

/// <summary>A decision link: <see cref="Id"/> for portfolio records, <see cref="Item"/> for roadmap items.</summary>
public sealed record PfDecisionLink(DecisionTargetKind Kind, int? Id, PfRoadmapRef? Item);

public sealed record PfHistory(PortfolioEntityType EntityType, int EntityId, string? EntityName, AuditAction Action, string? Field,
    string? OldValue, string? NewValue, DateTime ChangedAt);

/// <summary>
/// A document: a saved file (<see cref="FileName"/>, <see cref="LocalPath"/>) and/or its SharePoint <see cref="Url"/>.
/// <see cref="Product"/> is the name of the product it belongs to (null: shared). Attached by <see cref="TargetId"/>
/// (application, vendor, contract) or <see cref="Item"/> (roadmap item).
/// </summary>
public sealed class PfDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DocumentCategory Category { get; set; }
    public string? FileName { get; set; }
    public string? LocalPath { get; set; }
    public string? Url { get; set; }
    public long? SizeBytes { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? Notes { get; set; }
    public DocumentTargetKind? AttachedTo { get; set; }
    public int? TargetId { get; set; }
    public string? Product { get; set; }
    public PfRoadmapRef? Item { get; set; }
    public DateTime CreatedAt { get; set; }
}
