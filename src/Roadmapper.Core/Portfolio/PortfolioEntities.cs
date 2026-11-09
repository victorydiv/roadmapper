namespace Roadmapper.Core.Portfolio;

/// <summary>A person: internal staff when <see cref="VendorId"/> is null, otherwise someone at that vendor.</summary>
public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    /// <summary>Internal staff only, e.g. "Ops – Memphis DC".</summary>
    public string? Department { get; set; }
    public string? Notes { get; set; }
    /// <summary>Cleared when someone leaves, so their history and links still read correctly.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsInternal => VendorId is null;
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
/// A flow of data from <see cref="Source"/> to <see cref="Target"/>, optionally through a middle hop
/// (<see cref="Via"/>: an EDI VAN, iPaaS or middleware application).
/// </summary>
public class Integration
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SourceApplicationId { get; set; }
    public Application? Source { get; set; }
    public int TargetApplicationId { get; set; }
    public Application? Target { get; set; }
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
