namespace Roadmapper.Core.Portfolio;

/// <summary>A person: someone at a vendor (<see cref="VendorId"/>), at a trading partner (<see cref="TradingPartnerId"/>), or internal staff when both are null.</summary>
public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public int? TradingPartnerId { get; set; }
    public TradingPartner? TradingPartner { get; set; }
    /// <summary>Internal staff only, e.g. "Ops – Memphis DC".</summary>
    public string? Department { get; set; }
    public string? Notes { get; set; }
    /// <summary>Cleared when someone leaves, so their history and links still read correctly.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsInternal => VendorId is null && TradingPartnerId is null;

    /// <summary>The company name, or null for internal staff (needs Vendor / TradingPartner loaded).</summary>
    public string? CompanyName => Vendor?.Name ?? TradingPartner?.Name;
}

/// <summary>A company we buy software or services from. Contract dates live on its <see cref="Contracts"/>.</summary>
public class Vendor
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Website { get; set; }
    public int? AccountManagerId { get; set; }
    public Contact? AccountManager { get; set; }
    public int? TechnicalContactId { get; set; }
    public Contact? TechnicalContact { get; set; }
    public string? SupportPortalUrl { get; set; }
    public string? SupportPhone { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Contract> Contracts { get; set; } = new();
    /// <summary>The vendor's own people.</summary>
    public List<Contact> Staff { get; set; } = new();
    /// <summary>Who to call, in order, when support isn't moving.</summary>
    public List<VendorEscalationContact> EscalationContacts { get; set; } = new();
    public List<Application> Applications { get; set; } = new();
}

/// <summary>One step of a vendor's escalation path. Level 1 is called first.</summary>
public class VendorEscalationContact
{
    public int VendorId { get; set; }
    public int ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int Level { get; set; }
    public string? Note { get; set; }
}

public class Contract
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public string Name { get; set; } = "";
    public string? ContractNumber { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? RenewalDate { get; set; }
    /// <summary>Days' notice the vendor needs before <see cref="RenewalDate"/> to cancel or renegotiate.</summary>
    public int? NoticePeriodDays { get; set; }
    public bool AutoRenews { get; set; }
    public decimal? AnnualValue { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The last day to give notice: the renewal date less the notice period.</summary>
    public DateOnly? NoticeDeadline => PortfolioRules.NoticeDeadline(RenewalDate, NoticePeriodDays);
}

/// <summary>An enterprise application in the portfolio. Its renewal date comes from its <see cref="Contract"/>.</summary>
public class Application
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public AppCategory Category { get; set; } = AppCategory.Other;
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    /// <summary>The contract that covers this application; always one of <see cref="Vendor"/>'s.</summary>
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    /// <summary>This application's own roadmap, if it has one.</summary>
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? BusinessOwnerId { get; set; }
    public Contact? BusinessOwner { get; set; }
    public int? TechnicalOwnerId { get; set; }
    public Contact? TechnicalOwner { get; set; }
    public Criticality Criticality { get; set; } = Criticality.Tier2;
    public LifecycleStatus Status { get; set; } = LifecycleStatus.Active;
    public Hosting Hosting { get; set; } = Hosting.SaaS;
    /// <summary>This application's share of the cost per year (one contract can cover several applications).</summary>
    public decimal? AnnualCost { get; set; }
    /// <summary>1–5: how well it serves the business.</summary>
    public int? BusinessFit { get; set; }
    /// <summary>1–5: how sound it is technically (supportability, integration, security).</summary>
    public int? TechnicalFit { get; set; }
    public DateOnly? GoLiveDate { get; set; }
    public DateOnly? RetireDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ApplicationSupportContact> SupportContacts { get; set; } = new();
    public List<ApplicationInitiative> Initiatives { get; set; } = new();

    public DateOnly? RenewalDate => Contract?.RenewalDate;
    public DateOnly? NoticeDeadline => Contract?.NoticeDeadline;
    public TimeQuadrant? TimeQuadrant => PortfolioRules.Quadrant(BusinessFit, TechnicalFit);
}

