namespace Roadmapper.Core.Portfolio;

/// <summary>What kind of system an application is. <see cref="ExternalParty"/> stands for a customer, carrier or trading partner endpoint.</summary>
public enum AppCategory { WMS, TMS, ERP, EDI, CustomerPortal, Reporting, IntegrationPlatform, Finance, Other, ExternalParty }

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
public enum PortfolioEntityType { Application, Vendor, Contact, Integration }

/// <summary>How data moves between two applications.</summary>
public enum IntegrationType { API, SFTP, EDI, Manual, Batch }

/// <summary>One-way flows go source → target; bidirectional flows also send data back.</summary>
public enum FlowDirection { OneWay, Bidirectional }

public enum IntegrationFrequency { RealTime, NearRealTime, Hourly, Daily, Weekly, OnDemand }

/// <summary>Open and Accepted risks still count toward an integration's risk level; Mitigated and Closed don't.</summary>
public enum RiskStatus { Open, Mitigated, Accepted, Closed }

/// <summary>An integration's overall risk, from its risks, criticality and review date. See <see cref="PortfolioRules.RiskLevel"/>.</summary>
public enum RiskLevel { Low, Medium, High }
