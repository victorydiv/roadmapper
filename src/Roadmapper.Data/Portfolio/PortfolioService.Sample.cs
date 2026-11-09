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
        return !await db.Applications.AnyAsync() && !await db.Vendors.AnyAsync() && !await db.Contacts.AnyAsync();
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
    }
}