public class ApplicationSupportContact
{
    public int ApplicationId { get; set; }
    public int ContactId { get; set; }
    public Contact? Contact { get; set; }
    public SupportRole Role { get; set; }
    public string? Note { get; set; }
}

/// <summary>Links an application to a roadmap item (usually an Initiative) in any product. Nothing about the item is copied.</summary>
public class ApplicationInitiative
{
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    public int RoadmapItemId { get; set; }
    public RoadmapItem? RoadmapItem { get; set; }
    public InitiativeImpact ImpactType { get; set; } = InitiativeImpact.Enhances;
}

/// <summary>
/// A customer, carrier, supplier or other outside company that exchanges data with your applications. Partners
/// are the far ends of integrations: you track who they are and how to reach them, not their systems.
/// </summary>
public class TradingPartner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public PartnerType Type { get; set; } = PartnerType.Customer;
    public PartnerStatus Status { get; set; } = PartnerStatus.Active;
    public PartnerImportance Importance { get; set; } = PartnerImportance.Standard;
    /// <summary>Your ID for them, e.g. the customer code in the WMS or ERP.</summary>
    public string? AccountCode { get; set; }
    /// <summary>EDI interchange (ISA) ID qualifier, e.g. "01", "12", "ZZ".</summary>
    public string? EdiQualifier { get; set; }
    /// <summary>EDI interchange (ISA) ID.</summary>
    public string? EdiId { get; set; }
    /// <summary>EDI application (GS) ID, when it differs from the ISA ID.</summary>
    public string? GsId { get; set; }
    /// <summary>Who owns the relationship on your side: account manager, CS lead.</summary>
    public int? RelationshipOwnerId { get; set; }
    public Contact? RelationshipOwner { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The partner's own people (IT, EDI coordinators).</summary>
    public List<Contact> Staff { get; set; } = new();

    /// <summary>"01:123456789", or just the ID when there's no qualifier.</summary>
    public string? EdiAddress => EdiId is null ? null : EdiQualifier is null ? EdiId : $"{EdiQualifier}:{EdiId}";
}

/// <summary>
/// A flow of data from a source to a target, optionally through a middle hop (<see cref="Via"/>: an EDI VAN, iPaaS
/// or middleware application). Each end is one of your applications or a trading partner, and at least one end is
/// an application.
/// </summary>
public class Integration
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Set when the source is an application; otherwise <see cref="SourcePartnerId"/> is.</summary>
    public int? SourceApplicationId { get; set; }
    public Application? Source { get; set; }
    public int? SourcePartnerId { get; set; }
    public TradingPartner? SourcePartner { get; set; }
    /// <summary>Set when the target is an application; otherwise <see cref="TargetPartnerId"/> is.</summary>
    public int? TargetApplicationId { get; set; }
    public Application? Target { get; set; }
    public int? TargetPartnerId { get; set; }
    public TradingPartner? TargetPartner { get; set; }
    public int? ViaApplicationId { get; set; }
    public Application? Via { get; set; }
    public IntegrationType Type { get; set; } = IntegrationType.API;
    public FlowDirection Direction { get; set; } = FlowDirection.OneWay;
    public IntegrationFrequency Frequency { get; set; } = IntegrationFrequency.RealTime;
    /// <summary>e.g. "X12 940", "JSON", "CSV".</summary>
    public string? MessageFormat { get; set; }
    public string BusinessPurpose { get; set; } = "";
    public Criticality Criticality { get; set; } = Criticality.Tier2;
    public int? OwnerId { get; set; }
    public Contact? Owner { get; set; }
    public LifecycleStatus Status { get; set; } = LifecycleStatus.Active;
    public DateOnly? LastReviewDate { get; set; }
    public int ReviewIntervalMonths { get; set; } = 12;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<IntegrationRisk> Risks { get; set; } = new();

    /// <summary>When the next review is due; null when it has never been reviewed.</summary>
    public DateOnly? NextReviewDate => LastReviewDate?.AddMonths(ReviewIntervalMonths);

    public NodeRef SourceNode => NodeRef.Of(SourceApplicationId, SourcePartnerId);
    public NodeRef TargetNode => NodeRef.Of(TargetApplicationId, TargetPartnerId);
    public NodeRef? ViaNode => ViaApplicationId is { } v ? NodeRef.App(v) : null;
    /// <summary>Needs Source / SourcePartner loaded.</summary>
    public string SourceName => Source?.Name ?? SourcePartner?.Name ?? "";
    /// <summary>Needs Target / TargetPartner loaded.</summary>
    public string TargetName => Target?.Name ?? TargetPartner?.Name ?? "";
    public bool InvolvesPartner => SourcePartnerId is not null || TargetPartnerId is not null;
}

