# Application Portfolio & Integration Management — Architecture

## Context

Roadmapper today is a single-user, offline roadmap tool (.NET 10 Blazor Server, EF Core + SQLite, packaged as `Roadmapper.exe`). A **Product** is one roadmap: Goals › Initiatives › Features › Tasks in one `Items` table, with WSJF/RICE scoring, snapshots, audit history and `.rmp` sharing.

The user is an **IT Product Manager at a 3PL** who manages *purchased* SaaS (WMS, TMS, ERP, EDI, portals, reporting). Their real work is above the backlog: which systems they own, who the vendors are, what's wired to what, what breaks if something changes, what an investment returned, and why decisions were made. Jira stays the system of record for execution. This module turns Roadmapper into a **Product Operations Platform**: a portfolio catalog + integration map + impact analysis + benefits tracking + decision log, all linked back into the existing roadmap rather than duplicating it.

### Key design decisions
| Topic | Decision |
|---|---|
| App ↔ roadmap | **Both links.** `Application.ProductId` (optional: the app's own roadmap) **and** many-to-many `ApplicationInitiatives` (any roadmap item, any product, ↔ many apps). |
| Contracts | **Contract table.** Vendor 1..n Contracts; Application → Contract; app renewal date is *derived*, never stored twice. |
| API | **Read-only JSON API** (`GET /api/portfolio/*`) + XLSX/SVG exports. All writes go through the UI/service. |
| Pacing | Create branch, commit architecture doc, build **one phase at a time, stop for review after each**. |

### Guiding principles
1. **Reuse, don't duplicate.** Initiatives, progress roll-up, owners, status and dates come from existing `RoadmapItem`s. Benefits attach to roadmap items. The TIME chart reuses the Priorities-matrix pattern. Map SVG follows `TimelineSvg`.
2. **Portfolio is global**, not per product. A product roadmap is one lens; the portfolio spans all products.
3. **Pure logic in Core, I/O in Data, UI in Web** — same layering as `RollupService`/`ScoringRubric` today, so impact analysis, health and layout are unit-testable without a DB.
4. **Not Jira.** No stories/defects/sprints. Only an optional `JiraKey`/URL link on roadmap items (Phase 6) to jump across.

---

## 1. Architecture

```
┌──────────────────────────── Roadmapper.Web ─────────────────────────────┐
│ Pages/Portfolio/*.razor  ── inject ──► PortfolioService (Data)          │
│ Api/PortfolioApi.cs  (GET /api/portfolio/*, read-only JSON)             │
│ Exports/IntegrationMapSvg.cs, PortfolioExport (xlsx)                    │
└──────────────────────────────────────────────────────────────────────────┘
┌──────────────────────────── Roadmapper.Data ────────────────────────────┐
│ Portfolio/PortfolioService.*.cs  (partial, one file per aggregate)      │
│ Portfolio/PortfolioModel.cs      (IEntityTypeConfiguration<> classes)   │
│ Portfolio/PortfolioAuditTrail.cs (field-level history for portfolio)    │
│ RoadmapDbContext  (+ DbSets, ApplyConfigurationsFromAssembly)           │
│ Migrations/  AddPortfolioCatalog, AddIntegrations, AddProcesses, …      │
└──────────────────────────────────────────────────────────────────────────┘
┌──────────────────────────── Roadmapper.Core ────────────────────────────┐
│ Portfolio/Entities, Enums, Drafts (validation)                          │
│ Portfolio/PortfolioGraph   — in-memory graph of apps + integrations     │
│ Portfolio/ImpactAnalyzer   — change-impact traversal → ImpactReport     │
│ Portfolio/PortfolioHealth  — health score, TIME quadrant, risk rating   │
│ Portfolio/BenefitMath      — expected vs actual, realization %          │
│ Portfolio/MapLayout        — layered layout for the integration map     │
└──────────────────────────────────────────────────────────────────────────┘
```

- `PortfolioService(IDbContextFactory<RoadmapDbContext>, TimeProvider?)` mirrors `RoadmapService` exactly: short-lived context per call, `InvalidOperationException` for user-facing rule errors, `AsNoTracking` read models. Registered `AddScoped<PortfolioService>()` in [Program.cs](../src/Roadmapper.Web/Program.cs).
- **Same database file**, same migration chain — so roadmap items can be FK-linked.
- **Audit:** existing `AuditEntry` has a required `ProductId` FK, so portfolio history goes to a new `PortfolioAuditEntries` table using the same Capture → Save → Resolve pattern as [AuditTrail.cs](../src/Roadmapper.Data/AuditTrail.cs), hooked into the same `SaveChangesAsync` override.
- **Money:** `decimal` properties. EF SQLite stores decimal as TEXT and can't SUM/ORDER in SQL, so aggregates are computed in memory (portfolio is hundreds of rows, not millions) — same "roadmaps are small" stance the app already takes.
- **Enums stored as text** (existing convention, readable DB).

---

## 2. Database schema (normalized)

Legend: **PK**, FK→table (delete behavior), *derived* = computed, never stored. All tables have `CreatedAt`, `UpdatedAt` (UTC) unless noted. Text lengths follow existing conventions (names 200, notes 4000).

### 2.1 People & vendors

**Contacts** — one row per person, internal or vendor. Avoids repeating names/emails across apps, vendors, integrations, processes.
| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| Name | text(200) req | |
| Title | text(200) | job title |
| Email, Phone | text(200), text(50) | |
| VendorId | int? FK→Vendors (SetNull) | null = internal employee |
| Department | text(100) | internal only (e.g. "Ops – Memphis DC") |
| Notes | text(2000) | |
| IsActive | bool | keep history when people leave |
Index: (VendorId), (Name).

**Vendors**
| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| Name | text(200) req, unique NOCASE | |
| Website | text(500) | |
| AccountManagerId | int? FK→Contacts (SetNull) | |
| TechnicalContactId | int? FK→Contacts (SetNull) | |
| SupportPortalUrl, SupportPhone | text | |
| Notes | text(4000) | |
| *ContractStart / ContractEnd / NextRenewal* | *derived* | min/max/next over its Contracts |

**VendorEscalationContacts** (ordered escalation path)
| VendorId FK (Cascade) | ContactId FK (Cascade) | Level int (1 = first call) | Note text(500) | PK(VendorId, ContactId) |

**Contracts**
| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| VendorId | int FK→Vendors (Cascade) | |
| Name | text(200) req | e.g. "WMS SaaS subscription 2025–2028" |
| ContractNumber | text(100) | |
| StartDate, EndDate | DateOnly? | |
| RenewalDate | DateOnly? | |
| NoticePeriodDays | int? | → *NoticeDeadline = RenewalDate − NoticePeriodDays* (the date that actually matters) |
| AutoRenews | bool | |
| AnnualValue | decimal? | total contract value / yr |
| Notes | text(4000) | |
Index: (VendorId), (RenewalDate).

### 2.2 Applications

**Applications**
| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| Name | text(200) req, unique NOCASE | |
| Description | text(4000) | |
| Category | enum text | WMS, TMS, ERP, EDI, CustomerPortal, Reporting, Integration­Platform, Finance, Other, **ExternalParty** (customer/carrier/trading-partner endpoints so EDI flows have both ends) |
| VendorId | int? FK→Vendors (SetNull) | null = in-house / external party |
| ContractId | int? FK→Contracts (SetNull) | must belong to VendorId (service rule) |
| ProductId | int? FK→Products (SetNull), unique when not null | the app's own roadmap |
| BusinessOwnerId | int? FK→Contacts (SetNull) | |
| TechnicalOwnerId | int? FK→Contacts (SetNull) | |
| Criticality | enum text: Tier1, Tier2, Tier3 | Tier1 = site stops shipping |
| Status | enum text: Planned, Active, Retiring, Retired | |
| AnnualCost | decimal? | this app's share (a contract can cover several apps) |
| Hosting | enum text: SaaS, Hosted, OnPrem | |
| BusinessFit, TechnicalFit | int? (1–5) | drive TIME quadrant |
| GoLiveDate, RetireDate | DateOnly? | |
| Notes | text(4000) | |
| *RenewalDate* | *derived* | `Contract.RenewalDate` |
| *TimeQuadrant* | *derived* | Tolerate / Invest / Migrate / Eliminate (Gartner TIME) |
Index: (Status), (VendorId), (Criticality).

**ApplicationSupportContacts**
| ApplicationId FK (Cascade) | ContactId FK (Cascade) | Role enum text: L1Support, L2Support, VendorSupport, SME, KeyUser, Other | Note | PK(ApplicationId, ContactId, Role) |

**ApplicationInitiatives** — the bridge to the existing roadmap ("Linked Roadmap Initiatives")
| ApplicationId FK→Applications (Cascade) | RoadmapItemId FK→Items (Cascade) | ImpactType enum text: Enhances, Implements, Replaces, Retires, Integrates, Upgrades | PK(ApplicationId, RoadmapItemId) |
Any item level is allowed (Initiative is the default in the UI); status, dates, progress, owner all read from `Items` — nothing copied.

### 2.3 Integrations

**Integrations**
| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| Name | text(200) req | e.g. "Order release WMS→TMS" |
| SourceApplicationId | int FK→Applications (**Restrict**) | can't delete an app that still has integrations — UI explains |
| TargetApplicationId | int FK→Applications (Restrict) | ≠ Source (check) |
| ViaApplicationId | int? FK→Applications (SetNull) | middleware/EDI VAN/iPaaS hop |
| Type | enum text: API, SFTP, EDI, Manual, Batch | |
| Direction | enum text: OneWay, Bidirectional | source→target is the primary flow |
| Frequency | enum text: RealTime, NearRealTime, Hourly, Daily, Weekly, OnDemand | |
| MessageFormat | text(100) | e.g. "X12 940", "JSON", "CSV" |
| BusinessPurpose | text(2000) req | |
| Criticality | enum text Tier1–3 | business criticality of the flow |
| OwnerId | int? FK→Contacts (SetNull) | |
| Status | enum text: Planned, Active, Retiring, Retired | |
| LastReviewDate | DateOnly? | |
| ReviewIntervalMonths | int default 12 | → *ReviewOverdue* derived |
| Notes | text(4000) | |
| *RiskRating* | *derived* | max open risk score, bumped by Tier1 + overdue review |
Index: (SourceApplicationId), (TargetApplicationId), (ViaApplicationId).

**IntegrationRisks** — "Risks" as rows, so high-risk integrations are computable
| Id PK | IntegrationId FK (Cascade) | Description text(1000) req | Likelihood 1–5 | Impact 1–5 | *Score = L×I (derived)* | Mitigation text(2000) | Status enum: Open, Mitigated, Accepted, Closed | OwnerId FK→Contacts? (SetNull) | RaisedOn DateOnly |

### 2.4 Business processes

**BusinessProcesses**
| Id PK | Name text(200) req unique NOCASE | Description | ProcessOwnerId FK→Contacts? (SetNull) | ParentProcessId FK→self? (Restrict) — L1 "Order to Cash" › L2 "Outbound shipping" | Domain enum text: Inbound, Inventory, Outbound, Transportation, Billing, CustomerService, Reporting, Other | SortOrder |

**ProcessApplications** ("Applications Used")
| ProcessId FK (Cascade) | ApplicationId FK (Cascade) | Usage text(500) e.g. "wave planning" | IsPrimary bool | PK(ProcessId, ApplicationId) |

**ProcessIntegrations** (which flows the process depends on — makes impact analysis precise)
| ProcessId FK (Cascade) | IntegrationId FK (Cascade) | PK |

**ProcessPainPoints**
| Id PK | ProcessId FK (Cascade) | Description text(1000) req | Severity enum: Low, Medium, High, Critical | ApplicationId FK? (SetNull) — where it hurts | AddressedByItemId FK→Items? (SetNull) — the initiative fixing it | Status: Open, Addressed, WontFix |

**ProcessKpis**
| Id PK | ProcessId FK (Cascade) | Name text(200) req | Unit text(30) ("%", "hrs", "orders/hr", "USD") | Direction enum: HigherIsBetter, LowerIsBetter | Baseline decimal? | Target decimal? | Current decimal? | MeasuredOn DateOnly? |

### 2.5 Benefits realization (attached to roadmap items)

**Benefits**
| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| RoadmapItemId | int FK→Items (Cascade) | the initiative that should deliver it |
| ApplicationId | int? FK→Applications (SetNull) | where it lands |
| KpiId | int? FK→ProcessKpis (SetNull) | optional: benefit measured by a process KPI |
| Category | enum text: CostSavings, LaborSavings, RevenueImpact, Adoption, RiskReduction, Other | |
| Description | text(1000) req | |
| Unit | enum text: USD, Hours, Percent, Count | Labor savings in Hours are valued with a portfolio setting `LaborRate` (USD/hr) for $ totals |
| ExpectedValue | decimal? | annualized |
| ExpectedBy | DateOnly? | |
| Baseline | decimal? | for Adoption/% benefits |
| Notes | text(2000) | |
| *ActualValue / Realization %* | *derived* | latest (or sum, per category rule) of measurements |

**BenefitMeasurements** (time series → actuals trend)
| Id PK | BenefitId FK (Cascade) | MeasuredOn DateOnly req | Value decimal req | Source text(200) ("Power BI ops dashboard") | Note |
Index: (BenefitId, MeasuredOn) unique.

### 2.6 Decision log

**Decisions**
| Id PK | Title text(300) req | DecidedOn DateOnly req | Status enum: Proposed, Decided, Superseded, Reversed | Context text(4000) | Decision text(4000) req | Outcome text(4000) (filled in later) | DecidedBy text(200) | SupersededById FK→self? (SetNull) |

**DecisionAlternatives**
| Id PK | DecisionId FK (Cascade) | Option text(500) req | Pros, Cons, WhyRejected text(2000) | SortOrder |

**DecisionLinks** — one table, exactly one target column non-null (CHECK constraint)
| Id PK | DecisionId FK (Cascade) | ApplicationId? | IntegrationId? | VendorId? | ContractId? | ProcessId? | RoadmapItemId? (all FK, Cascade) |
Lets a decision show up on every entity it affects and in impact reports.

### 2.7 Supporting

**PortfolioSettings** (single row, Id = 1): `LaborRate decimal?`, `RenewalWarningDays int = 120`, `DefaultReviewIntervalMonths int = 12`.

**PortfolioAuditEntries**: Id, EntityType text(40), EntityId int, EntityName text(300) (snapshot), Action, Field, OldValue, NewValue, ChangedAt. Index (EntityType, EntityId), (ChangedAt). No FK — survives deletes, like `AuditEntry`.

### 2.8 ER overview
```
Vendor 1─* Contract 1─* Application *─1 Vendor
Vendor 1─* Contact (vendor staff)        Application ─0..1 Product (own roadmap)
Vendor *─* Contact (escalation, ordered) Application *─* RoadmapItem (ApplicationInitiatives)
Application *─* Contact (support roles)  Application 1─* Integration (as Source/Target/Via)
Integration 1─* IntegrationRisk          BusinessProcess *─* Application / Integration
BusinessProcess 1─* PainPoint ─0..1 RoadmapItem (addressed by)
BusinessProcess 1─* Kpi ◄─ Benefit *─1 RoadmapItem
Decision 1─* Alternative, 1─* DecisionLink ─► any of the above
```

### 2.9 Business rules (enforced in service, unit-tested)
- Integration Source ≠ Target; Via ≠ Source/Target; no duplicate (Source, Target, Type, Name).
- `Application.ContractId` must belong to `Application.VendorId`.
- `Application.ProductId` unique (one app per product roadmap).
- Deleting an Application with integrations is refused with a message listing them (Restrict FK + friendly check); `Retiring/Retired` is the normal path.
- BusinessProcess parent cycle prevention (reuse the approach from [HierarchyRules.cs](../src/Roadmapper.Core/HierarchyRules.cs) / [DependencyRules.cs](../src/Roadmapper.Core/DependencyRules.cs)).
- Fit scores 1–5; risk L/I 1–5; DecisionLinks exactly one target.
- Deleting a Product: `Application.ProductId` → null; `ApplicationInitiatives`/`Benefits` for its items cascade (warn in Settings → Delete product dialog with counts).

---

## 3. Entity definitions (pattern)

POCOs in `Roadmapper.Core/Portfolio/PortfolioEntities.cs`, same style as [Entities.cs](../src/Roadmapper.Core/Entities.cs):

```csharp
public class Application
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public AppCategory Category { get; set; }
    public int? VendorId { get; set; }          public Vendor? Vendor { get; set; }
    public int? ContractId { get; set; }        public Contract? Contract { get; set; }
    public int? ProductId { get; set; }         public Product? Product { get; set; }
    public int? BusinessOwnerId { get; set; }   public Contact? BusinessOwner { get; set; }
    public int? TechnicalOwnerId { get; set; }  public Contact? TechnicalOwner { get; set; }
    public Criticality Criticality { get; set; } = Criticality.Tier2;
    public LifecycleStatus Status { get; set; } = LifecycleStatus.Active;
    public decimal? AnnualCost { get; set; }
    public int? BusinessFit { get; set; }  public int? TechnicalFit { get; set; }
    // …
    public List<ApplicationSupportContact> SupportContacts { get; set; } = new();
    public List<ApplicationInitiative> Initiatives { get; set; } = new();
    public DateOnly? RenewalDate => Contract?.RenewalDate;   // derived, [NotMapped]
}
```
Mapping in `Roadmapper.Data/Portfolio/PortfolioModel.cs` as `IEntityTypeConfiguration<T>` classes; `RoadmapDbContext.OnModelCreating` adds `b.ApplyConfigurationsFromAssembly(typeof(RoadmapDbContext).Assembly)` so the existing file doesn't balloon. Drafts (`ApplicationDraft`, `IntegrationDraft`, …) with `Validate()` mirror [ItemDraft.cs](../src/Roadmapper.Core/ItemDraft.cs).

## 4. Migrations (one per phase, generated with `dotnet ef migrations add`)
| Phase | Migration | Tables |
|---|---|---|
| 1 | `AddPortfolioCatalog` | Contacts, Vendors, VendorEscalationContacts, Contracts, Applications, ApplicationSupportContacts, ApplicationInitiatives, PortfolioSettings, PortfolioAuditEntries |
| 2 | `AddIntegrations` | Integrations, IntegrationRisks |
| 3 | `AddBusinessProcesses` | BusinessProcesses, ProcessApplications, ProcessIntegrations, ProcessPainPoints, ProcessKpis |
| 4 | `AddBenefits` | Benefits, BenefitMeasurements |
| 5 | `AddDecisionLog` | Decisions, DecisionAlternatives, DecisionLinks (+ CHECK constraint) |
| 6 | `AddItemJiraLink` | `Items.JiraKey`, `Items.ExternalUrl` (nullable) |
All additive — existing databases upgrade automatically via `DbInitializer.MigrateAsync()`; no data changes to existing tables except Phase 6's nullable columns.

## 5. Core algorithms

**PortfolioGraph** (Core): built from apps + integrations; nodes = apps, edges = integrations (Via splits into source→via→target for traversal). `Upstream(app, depth)`, `Downstream(app, depth)`, with Bidirectional edges counted both ways.

**ImpactAnalyzer.Analyze(graph, appId, ImpactScope)** → `ImpactReport`:
1. **Integrations**: every integration where app is Source/Target/Via (direct), plus transitive downstream flows to depth N (default 2) flagged *indirect*.
2. **Business processes**: processes using the app (ProcessApplications) ∪ processes depending on any impacted integration (ProcessIntegrations); parent processes rolled up.
3. **Roadmap initiatives**: items linked to the app and to directly connected apps (ApplicationInitiatives), with status/dates from `Items`; in-flight ones (Planned/InProgress) highlighted.
4. **Vendors**: the app's vendor + vendors of directly integrated apps (they must coordinate/test), with escalation contacts and next notice deadline.
5. Also: open integration risks, Tier-1 count, open pain points, decisions linked.
Output is grouped, sorted by criticality, and includes a severity summary ("3 Tier-1 integrations, 2 L1 processes, 4 in-flight initiatives").

**PortfolioHealth**: per-app score 0–100 from: Tier, open risk scores on its integrations, overdue integration reviews, renewal notice deadline within `RenewalWarningDays`, Status=Retiring with active integrations, fit scores. Mapped to Green/Amber/Red. TIME quadrant from BusinessFit × TechnicalFit (≥3 = high).

**BenefitMath**: realization % = actual ÷ expected (direction-aware), $-normalized totals (Hours × LaborRate), and "at risk" when ExpectedBy has passed with < 50 % realized.

**MapLayout**: layered (Sugiyama-lite) layout — columns by Category order (ExternalParty · Portal · ERP · WMS/TMS · EDI/Integration · Reporting) or, for the dependency view, upstream | selected | downstream. Deterministic, pure, unit-tested; rendered as SVG.

## 6. API endpoints (read-only JSON, `Roadmapper.Web/Api/PortfolioApi.cs`)
`app.MapPortfolioApi()` → group `/api/portfolio`, JSON camelCase + string enums (same options as `RoadmapFile`):
| Method/Path | Returns |
|---|---|
| GET `/applications` `?status=&category=&tier=` | list (with vendor, owners, renewal, health) |
| GET `/applications/{id}` | detail incl. contacts, initiatives, integrations, processes |
| GET `/applications/{id}/dependencies?depth=2` | upstream/downstream graph |
| GET `/applications/{id}/impact?depth=2` | `ImpactReport` |
| GET `/vendors`, `/vendors/{id}` | incl. contracts, escalation path |
| GET `/contracts?renewingWithinDays=120` | upcoming renewals |
| GET `/integrations` `?risk=high&type=` , `/integrations/{id}` | |
| GET `/processes`, `/processes/{id}` | |
| GET `/benefits?itemId=&productId=` | expected vs actual |
| GET `/decisions?entityType=&entityId=` | |
| GET `/dashboard` | executive dashboard model |
| GET `/map.svg?focus={appId}&depth=` | integration map image |
| GET `/export/portfolio.xlsx` | one sheet per entity (ClosedXML, like `RoadmapExport`) |
Safe on localhost because GET-only (no state change → no CSRF exposure). UI does **not** call the API; Razor pages inject `PortfolioService` like existing pages.

## 7. UI (wireframes)

Top bar ([MainLayout.razor](../src/Roadmapper.Web/Components/Layout/MainLayout.razor)): `Roadmapper | Products | Portfolio | … User guide`.
Portfolio pages share a `PortfolioHeader` tab bar (mirrors [ProductHeader.razor](../src/Roadmapper.Web/Components/Shared/ProductHeader.razor)):
`Dashboard · Applications · Integrations · Map · Processes · Vendors · Benefits · Decisions · Settings`

**Executive Dashboard** `/portfolio`
```
┌ Portfolio health ───────┐┌ Upcoming renewals (120d) ──────────────┐
│ ● 9 Green ● 3 Amber ● 1 ││ Manhattan WMS  notice by 2027-01-15 ⚠  │
│ Annual run cost $2.4M   ││ BluJay TMS     renews   2027-03-01     │
│ TIME: I 5 T 4 M 2 E 2   ││ …                       [All renewals] │
└─────────────────────────┘└────────────────────────────────────────┘
┌ High-risk integrations ─────────────────────────────────────────────┐
│ ▲ WMS→EDI 945 ship confirm  Tier1  risk 20  review overdue 4 mo     │
└─────────────────────────────────────────────────────────────────────┘
┌ Strategic initiative progress ──────┐┌ Realized value ──────────────┐
│ EDI 214 migration  ███████░░ 70% ● ││ Expected $1.1M  Realized $640k│
│ Yard mgmt rollout  ██░░░░░░░ 20% ▲ ││ ▇▇▇▅▃ by category  (58 %)    │
└─────────────────────────────────────┘└───────────────────────────────┘
[Export to Excel]  [Print / PDF]
```
(Initiatives shown = items linked to any application or carrying benefits, grouped by product, progress from existing roll-up.)

**Applications** `/portfolio/applications` — filterable table (Category, Tier, Status, Vendor, Health, Renewal) + TIME matrix toggle (reuses the Priorities quadrant styling).
**Application detail** `/portfolio/applications/{id}`
```
Manhattan WMS   [Tier 1] [Active] [SaaS]  ● Amber          [Edit] [Impact analysis]
Vendor: Manhattan Associates · Contract: WMS SaaS 2025–28 · renews 2027-03-01 (notice 2027-01-15)
Business owner: J. Ruiz (Ops)   Technical owner: P. Shah (IT)   Annual cost: $480,000
Roadmap: [Open "Manhattan WMS" roadmap →]
Tabs: Overview | Integrations (12) | Dependencies | Processes (6) | Initiatives (4) | Benefits | Contacts | Decisions | History
```
**Impact analysis** `/portfolio/applications/{id}/impact` — depth picker, four grouped panels (Integrations / Processes / Initiatives / Vendors) + summary banner, "Copy as text" & print for stakeholder emails.
**Dependencies** — upstream | [app] | downstream SVG, clickable nodes.
**Integration catalog** `/portfolio/integrations` table + **Map** `/portfolio/map` (SVG, filter by category/tier/type, focus app).
**Integration detail/editor** — fields + risk register grid (L × I heat chip).
**Processes** `/portfolio/processes` — tree (L1 › L2), detail with Apps used, Integrations, Pain points (→ "Create initiative" opens existing ItemEditor in chosen product), KPIs.
**Vendors** — list with next notice deadline; detail with contracts, account manager, tech contact, escalation ladder, apps.
**Benefits** `/portfolio/benefits` — by initiative: expected vs actual bars, add measurement inline.
**Decisions** `/portfolio/decisions` — log table, filter by linked entity; editor with alternatives list and link picker.
**Existing pages touched (small):** [ItemEditor.razor](../src/Roadmapper.Web/Components/Shared/ItemEditor.razor) gets an "Applications" multi-select + "Benefits" section for Initiatives; product Dashboard gets a "Linked applications" card; Settings delete-product warns about linked benefits.

## 8. Folder structure changes
```
src/Roadmapper.Core/Portfolio/
    PortfolioEntities.cs  PortfolioEnums.cs  PortfolioDrafts.cs  PortfolioRules.cs
    PortfolioGraph.cs  ImpactAnalyzer.cs  PortfolioHealth.cs  BenefitMath.cs  MapLayout.cs
src/Roadmapper.Data/Portfolio/
    PortfolioModel.cs  PortfolioAuditTrail.cs  PortfolioSeed.cs
    PortfolioService.cs (+ .Vendors .Applications .Integrations .Processes .Benefits .Decisions .Dashboard .cs)
    PortfolioExport.cs
src/Roadmapper.Data/Migrations/  2026111x_AddPortfolioCatalog.cs … (one per phase)
src/Roadmapper.Web/Api/PortfolioApi.cs
src/Roadmapper.Web/Exports/IntegrationMapSvg.cs
src/Roadmapper.Web/Components/Pages/Portfolio/
    PortfolioDashboard.razor  Applications.razor  ApplicationDetail.razor  ImpactAnalysis.razor
    Integrations.razor  IntegrationMap.razor  Processes.razor  Vendors.razor  VendorDetail.razor
    Benefits.razor  Decisions.razor  PortfolioSettings.razor
src/Roadmapper.Web/Components/Shared/Portfolio/
    PortfolioHeader.razor  ApplicationEditor.razor  VendorEditor.razor  ContractEditor.razor
    ContactPicker.razor  IntegrationEditor.razor  RiskGrid.razor  ProcessEditor.razor
    BenefitEditor.razor  DecisionEditor.razor  HealthBadge.razor  TierBadge.razor  ImpactPanel.razor
tests/Roadmapper.Tests/
    PortfolioServiceTests.cs  ImpactAnalyzerTests.cs  PortfolioHealthTests.cs  MapLayoutTests.cs  PortfolioApiTests.cs
docs/PORTFOLIO_ARCHITECTURE.md   (this design, committed in Phase 0)
docs/USER_GUIDE.md               (new "Portfolio" sections + §10 data model/routes/endpoints)
```

## 9. Phased implementation (stop for review after each)

**Phase 0 — Branch & architecture doc**
- `git checkout -b feature/portfolio-integration-mgmt` (note: untracked `docs/USER_GUIDE.pdf` is left alone).
- Commit `docs/PORTFOLIO_ARCHITECTURE.md` (sections 1–8 above).

**Phase 1 — Catalog foundation: Contacts, Vendors, Contracts, Applications, initiative links**
- Core entities/enums/drafts/rules; Data config + `AddPortfolioCatalog` migration; `PortfolioService` CRUD; `PortfolioAuditTrail`.
- UI: top-bar Portfolio link, PortfolioHeader, Applications list/detail/editor, Vendors list/detail (contracts, escalation), contact picker; ItemEditor "Applications" multi-select.
- API: `/applications`, `/vendors`, `/contracts`. Sample seed on new DBs (WMS, TMS, ERP, EDI platform, Customer portal, 2 vendors) linked to the sample product.
- Tests: CRUD, contract-belongs-to-vendor, product uniqueness, derived renewal/notice dates, audit rows, product delete cascade.

**Phase 2 — Integrations, catalog map, dependency view**
- Integrations + risks, `AddIntegrations`; `PortfolioGraph`, `MapLayout`, `IntegrationMapSvg`; Integrations page, Map page, application Dependencies tab; `/integrations`, `/applications/{id}/dependencies`, `/map.svg`.
- Tests: graph traversal (cycles, bidirectional, via), layout determinism, delete-restrict rule.

**Phase 3 — Business processes + Change Impact Analysis**
- Processes, pain points, KPIs, `AddBusinessProcesses`; `ImpactAnalyzer`; Processes pages; Impact Analysis page; pain point → "Create initiative" (reuses `ItemDraft`/`ItemEditor`); `/processes`, `/applications/{id}/impact`.
- Tests: impact covers all four categories, depth limits, in-flight initiative flagging.

**Phase 4 — Benefits realization**
- Benefits + measurements, `AddBenefits`; `BenefitMath`; Benefits page + ItemEditor Benefits section + product Dashboard card; `/benefits`.
- Tests: realization %, labor-rate $ conversion, at-risk detection.

**Phase 5 — Executive dashboard + Decision log**
- `PortfolioHealth`; dashboard page; Decisions + alternatives + links, `AddDecisionLog`; decisions surface on app/integration/vendor detail and in impact reports; `/dashboard`, `/decisions`, `/export/portfolio.xlsx`.
- Tests: health scoring, dashboard aggregates, DecisionLinks check constraint.

**Phase 6 — Polish & release**
- `Items.JiraKey/ExternalUrl` (link out to Jira), portfolio PDF print, user guide chapters + §10 reference, bump `<Version>` to 1.2.0, `publish.ps1` dry run.
- (Deferred / optional: portfolio backup file format, Jira read-sync — out of scope until asked.)

## 10. Verification (each phase)
- `dotnet build` and `dotnet test` from repo root — all existing + new tests green.
- `dotnet ef migrations add …` then apply to a **copy** of a real `%LOCALAPPDATA%\Roadmapper\roadmapper.db` to prove existing data upgrades cleanly; also fresh DB seeds sample portfolio.
- Run `dotnet run --project src/Roadmapper.Web --launch-profile http`, walk the phase's pages (create/edit/delete, rule errors shown as alerts), check History rows.
- `curl http://localhost:5210/api/portfolio/...` for the phase's endpoints; open `map.svg` / `portfolio.xlsx`.
- Existing flows regression-checked: Products page, product tabs, `.rmp` export/import, PDF export.
- Commit per phase on the feature branch, then stop for user review.
