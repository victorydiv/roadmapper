namespace Roadmapper.Core.Portfolio;

/// <summary>What kind of system an application is. Customers, carriers and other outside companies are <see cref="TradingPartner"/>s, not applications.</summary>
public enum AppCategory { WMS, TMS, ERP, EDI, CustomerPortal, Reporting, IntegrationPlatform, Finance, Other }

/// <summary>Business criticality. Tier 1 = operations stop without it.</summary>
public enum Criticality { Tier1 = 1, Tier2 = 2, Tier3 = 3 }

/// <summary>Where an application or integration is in its life.</summary>
public enum LifecycleStatus { Planned, Active, Retiring, Retired }

public enum Hosting { SaaS, Hosted, OnPrem }

/// <summary>Gartner TIME model, from business fit × technical fit.</summary>
public enum TimeQuadrant { Tolerate, Invest, Migrate, Eliminate }

public enum SupportRole { L1Support, L2Support, VendorSupport, SME, KeyUser, Other }

/// <summary>How a roadmap item affects a linked application.</summary>
public enum InitiativeImpact { Enhances, Implements, Replaces, Retires, Integrates, Upgrades }

/// <summary>Kinds of portfolio record that keep a change history.</summary>
public enum PortfolioEntityType { Application, Vendor, Contact, Integration, TradingPartner, Process, Benefit, Decision, Document }

/// <summary>How data moves between two applications.</summary>
public enum IntegrationType { API, SFTP, EDI, Manual, Batch }

/// <summary>One-way flows go source → target; bidirectional flows also send data back.</summary>
public enum FlowDirection { OneWay, Bidirectional }

public enum IntegrationFrequency { RealTime, NearRealTime, Hourly, Daily, Weekly, OnDemand }

/// <summary>Open and Accepted risks still count toward an integration's risk level; Mitigated and Closed don't.</summary>
public enum RiskStatus { Open, Mitigated, Accepted, Closed }

/// <summary>An integration's overall risk, from its risks, criticality and review date. See <see cref="PortfolioRules.RiskLevel"/>.</summary>
public enum RiskLevel { Low, Medium, High }

/// <summary>The kind of outside company a trading partner is.</summary>
public enum PartnerType { Customer, Carrier, Supplier, Other }

/// <summary>Where the relationship stands. Onboarding partners are being set up (mapping, testing) and not live yet.</summary>
public enum PartnerStatus { Onboarding, Active, Inactive }

/// <summary>How much it matters when a partner's flows break. Key partners raise the severity of impacts on them.</summary>
public enum PartnerImportance { Key, Standard }

/// <summary>The part of a 3PL's operation a business process belongs to.</summary>
public enum ProcessDomain { Inbound, Inventory, Outbound, Transportation, Billing, CustomerService, Reporting, Other }

/// <summary>How much a pain point hurts the process.</summary>
public enum PainSeverity { Low, Medium, High, Critical }

/// <summary>Addressed: fixed (usually by the linked roadmap item). Won't fix: accepted as it is.</summary>
public enum PainPointStatus { Open, Addressed, WontFix }

/// <summary>Which way a KPI should move.</summary>
public enum KpiDirection { HigherIsBetter, LowerIsBetter }

/// <summary>How a business process is touched by a change: through an application it uses, or an integration it depends on.</summary>
public enum ProcessImpactKind { UsesApplication, DependsOnIntegration, SubProcessAffected }

/// <summary>What kind of value a benefit delivers.</summary>
public enum BenefitCategory { CostSavings, LaborSavings, RevenueImpact, Adoption, RiskReduction, Other }

/// <summary>What a benefit's values are counted in. Hours are valued in dollars with the portfolio labor rate.</summary>
public enum BenefitUnit { USD, Hours, Percent, Count }

/// <summary>Where a benefit stands. See <see cref="BenefitMath.Status"/>.</summary>
public enum BenefitStatus { NotMeasured, Realizing, AtRisk, Realized }

/// <summary>An application's overall health. See <see cref="PortfolioHealth"/>.</summary>
public enum HealthRating { Green, Amber, Red }

/// <summary>Superseded: replaced by a later decision. Reversed: undone.</summary>
public enum DecisionStatus { Proposed, Decided, Superseded, Reversed }

/// <summary>What a decision is linked to.</summary>
public enum DecisionTargetKind { Application, Integration, Vendor, Contract, Process, RoadmapItem, TradingPartner }

/// <summary>Overall severity of a change impact. See <see cref="ImpactAnalyzer"/>.</summary>
public enum ImpactSeverity { Low, Medium, High }

/// <summary>What kind of document. Each kind is saved to its own folder (see <see cref="DocumentFolder"/>).</summary>
public enum DocumentCategory { Roadmap, Contract, Invoice, Sow, Brd, Other }

/// <summary>What a document is attached to.</summary>
public enum DocumentTargetKind { Product, Application, Vendor, Contract, RoadmapItem }