/// <summary>A risk on an integration, scored likelihood × impact (each 1–5).</summary>
public class IntegrationRisk
{
    public int Id { get; set; }
    public int IntegrationId { get; set; }
    public Integration? Integration { get; set; }
    public string Description { get; set; } = "";
    public int Likelihood { get; set; } = 3;
    public int Impact { get; set; } = 3;
    public string? Mitigation { get; set; }
    public RiskStatus Status { get; set; } = RiskStatus.Open;
    public int? OwnerId { get; set; }
    public Contact? Owner { get; set; }
    public DateOnly? RaisedOn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>1–25.</summary>
    public int Score => Likelihood * Impact;
    public bool IsActive => Status is RiskStatus.Open or RiskStatus.Accepted;
}

/// <summary>
/// Something the operation does, e.g. "Order to Cash" with sub-processes "Order intake" and "Outbound shipping".
/// Processes say which applications they use and which integrations they rely on, so a change to either can be
/// traced to the work it affects.
/// </summary>
public class BusinessProcess
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ProcessDomain Domain { get; set; } = ProcessDomain.Other;
    public int? ProcessOwnerId { get; set; }
    public Contact? ProcessOwner { get; set; }
    /// <summary>The process this is a step of; null for a top-level (L1) process.</summary>
    public int? ParentProcessId { get; set; }
    public BusinessProcess? ParentProcess { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<BusinessProcess> Children { get; set; } = new();
    public List<ProcessApplication> Applications { get; set; } = new();
    public List<ProcessIntegration> Integrations { get; set; } = new();
    public List<ProcessPainPoint> PainPoints { get; set; } = new();
    public List<ProcessKpi> Kpis { get; set; } = new();
}

/// <summary>An application a process uses, and what for.</summary>
public class ProcessApplication
{
    public int ProcessId { get; set; }
    public BusinessProcess? Process { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }
    /// <summary>e.g. "wave planning", "carrier tendering".</summary>
    public string? Usage { get; set; }
    /// <summary>The main system for this process, as opposed to one it touches in passing.</summary>
    public bool IsPrimary { get; set; }
}

/// <summary>An integration a process relies on: if the flow stops, the process stops or degrades.</summary>
public class ProcessIntegration
{
    public int ProcessId { get; set; }
    public BusinessProcess? Process { get; set; }
    public int IntegrationId { get; set; }
    public Integration? Integration { get; set; }
}

/// <summary>Something that slows a process down or causes errors, optionally tied to the application where it hurts and the roadmap item that fixes it.</summary>
public class ProcessPainPoint
{
    public int Id { get; set; }
    public int ProcessId { get; set; }
    public BusinessProcess? Process { get; set; }
    public string Description { get; set; } = "";
    public PainSeverity Severity { get; set; } = PainSeverity.Medium;
    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }
    /// <summary>The roadmap item (usually an Initiative) that fixes it.</summary>
    public int? AddressedByItemId { get; set; }
    public RoadmapItem? AddressedBy { get; set; }
    public PainPointStatus Status { get; set; } = PainPointStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A measure of how well a process runs: where it started, where it should be and where it is.</summary>
