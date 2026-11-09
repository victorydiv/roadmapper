using Microsoft.EntityFrameworkCore;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Data.Portfolio;

public partial class PortfolioService
{
    /// <summary>Title of the product <see cref="DbInitializer"/> creates on a new database.</summary>
    public const string SampleProductName = "Sample: Customer Portal";

    /// <summary>True when there are no applications, vendors or contacts yet.</summary>
    public async Task<bool> IsEmptyAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return !await db.Applications.AnyAsync() && !await db.Vendors.AnyAsync() && !await db.Contacts.AnyAsync()
            && !await db.TradingPartners.AnyAsync() && !await db.BusinessProcesses.AnyAsync() && !await db.Benefits.AnyAsync() && !await db.Decisions.AnyAsync();
    }

    /// <summary>
    /// Adds a small, made-up 3PL portfolio to explore. Only allowed while the portfolio is empty. When the sample
    /// product exists, its customer-portal application is linked to it and to two of its initiatives.
    /// </summary>
    public async Task SeedSampleAsync()
    {
        if (!await IsEmptyAsync()) throw new InvalidOperationException("The sample can only be added to an empty portfolio.");
        var today = Today;

        Task<int> Person(string name, string title, int? vendorId = null, string? dept = null, string? email = null) =>
            SaveContactAsync(new ContactDraft { Name = name, Title = title, VendorId = vendorId, Department = dept, Email = email });

        // Internal people.
        var opsDirector = await Person("Jordan Ruiz", "Director of Operations", dept: "Operations");
        var appsLead = await Person("Priya Shah", "IT Applications Lead", dept: "IT");
        var integrationLead = await Person("Marcus Bell", "Integration Engineer", dept: "IT");
        var finance = await Person("Dana Kim", "Finance Systems Manager", dept: "Finance");
        var custService = await Person("Alex Moreno", "Customer Service Manager", dept: "Customer Service");

        // Vendors, their people and contracts. All names are made up.
        async Task<(int Vendor, int Contract, int Tech)> VendorWith(string name, string website, string am, string tech,
            string contractName, int renewInDays, int noticeDays, bool autoRenews, decimal annual)
        {
            var vendorId = await SaveVendorAsync(new VendorDraft { Name = name, Website = website, SupportPhone = "1-800-555-0100" });
            var amId = await Person(am, "Account Manager", vendorId, email: $"{am.Split(' ')[0].ToLowerInvariant()}@example.com");
            var techId = await Person(tech, "Solutions Engineer", vendorId);
            await SaveVendorAsync(new VendorDraft { Id = vendorId, Name = name, Website = website, SupportPhone = "1-800-555-0100", AccountManagerId = amId, TechnicalContactId = techId });
            await AddEscalationContactAsync(vendorId, amId, "First call for commercial or support escalations");
            var renewal = today.AddDays(renewInDays);
            var contractId = await SaveContractAsync(new ContractDraft
            {
                VendorId = vendorId, Name = contractName, StartDate = renewal.AddYears(-3), EndDate = renewal.AddDays(-1),
                RenewalDate = renewal, NoticePeriodDays = noticeDays, AutoRenews = autoRenews, AnnualValue = annual,
            });
            return (vendorId, contractId, techId);
        }

        var northwind = await VendorWith("Northwind Supply Chain Software", "https://northwind.example.com", "Taylor Brooks", "Chris Novak",
            "WMS SaaS subscription", renewInDays: 150, noticeDays: 90, autoRenews: true, annual: 480_000m);
        var fabrikam = await VendorWith("Fabrikam Freight Systems", "https://fabrikam.example.com", "Morgan Patel", "Riley Chen",
            "TMS subscription", renewInDays: 420, noticeDays: 90, autoRenews: false, annual: 310_000m);
        var contoso = await VendorWith("Contoso EDI Services", "https://contoso.example.com", "Jamie Fox", "Pat Quinn",
            "Managed EDI / VAN services", renewInDays: 45, noticeDays: 30, autoRenews: true, annual: 96_000m);
        var litware = await VendorWith("Litware Business Systems", "https://litware.example.com", "Casey Wong", "Drew Ellis",
            "ERP cloud license", renewInDays: 700, noticeDays: 180, autoRenews: false, annual: 650_000m);

        var vpNorthwind = await Person("Avery Stone", "VP Customer Success", northwind.Vendor);
        await AddEscalationContactAsync(northwind.Vendor, vpNorthwind, "When an outage passes 4 hours");

        Task<int> App(string name, string description, AppCategory category, Criticality tier, (int Vendor, int Contract, int Tech)? vendor,
            decimal? cost, int businessFit, int technicalFit, int? businessOwner, int? technicalOwner,
            LifecycleStatus status = LifecycleStatus.Active, Hosting hosting = Hosting.SaaS, int? productId = null) =>
            SaveApplicationAsync(new ApplicationDraft
            {
                Name = name, Description = description, Category = category, Criticality = tier, Status = status, Hosting = hosting,
                VendorId = vendor?.Vendor, ContractId = vendor?.Contract, AnnualCost = cost, BusinessFit = businessFit, TechnicalFit = technicalFit,
                BusinessOwnerId = businessOwner, TechnicalOwnerId = technicalOwner, ProductId = productId,
            });

        int? sampleProduct;
        await using (var db = await dbFactory.CreateDbContextAsync())
            sampleProduct = await db.Products.Where(p => p.Name == SampleProductName).Select(p => (int?)p.Id).FirstOrDefaultAsync();

        var wms = await App("Northwind WMS", "Warehouse management for all distribution centers: receiving, putaway, waves, picking and shipping.",
            AppCategory.WMS, Criticality.Tier1, northwind, 480_000m, 4, 3, opsDirector, appsLead);
        await App("Fabrikam TMS", "Load planning, carrier tendering, freight audit and track & trace.",
            AppCategory.TMS, Criticality.Tier1, fabrikam, 310_000m, 4, 2, opsDirector, integrationLead);
        var erp = await App("Litware ERP", "Finance, billing and customer master data.",
            AppCategory.ERP, Criticality.Tier1, litware, 650_000m, 3, 4, finance, appsLead);
        await App("Contoso EDI", "EDI translation and VAN for 940/945/856/214/810 with customers and carriers.",
            AppCategory.EDI, Criticality.Tier1, contoso, 96_000m, 3, 3, custService, integrationLead);
        var portal = await App("Customer Portal", "Self-service order entry, inventory visibility and invoices for 3PL customers.",
            AppCategory.CustomerPortal, Criticality.Tier2, null, 120_000m, 4, 4, custService, appsLead, hosting: Hosting.Hosted, productId: sampleProduct);
        await App("Operations Reporting", "Shared dashboards for throughput, labor and on-time shipping.",
            AppCategory.Reporting, Criticality.Tier3, null, 24_000m, 2, 3, opsDirector, appsLead);
        await App("Yard Log Spreadsheet", "Spreadsheet the gate uses to log trailers. To be replaced by a yard management module.",
            AppCategory.Other, Criticality.Tier3, null, null, 1, 1, opsDirector, null, LifecycleStatus.Retiring, Hosting.OnPrem);

        await AddSupportContactAsync(wms, appsLead, SupportRole.L2Support, "Configuration and wave templates");
        await AddSupportContactAsync(wms, northwind.Tech, SupportRole.VendorSupport, null);
        await AddSupportContactAsync(erp, finance, SupportRole.KeyUser, "Billing runs");

        if (sampleProduct is { } pid)
        {
            var items = await GetLinkableItemsAsync(pid);
            if (items.FirstOrDefault(i => i.Title == "Frictionless onboarding") is { } onboarding)
                await LinkItemAsync(portal, onboarding.Id, InitiativeImpact.Implements);
            if (items.FirstOrDefault(i => i.Title == "Billing self-service") is { } billing)
            {
                await LinkItemAsync(portal, billing.Id, InitiativeImpact.Enhances);
                await LinkItemAsync(erp, billing.Id, InitiativeImpact.Integrates);
            }
        }

        await SeedSampleIntegrationsAsync();
        await SeedSampleProcessesAsync();
        if (await CanSeedSampleBenefitsAsync()) await SeedSampleBenefitsAsync();
        if (await CanSeedSampleDecisionsAsync()) await SeedSampleDecisionsAsync();
    }

    // The sample applications the sample integrations connect; they must all exist.
    private static readonly string[] SampleIntegrationApps =
        ["Northwind WMS", "Fabrikam TMS", "Litware ERP", "Contoso EDI", "Customer Portal", "Operations Reporting", "Yard Log Spreadsheet"];

    /// <summary>True when there are no integrations yet and the sample applications are all present.</summary>
    public async Task<bool> CanSeedSampleIntegrationsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Integrations.AnyAsync()) return false;
        return await db.Applications.CountAsync(a => SampleIntegrationApps.Contains(a.Name)) == SampleIntegrationApps.Length;
    }

    /// <summary>
    /// Adds made-up integrations between the sample applications, plus three trading partners (two customers and a
    /// carrier) so EDI flows have both ends. Only while there are no integrations.
    /// </summary>
    public async Task SeedSampleIntegrationsAsync()
    {
        if (!await CanSeedSampleIntegrationsAsync())
            throw new InvalidOperationException("Sample integrations need the sample applications and an empty integration catalog.");
        var today = Today;

        Dictionary<string, Application> apps;
        Dictionary<string, int> people;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            apps = await db.Applications.AsNoTracking().ToDictionaryAsync(a => a.Name, StringComparer.OrdinalIgnoreCase);
            people = (await db.Contacts.AsNoTracking().Where(c => c.VendorId == null).ToListAsync())
                .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Id);
        }

        int? Who(string name) => people.TryGetValue(name, out var id) ? id : null;
        NodeRef App(string name) => NodeRef.App(apps[name].Id);

        async Task<NodeRef> Partner(string name, PartnerType type, PartnerImportance importance, string code, string qualifier, string ediId,
            string? owner, string? contact = null, string? contactTitle = null)
        {
            int id;
            await using (var db = await dbFactory.CreateDbContextAsync())
                id = await db.TradingPartners.Where(p => p.Name == name).Select(p => p.Id).FirstOrDefaultAsync();
            if (id != 0) return NodeRef.Partner(id);
            id = await SavePartnerAsync(new TradingPartnerDraft
            {
                Name = name, Type = type, Importance = importance, AccountCode = code, EdiQualifier = qualifier, EdiId = ediId,
                RelationshipOwnerId = owner is null ? null : Who(owner),
            });
            if (contact is not null)
                await SaveContactAsync(new ContactDraft { Name = contact, Title = contactTitle, TradingPartnerId = id });
            return NodeRef.Partner(id);
        }

        var acme = await Partner("Acme Retail", PartnerType.Customer, PartnerImportance.Key, "ACME01", "01", "123456789",
            "Alex Moreno", "Sam Lee", "EDI Coordinator");
        var globex = await Partner("Globex Home Goods", PartnerType.Customer, PartnerImportance.Standard, "GLBX", "ZZ", "GLOBEXHOME", "Alex Moreno");
        var blueRidge = await Partner("Blue Ridge Freight", PartnerType.Carrier, PartnerImportance.Key, "BRFT", "02", "BRFT", "Jordan Ruiz",
            "Kim Alvarez", "Carrier IT Support");
        var (wms, tms, erp, portal, reporting, yard) = (App("Northwind WMS"), App("Fabrikam TMS"), App("Litware ERP"),
            App("Customer Portal"), App("Operations Reporting"), App("Yard Log Spreadsheet"));
        var edi = apps["Contoso EDI"].Id;

        Task<int> Flow(string name, NodeRef source, NodeRef target, IntegrationType type, IntegrationFrequency frequency, Criticality tier,
            string purpose, string? format = null, int? via = null, FlowDirection direction = FlowDirection.OneWay,
            string? owner = "Marcus Bell", int reviewedMonthsAgo = 3) =>
            SaveIntegrationAsync(new IntegrationDraft
            {
                Name = name, SourceKey = source.Key, TargetKey = target.Key, ViaApplicationId = via, Type = type,
                Frequency = frequency, Criticality = tier, BusinessPurpose = purpose, MessageFormat = format, Direction = direction,
                OwnerId = owner is null ? null : Who(owner), LastReviewDate = today.AddMonths(-reviewedMonthsAgo),
            });

        Task Risk(int integration, string description, int likelihood, int impact, string? mitigation = null, string? owner = "Marcus Bell") =>
            SaveRiskAsync(new RiskDraft
            {
                IntegrationId = integration, Description = description, Likelihood = likelihood, Impact = impact,
                Mitigation = mitigation, OwnerId = owner is null ? null : Who(owner), RaisedOn = today.AddDays(-40),
            });

        await Flow("Acme warehouse orders (940)", acme, wms, IntegrationType.EDI, IntegrationFrequency.NearRealTime, Criticality.Tier1,
            "Acme releases store and e-commerce orders to the warehouse for picking and shipping.", "X12 940", via: edi);
        var asn = await Flow("Acme ship confirmations (945)", wms, acme, IntegrationType.EDI, IntegrationFrequency.NearRealTime, Criticality.Tier1,
            "Tells Acme what shipped, so they can bill their customers and update inventory.", "X12 945", via: edi);
        await Risk(asn, "Trading-partner map changes go live without regression testing.", 3, 5,
            "Add a test partner and replay last week's 945s before each map change.");
        await Flow("Order release for load planning", wms, tms, IntegrationType.API, IntegrationFrequency.NearRealTime, Criticality.Tier1,
            "Sends released orders to the TMS to build loads and tender to carriers.", "JSON");
        await Flow("Globex warehouse orders (940)", globex, wms, IntegrationType.EDI, IntegrationFrequency.Hourly, Criticality.Tier2,
            "Globex sends replenishment orders for its home-goods stores.", "X12 940", via: edi);
        await Flow("Load tenders (204)", tms, blueRidge, IntegrationType.EDI, IntegrationFrequency.RealTime, Criticality.Tier2,
            "Offers loads to Blue Ridge Freight.", "X12 204", via: edi);
        await Flow("Shipment status (214)", blueRidge, tms, IntegrationType.EDI, IntegrationFrequency.Hourly, Criticality.Tier2,
            "Carrier pickup, in-transit and delivery events for track & trace.", "X12 214", via: edi, reviewedMonthsAgo: 16);
        var billing = await Flow("Billable activity for invoicing", wms, erp, IntegrationType.Batch, IntegrationFrequency.Daily, Criticality.Tier1,
            "Nightly handling, storage and accessorial charges for customer invoicing.", "CSV", owner: "Dana Kim");
        await Risk(billing, "The nightly file can fail without an alert, delaying invoices.", 3, 4,
            "Alert when the file hasn't landed by 6 a.m.", owner: "Dana Kim");
        await Flow("Invoices and statements", erp, portal, IntegrationType.API, IntegrationFrequency.Hourly, Criticality.Tier2,
            "Publishes invoices and statements to customers in the portal.", "JSON");
        await Flow("Self-service orders and inventory", portal, wms, IntegrationType.API, IntegrationFrequency.RealTime, Criticality.Tier2,
            "Customers enter orders in the portal; inventory levels flow back.", "JSON", direction: FlowDirection.Bidirectional);
        await Flow("Throughput and labor extract", wms, reporting, IntegrationType.Batch, IntegrationFrequency.Daily, Criticality.Tier3,
            "Feeds the operations dashboards.", "CSV", owner: "Priya Shah");
        var gate = await Flow("Trailer arrivals keyed into WMS", yard, wms, IntegrationType.Manual, IntegrationFrequency.OnDemand, Criticality.Tier3,
            "Gate staff re-key trailer arrivals so receiving can schedule doors.", owner: "Jordan Ruiz", reviewedMonthsAgo: 14);
        await Risk(gate, "Typos in trailer numbers delay receiving.", 4, 2, "Replace with the yard management initiative.", owner: "Jordan Ruiz");
    }

    // The sample integrations the sample processes rely on.
    private static readonly string[] SampleProcessIntegrations =
        ["Acme warehouse orders (940)", "Globex warehouse orders (940)", "Self-service orders and inventory", "Order release for load planning",
         "Acme ship confirmations (945)", "Billable activity for invoicing", "Invoices and statements", "Trailer arrivals keyed into WMS",
         "Load tenders (204)", "Shipment status (214)"];

    /// <summary>True when there are no processes yet and the sample applications and integrations are all present.</summary>
    public async Task<bool> CanSeedSampleProcessesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.BusinessProcesses.AnyAsync()) return false;
        return await db.Applications.CountAsync(a => SampleIntegrationApps.Contains(a.Name)) == SampleIntegrationApps.Length
            && await db.Integrations.Select(i => i.Name).Distinct().CountAsync(n => SampleProcessIntegrations.Contains(n)) == SampleProcessIntegrations.Length;
    }

    /// <summary>
    /// Adds made-up business processes over the sample applications and integrations: Order to Cash with three steps,
    /// Inbound with receiving, and Transportation, each with pain points and KPIs. Only while there are no processes.
    /// </summary>
    public async Task SeedSampleProcessesAsync()
    {
        if (!await CanSeedSampleProcessesAsync())
            throw new InvalidOperationException("Sample processes need the sample applications and integrations, and no processes yet.");
        var today = Today;

        Dictionary<string, int> apps, flows, people;
        int? billingItem = null;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            apps = await db.Applications.ToDictionaryAsync(a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);
            flows = (await db.Integrations.Select(i => new { i.Name, i.Id }).ToListAsync())
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Min(i => i.Id), StringComparer.OrdinalIgnoreCase);
            people = (await db.Contacts.AsNoTracking().Where(c => c.VendorId == null && c.TradingPartnerId == null).ToListAsync())
                .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Id);
            billingItem = await db.Items.Where(i => i.Title == "Billing self-service" && i.Product!.Name == SampleProductName)
                .Select(i => (int?)i.Id).FirstOrDefaultAsync();
        }
        int? Who(string name) => people.TryGetValue(name, out var id) ? id : null;

        async Task<int> Process(string name, ProcessDomain domain, string owner, string description, int? parent = null,
            (string App, string Usage, bool Primary)[]? uses = null, string[]? relies = null)
        {
            var id = await SaveProcessAsync(new ProcessDraft { Name = name, Domain = domain, ProcessOwnerId = Who(owner), Description = description, ParentProcessId = parent });
            foreach (var (app, usage, primary) in uses ?? []) await SetProcessApplicationAsync(id, apps[app], usage, primary);
            foreach (var flow in relies ?? []) await AddProcessIntegrationAsync(id, flows[flow]);
            return id;
        }

        Task Pain(int process, string description, PainSeverity severity, string? app = null, int? addressedBy = null) =>
            SavePainPointAsync(new PainPointDraft
            {
                ProcessId = process, Description = description, Severity = severity,
                ApplicationId = app is null ? null : apps[app], AddressedByItemId = addressedBy,
            });

        Task Kpi(int process, string name, string unit, KpiDirection direction, decimal baseline, decimal target, decimal current) =>
            SaveKpiAsync(new KpiDraft
            {
                ProcessId = process, Name = name, Unit = unit, Direction = direction, Baseline = baseline, Target = target,
                Current = current, MeasuredOn = today.AddDays(-7),
            });

        var o2c = await Process("Order to Cash", ProcessDomain.Outbound, "Jordan Ruiz",
            "From a customer's order to the invoice: intake, picking and shipping, billing.");
        var intake = await Process("Order intake", ProcessDomain.CustomerService, "Alex Moreno",
            "Customer orders arrive by EDI or the portal and are released to the warehouse.", o2c,
            [("Contoso EDI", "Translates 940 orders", false), ("Customer Portal", "Self-service order entry", false), ("Northwind WMS", "Order release", true)],
            ["Acme warehouse orders (940)", "Globex warehouse orders (940)", "Self-service orders and inventory"]);
        await Pain(intake, "Orders with unknown SKUs fail in EDI translation and aren't noticed until the next day.", PainSeverity.High, "Contoso EDI");
        await Kpi(intake, "Orders released same day", "%", KpiDirection.HigherIsBetter, 92, 98, 95);

        var ship = await Process("Pick, pack and ship", ProcessDomain.Outbound, "Jordan Ruiz",
            "Waves, picking, packing, loading and ship confirmation.", o2c,
            [("Northwind WMS", "Waves, picking, shipping", true), ("Fabrikam TMS", "Load planning", false)],
            ["Order release for load planning", "Acme ship confirmations (945)"]);
        await Pain(ship, "Wave planning is done by hand on peak days.", PainSeverity.Medium, "Northwind WMS");
        await Kpi(ship, "On-time shipping", "%", KpiDirection.HigherIsBetter, 94, 99, 96.5m);

        var invoicing = await Process("Customer invoicing", ProcessDomain.Billing, "Dana Kim",
            "Handling, storage and accessorial charges billed to customers.", o2c,
            [("Litware ERP", "Billing runs", true), ("Northwind WMS", "Billable activity", false), ("Customer Portal", "Invoice delivery", false)],
            ["Billable activity for invoicing", "Invoices and statements"]);
        await Pain(invoicing, "Accessorial charges are keyed by hand before each billing run.", PainSeverity.High, "Litware ERP", billingItem);
        await Kpi(invoicing, "Days to invoice", "days", KpiDirection.LowerIsBetter, 6, 2, 4);

        var inbound = await Process("Inbound", ProcessDomain.Inbound, "Jordan Ruiz", "Trailers arriving, receiving and putaway.");
        var receiving = await Process("Receiving", ProcessDomain.Inbound, "Jordan Ruiz",
            "Door scheduling, unloading and receipt into inventory.", inbound,
            [("Northwind WMS", "Receipts and putaway", true), ("Yard Log Spreadsheet", "Trailer check-in", false)],
            ["Trailer arrivals keyed into WMS"]);
        await Pain(receiving, "Trailer numbers are re-keyed from the yard log, and typos delay door scheduling.", PainSeverity.Medium, "Yard Log Spreadsheet");
        await Kpi(receiving, "Dock-to-stock time", "hrs", KpiDirection.LowerIsBetter, 24, 8, 18);

        var transport = await Process("Transportation", ProcessDomain.Transportation, "Jordan Ruiz",
            "Tendering loads to carriers and tracking them to delivery.", null,
            [("Fabrikam TMS", "Tendering, track & trace", true)],
            ["Load tenders (204)", "Shipment status (214)"]);
        await Pain(transport, "Carrier status messages arrive late, so customer service can't answer where-is-my-order calls.", PainSeverity.Medium, "Fabrikam TMS");
        await Kpi(transport, "Tender acceptance", "%", KpiDirection.HigherIsBetter, 85, 95, 88);
    }

    /// <summary>True when there are no benefits yet and the sample roadmap's initiatives are there to carry them.</summary>
    public async Task<bool> CanSeedSampleBenefitsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Benefits.AnyAsync()) return false;
        return await db.Items.CountAsync(i => i.Product!.Name == SampleProductName
            && (i.Title == "Billing self-service" || i.Title == "Frictionless onboarding")) == 2;
    }

    /// <summary>
    /// Adds made-up benefits to the sample roadmap's initiatives, with a few months of measurements: labor hours saved
    /// by billing self-service, portal adoption, and same-day order release measured by the sample process KPI.
    /// Sets a labor rate when none is set. Only while there are no benefits.
    /// </summary>
    public async Task SeedSampleBenefitsAsync()
    {
        if (!await CanSeedSampleBenefitsAsync())
            throw new InvalidOperationException("Sample benefits need the sample roadmap and no benefits yet.");
        var today = Today;

        int billing, onboarding;
        int? erp, portal, sameDayKpi;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var items = await db.Items.Where(i => i.Product!.Name == SampleProductName).Select(i => new { i.Id, i.Title }).ToListAsync();
            billing = items.First(i => i.Title == "Billing self-service").Id;
            onboarding = items.First(i => i.Title == "Frictionless onboarding").Id;
            erp = await db.Applications.Where(a => a.Name == "Litware ERP").Select(a => (int?)a.Id).FirstOrDefaultAsync();
            portal = await db.Applications.Where(a => a.Name == "Customer Portal").Select(a => (int?)a.Id).FirstOrDefaultAsync();
            sameDayKpi = await db.ProcessKpis.Where(k => k.Name == "Orders released same day").Select(k => (int?)k.Id).FirstOrDefaultAsync();
            if (await db.PortfolioSettings.Select(s => s.LaborRate).FirstOrDefaultAsync() is null) await SetLaborRateAsync(32m);
        }

        async Task Benefit(int item, int? app, BenefitCategory category, string description, BenefitUnit unit, decimal expected,
            int expectedInMonths, decimal? baseline = null, int? kpi = null, params (int MonthsAgo, decimal Value)[] readings)
        {
            var id = await SaveBenefitAsync(new BenefitDraft
            {
                RoadmapItemId = item, ApplicationId = app, Category = category, Description = description, Unit = unit,
                ExpectedValue = expected, ExpectedBy = today.AddMonths(expectedInMonths), Baseline = baseline, KpiId = kpi,
            });
            foreach (var (monthsAgo, value) in readings)
                await SaveMeasurementAsync(id, today.AddMonths(-monthsAgo), value, "Finance monthly close");
        }

        await Benefit(billing, erp, BenefitCategory.LaborSavings, "Billing clerks stop keying accessorial charges by hand.",
            BenefitUnit.Hours, 1_040, 4, readings: [(3, 240), (2, 410), (1, 520)]);
        await Benefit(billing, erp, BenefitCategory.CostSavings, "Fewer billing disputes and credit memos.",
            BenefitUnit.USD, 60_000, -1, readings: [(2, 9_000), (1, 14_000)]);
        await Benefit(onboarding, portal, BenefitCategory.Adoption, "Customers placing orders in the portal instead of by email.",
            BenefitUnit.Percent, 60, 6, baseline: 20, readings: [(2, 28), (1, 34)]);
        if (sameDayKpi is { } k)
            await Benefit(onboarding, portal, BenefitCategory.Other, "More orders released to the warehouse the day they arrive.",
                BenefitUnit.Percent, 98, 3, kpi: k);
    }

    /// <summary>True when there are no decisions yet and the sample EDI, yard log and WMS applications are there to link them to.</summary>
    public async Task<bool> CanSeedSampleDecisionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Decisions.AnyAsync()) return false;
        return await db.Applications.CountAsync(a => a.Name == "Contoso EDI" || a.Name == "Yard Log Spreadsheet" || a.Name == "Northwind WMS") == 3;
    }

    /// <summary>Adds three made-up decisions to the sample portfolio, with alternatives and links. Only while there are no decisions.</summary>
    public async Task SeedSampleDecisionsAsync()
    {
        if (!await CanSeedSampleDecisionsAsync())
            throw new InvalidOperationException("Sample decisions need the sample applications and no decisions yet.");
        var today = Today;

        Dictionary<string, int> apps, vendors, processes, flows;
        int? ediContract;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            apps = await db.Applications.ToDictionaryAsync(a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);
            vendors = await db.Vendors.ToDictionaryAsync(v => v.Name, v => v.Id, StringComparer.OrdinalIgnoreCase);
            processes = await db.BusinessProcesses.ToDictionaryAsync(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);
            flows = (await db.Integrations.Select(i => new { i.Name, i.Id }).ToListAsync())
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Min(i => i.Id), StringComparer.OrdinalIgnoreCase);
            ediContract = await db.Contracts.Where(c => c.Name == "Managed EDI / VAN services").Select(c => (int?)c.Id).FirstOrDefaultAsync();
        }

        async Task<int> Decide(string title, int monthsAgo, DecisionStatus status, string context, string decision, string? decidedBy,
            string? outcome, AlternativeDraft[] alternatives, params DecisionTarget?[] links)
        {
            var id = await SaveDecisionAsync(new DecisionDraft
            {
                Title = title, DecidedOn = today.AddMonths(-monthsAgo), Status = status, Context = context, DecisionText = decision,
                DecidedBy = decidedBy, Outcome = outcome, Alternatives = alternatives.ToList(),
            });
            foreach (var link in links.OfType<DecisionTarget>()) await LinkDecisionAsync(id, link);
            return id;
        }
        DecisionTarget? App(string n) => apps.TryGetValue(n, out var id) ? new DecisionTarget(DecisionTargetKind.Application, id) : null;
        DecisionTarget? Vendor(string n) => vendors.TryGetValue(n, out var id) ? new DecisionTarget(DecisionTargetKind.Vendor, id) : null;
        DecisionTarget? Process(string n) => processes.TryGetValue(n, out var id) ? new DecisionTarget(DecisionTargetKind.Process, id) : null;
        DecisionTarget? Flow(string n) => flows.TryGetValue(n, out var id) ? new DecisionTarget(DecisionTargetKind.Integration, id) : null;

        var tenderByEdi = await Decide("Tender loads to carriers over EDI 204", 30, DecisionStatus.Decided,
            "Carriers asked for electronic tenders; phone and email tendering was slow and error-prone.",
            "Send load tenders from the TMS to carriers as X12 204 through the EDI provider.",
            "Transportation steering group", "Tender acceptance rose from 70% to 85% within six months.",
            [new() { Option = "Carrier portal tendering", Pros = "No EDI setup per carrier", Cons = "Dispatchers key every load twice", WhyRejected = "Doesn't scale past a handful of carriers" }],
            Flow("Load tenders (204)"), App("Fabrikam TMS"), Process("Transportation"));

        await Decide("Keep Contoso as the EDI provider through the next renewal", 2, DecisionStatus.Decided,
            "The managed EDI contract renews soon. Two other providers were quoted.",
            "Renew with Contoso for two years and negotiate a 10% volume discount; revisit API-based exchange with the WMS upgrade.",
            "Priya Shah (IT Applications Lead)", null,
            [
                new() { Option = "Move to another managed EDI provider", Pros = "About 15% cheaper", Cons = "Re-mapping and re-testing every trading partner", WhyRejected = "Migration risk for Tier 1 order and ship-confirm flows during peak" },
                new() { Option = "Bring EDI in-house on the integration platform", Pros = "No per-document fees", Cons = "Needs EDI skills the team doesn't have", WhyRejected = "Not until the platform team is staffed" },
            ],
            App("Contoso EDI"), Vendor("Contoso EDI Services"),
            ediContract is { } c ? new DecisionTarget(DecisionTargetKind.Contract, c) : null);

        await Decide("Replace the yard log spreadsheet with the WMS yard module", 0, DecisionStatus.Proposed,
            "Gate staff log trailers in a spreadsheet and re-key arrivals into the WMS; typos delay door scheduling.",
            "License the WMS yard management module and retire the spreadsheet after one peak season in parallel.",
            null, null,
            [new() { Option = "Standalone yard management system", Pros = "Richer features", Cons = "Another integration and vendor", WhyRejected = "More than a single site needs" }],
            App("Yard Log Spreadsheet"), App("Northwind WMS"), Process("Receiving"));

        // An earlier decision the EDI one replaced, to show the chain.
        var phone = await Decide("Tender loads by phone and email", 48, DecisionStatus.Superseded,
            "Too few carriers to justify EDI.", "Dispatchers tender loads by phone and email from the TMS load board.",
            "Transportation steering group", "Worked until volume doubled.", [], App("Fabrikam TMS"));
        var draft = DecisionDraft.From((await GetDecisionAsync(phone))!);
        draft.SupersededById = tenderByEdi;
        await SaveDecisionAsync(draft);
    }
}