public class ProcessKpi
{
    public int Id { get; set; }
    public int ProcessId { get; set; }
    public BusinessProcess? Process { get; set; }
    public string Name { get; set; } = "";
    /// <summary>"%", "hrs", "orders/hr", "USD".</summary>
    public string? Unit { get; set; }
    public KpiDirection Direction { get; set; } = KpiDirection.HigherIsBetter;
    public decimal? Baseline { get; set; }
    public decimal? Target { get; set; }
    public decimal? Current { get; set; }
    public DateOnly? MeasuredOn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Share of the way from baseline to target in percent; null when baseline, target or current is missing.</summary>
    public int? ProgressPct => PortfolioRules.KpiProgress(this);
    /// <summary>Current meets the target in the right direction; null when either is missing.</summary>
    public bool? OnTarget => PortfolioRules.KpiOnTarget(this);
}

/// <summary>
/// Value a roadmap item is expected to deliver, e.g. "Save 1,040 labor hours a year" on the initiative that automates
/// billing. Expected values are annual; measurements record what it's actually running at.
/// </summary>
public class Benefit
{
    public int Id { get; set; }
    /// <summary>The roadmap item (usually an Initiative) that should deliver it.</summary>
    public int RoadmapItemId { get; set; }
    public RoadmapItem? RoadmapItem { get; set; }
    /// <summary>Where the benefit lands, if in one application.</summary>
    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }
    /// <summary>A process KPI that measures it; its current value stands in when the benefit has no measurements.</summary>
    public int? KpiId { get; set; }
    public ProcessKpi? Kpi { get; set; }
    public BenefitCategory Category { get; set; } = BenefitCategory.CostSavings;
    public string Description { get; set; } = "";
    public BenefitUnit Unit { get; set; } = BenefitUnit.USD;
    /// <summary>The annual value once fully realized (or the target level, for adoption and percentages).</summary>
    public decimal? ExpectedValue { get; set; }
    public DateOnly? ExpectedBy { get; set; }
    /// <summary>The starting level, for benefits measured as a level (adoption %, error rate). Empty for savings counted from zero.</summary>
    public decimal? Baseline { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<BenefitMeasurement> Measurements { get; set; } = new();
}

/// <summary>One reading of a benefit: the annualized value (or level) it was running at on a date.</summary>
public class BenefitMeasurement
{
    public int Id { get; set; }
    public int BenefitId { get; set; }
    public Benefit? Benefit { get; set; }
    public DateOnly MeasuredOn { get; set; }
    public decimal Value { get; set; }
    /// <summary>Where the number came from, e.g. "Power BI ops dashboard".</summary>
    public string? Source { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A decision worth remembering: what was decided, why, what else was considered, and how it turned out. Linked to
/// every record it affects, so it shows up there and in impact analysis.
/// </summary>
public class Decision
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateOnly DecidedOn { get; set; }
    public DecisionStatus Status { get; set; } = DecisionStatus.Decided;
    /// <summary>The situation and the problem that needed a decision.</summary>
    public string? Context { get; set; }
    /// <summary>What was decided.</summary>
    public string DecisionText { get; set; } = "";
    /// <summary>How it turned out, filled in later.</summary>
    public string? Outcome { get; set; }
    /// <summary>Who made the call: a person, a steering committee.</summary>
    public string? DecidedBy { get; set; }
    /// <summary>The later decision that replaced this one.</summary>
    public int? SupersededById { get; set; }
    public Decision? SupersededBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<DecisionAlternative> Alternatives { get; set; } = new();
    public List<DecisionLink> Links { get; set; } = new();
}

/// <summary>An option that was considered and not chosen.</summary>
public class DecisionAlternative
{
    public int Id { get; set; }
    public int DecisionId { get; set; }
    public string Option { get; set; } = "";
    public string? Pros { get; set; }
    public string? Cons { get; set; }
    public string? WhyRejected { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Ties a decision to one record. Exactly one target id is set (a CHECK constraint enforces it).</summary>
public class DecisionLink
{
    public int Id { get; set; }
    public int DecisionId { get; set; }
    public Decision? Decision { get; set; }
    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }
    public int? IntegrationId { get; set; }
    public Integration? Integration { get; set; }
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? ProcessId { get; set; }
    public BusinessProcess? Process { get; set; }
    public int? RoadmapItemId { get; set; }
    public RoadmapItem? RoadmapItem { get; set; }
    public int? TradingPartnerId { get; set; }
    public TradingPartner? TradingPartner { get; set; }

    public DecisionTarget Target => DecisionTarget.Of(this);

    /// <summary>The target's name (needs its navigation loaded).</summary>
    public string TargetName => Application?.Name ?? Integration?.Name ?? Vendor?.Name ?? Contract?.Name ?? Process?.Name
        ?? RoadmapItem?.Title ?? TradingPartner?.Name ?? "";
}

/// <summary>A decision link's target as one value. <see cref="Key"/> ("a12", "i3", "v2", "c5", "pr4", "r9", "p3") is used in select lists and URLs.</summary>
public readonly record struct DecisionTarget(DecisionTargetKind Kind, int Id)
{
    private static readonly (DecisionTargetKind Kind, string Prefix)[] Prefixes =
    [
        (DecisionTargetKind.Process, "pr"), (DecisionTargetKind.Application, "a"), (DecisionTargetKind.Integration, "i"),
        (DecisionTargetKind.Vendor, "v"), (DecisionTargetKind.Contract, "c"), (DecisionTargetKind.RoadmapItem, "r"),
        (DecisionTargetKind.TradingPartner, "p"),
    ];

    public string Key { get { var kind = Kind; return Prefixes.First(p => p.Kind == kind).Prefix + Id; } }

    public static DecisionTarget? Parse(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var (kind, prefix) in Prefixes)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.AsSpan(prefix.Length), out var id) && id > 0)
                return new DecisionTarget(kind, id);
        return null;
    }

    public static DecisionTarget Of(DecisionLink l) =>
        l.ApplicationId is { } a ? new(DecisionTargetKind.Application, a)
        : l.IntegrationId is { } i ? new(DecisionTargetKind.Integration, i)
        : l.VendorId is { } v ? new(DecisionTargetKind.Vendor, v)
        : l.ContractId is { } c ? new(DecisionTargetKind.Contract, c)
        : l.ProcessId is { } p ? new(DecisionTargetKind.Process, p)
        : l.RoadmapItemId is { } r ? new(DecisionTargetKind.RoadmapItem, r)
        : new(DecisionTargetKind.TradingPartner, l.TradingPartnerId ?? 0);

    /// <summary>A new link from <paramref name="decisionId"/> to this target.</summary>
    public DecisionLink ToLink(int decisionId)
    {
        var l = new DecisionLink { DecisionId = decisionId };
        switch (Kind)
        {
            case DecisionTargetKind.Application: l.ApplicationId = Id; break;
            case DecisionTargetKind.Integration: l.IntegrationId = Id; break;
            case DecisionTargetKind.Vendor: l.VendorId = Id; break;
            case DecisionTargetKind.Contract: l.ContractId = Id; break;
            case DecisionTargetKind.Process: l.ProcessId = Id; break;
            case DecisionTargetKind.RoadmapItem: l.RoadmapItemId = Id; break;
            default: l.TradingPartnerId = Id; break;
        }
        return l;
    }

    public override string ToString() => Key;
}

/// <summary>Portfolio-wide settings; a single row with Id 1.</summary>
public class PortfolioSettings
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    /// <summary>US dollars per hour, to value labor savings.</summary>
    public decimal? LaborRate { get; set; }
    /// <summary>Notice deadlines this close (in days) show as upcoming renewals.</summary>
    public int RenewalWarningDays { get; set; } = 120;
    public int DefaultReviewIntervalMonths { get; set; } = 12;
}

/// <summary>
/// One recorded change to a portfolio record. No foreign key, so history survives deletion; <see cref="EntityName"/>
/// is a snapshot. Changes to a vendor's contracts and escalation path are filed under the vendor; links on an
/// application (initiatives, support contacts) under the application.
/// </summary>
public class PortfolioAuditEntry
{
    public int Id { get; set; }
    public PortfolioEntityType EntityType { get; set; }
    public int EntityId { get; set; }
    public string? EntityName { get; set; }
    public AuditAction Action { get; set; }
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
