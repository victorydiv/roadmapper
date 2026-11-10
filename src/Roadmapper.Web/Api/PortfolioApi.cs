using Roadmapper.Core;
using Roadmapper.Core.Portfolio;
using Roadmapper.Data.Portfolio;
using Roadmapper.Web.Exports;

namespace Roadmapper.Web.Api;

/// <summary>
/// Read-only JSON over the portfolio, for Power BI, Excel (Get Data › From Web) and scripts. Every endpoint is a GET
/// that changes nothing, so an unauthenticated localhost app can serve it safely. The UI doesn't use it; Razor pages
/// call <see cref="PortfolioService"/> directly.
/// </summary>
public static class PortfolioApi
{
    public static void MapPortfolioApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/portfolio");

        // ?status=Active&category=WMS&tier=Tier1 (or 1). Unknown values are ignored rather than rejected.
        api.MapGet("/applications", async (PortfolioService svc, string? status, string? category, string? tier) =>
        {
            IEnumerable<Application> apps = await svc.GetApplicationsAsync();
            if (Parse<LifecycleStatus>(status) is { } s) apps = apps.Where(a => a.Status == s);
            if (Parse<AppCategory>(category) is { } c) apps = apps.Where(a => a.Category == c);
            if (ParseTier(tier) is { } t) apps = apps.Where(a => a.Criticality == t);
            return apps.Select(ApplicationDto.From).ToList();
        });

        api.MapGet("/applications/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetApplicationAsync(id) is { } a
                ? Results.Ok(ApplicationDetailDto.From(a, await svc.GetLinkedItemsAsync(id)))
                : Results.NotFound());

        api.MapGet("/vendors", async (PortfolioService svc) =>
            (await svc.GetVendorsAsync()).Select(v => VendorDto.From(v, svc.Today)).ToList());

        api.MapGet("/vendors/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetVendorAsync(id) is { } v ? Results.Ok(VendorDetailDto.From(v, svc.Today)) : Results.NotFound());

        // Contracts whose notice deadline is within N days (default: the portfolio's renewal warning setting).
        api.MapGet("/contracts", async (PortfolioService svc, int? renewingWithinDays) =>
            (await svc.GetRenewalsAsync(renewingWithinDays)).Select(r => new RenewalDto(ContractDto.From(r.Contract), r.VendorName, r.Applications)).ToList());

        api.MapGet("/contacts", async (PortfolioService svc) =>
            (await svc.GetContactsAsync()).Select(ContactDto.From).ToList());

        // ?risk=high&type=EDI&applicationId=3 (as source, target or via)
        api.MapGet("/integrations", async (PortfolioService svc, string? risk, string? type, int? applicationId) =>
        {
            var today = svc.Today;
            IEnumerable<Integration> list = applicationId is { } appId
                ? await svc.GetIntegrationsForApplicationAsync(appId)
                : await svc.GetIntegrationsAsync();
            if (Parse<RiskLevel>(risk) is { } r) list = list.Where(i => PortfolioRules.RiskLevel(i, today) == r);
            if (Parse<IntegrationType>(type) is { } t) list = list.Where(i => i.Type == t);
            return list.Select(i => IntegrationDto.From(i, today)).ToList();
        });

        api.MapGet("/integrations/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetIntegrationAsync(id) is { } i ? Results.Ok(IntegrationDetailDto.From(i, svc.Today)) : Results.NotFound());

        // Upstream: what sends data to this application. Downstream: what it sends data to. depth = 1–5 integrations away.
        api.MapGet("/applications/{id:int}/dependencies", async (int id, PortfolioService svc, int? depth) =>
            await svc.GetDependenciesAsync(NodeRef.App(id), depth ?? 2) is { } view ? Results.Ok(DependencyDto.From(view, svc.Today)) : Results.NotFound());

        // Customers, carriers, suppliers. ?type=Customer&status=Active
        api.MapGet("/partners", async (PortfolioService svc, string? type, string? status) =>
        {
            IEnumerable<TradingPartner> list = await svc.GetPartnersAsync();
            if (Parse<PartnerType>(type) is { } t) list = list.Where(p => p.Type == t);
            if (Parse<PartnerStatus>(status) is { } s) list = list.Where(p => p.Status == s);
            return list.Select(PartnerDto.From).ToList();
        });

        api.MapGet("/partners/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetPartnerAsync(id) is { } p
                ? Results.Ok(new PartnerDetailDto(PartnerDto.From(p), p.Notes, p.Staff.Select(ContactDto.From).ToList(),
                    (await svc.GetIntegrationsForPartnerAsync(id)).Select(i => IntegrationDto.From(i, svc.Today)).ToList()))
                : Results.NotFound());

        // Which of your applications a partner depends on (downstream of its inbound flows, upstream of its outbound ones).
        api.MapGet("/partners/{id:int}/dependencies", async (int id, PortfolioService svc, int? depth) =>
            await svc.GetDependenciesAsync(NodeRef.Partner(id), depth ?? 2) is { } view ? Results.Ok(DependencyDto.From(view, svc.Today)) : Results.NotFound());

        // Business processes in tree order (parents first). ?domain=Outbound&applicationId=3 (uses that application)
        api.MapGet("/processes", async (PortfolioService svc, string? domain, int? applicationId) =>
        {
            IEnumerable<BusinessProcess> list = await svc.GetProcessesAsync();
            if (Parse<ProcessDomain>(domain) is { } d) list = list.Where(p => p.Domain == d);
            if (applicationId is { } appId) list = list.Where(p => p.Applications.Any(a => a.ApplicationId == appId));
            return list.Select(ProcessDto.From).ToList();
        });

        api.MapGet("/processes/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetProcessAsync(id) is { } p ? Results.Ok(ProcessDetailDto.From(p, svc.Today)) : Results.NotFound());

        // Change impact: what a change to the application or partner touches. depth = 1–5 steps downstream.
        // ?format=text returns the plain-text report (for email or a ticket) instead of JSON.
        api.MapGet("/applications/{id:int}/impact", async (int id, PortfolioService svc, int? depth, string? format) =>
            Impact(await svc.GetImpactAsync(NodeRef.App(id), depth ?? ImpactAnalyzer.DefaultDepth), format));

        api.MapGet("/partners/{id:int}/impact", async (int id, PortfolioService svc, int? depth, string? format) =>
            Impact(await svc.GetImpactAsync(NodeRef.Partner(id), depth ?? ImpactAnalyzer.DefaultDepth), format));

        // Benefits with expected, actual, realization and dollar values. ?productId=&itemId=&applicationId=&status=AtRisk
        api.MapGet("/benefits", async (PortfolioService svc, int? productId, int? itemId, int? applicationId, string? status) =>
        {
            var today = svc.Today;
            var rate = (await svc.GetSettingsAsync()).LaborRate;
            IEnumerable<Benefit> list = await svc.GetBenefitsAsync(productId, itemId, applicationId);
            if (Parse<BenefitStatus>(status) is { } s) list = list.Where(b => BenefitMath.Status(b, today) == s);
            var rows = list.ToList();
            return new BenefitsDto(rate, BenefitMath.Totals(rows, rate, today), rows.Select(b => BenefitDto.From(b, rate, today)).ToList());
        });

        // The executive dashboard: health, run cost, renewals, risky integrations, strategic work, value, recent decisions.
        api.MapGet("/dashboard", async (PortfolioService svc) => DashboardDto.From(await svc.GetDashboardAsync()));

        // The decision log, newest first. ?entityType=Application&entityId=3 (any DecisionTargetKind) &status=Decided
        api.MapGet("/decisions", async (PortfolioService svc, string? entityType, int? entityId, string? status) =>
        {
            DecisionTarget? target = Parse<DecisionTargetKind>(entityType) is { } k && entityId is { } id ? new DecisionTarget(k, id) : null;
            IEnumerable<Decision> list = await svc.GetDecisionsAsync(target);
            if (Parse<DecisionStatus>(status) is { } s) list = list.Where(d => d.Status == s);
            return list.Select(DecisionDto.From).ToList();
        });

        api.MapGet("/decisions/{id:int}", async (int id, PortfolioService svc) =>
            await svc.GetDecisionAsync(id) is { } d ? Results.Ok(DecisionDto.From(d)) : Results.NotFound());

        // Documents kept in SharePoint, newest first. ?productId=5 (documents belonging to that product)
        // &entityType=Application&entityId=3 (Application, Vendor, Contract, RoadmapItem) &category=Contract
        // (Roadmap, Contract, Invoice, Sow, Brd, Other).
        api.MapGet("/documents", async (PortfolioService svc, int? productId, string? entityType, int? entityId, string? category) =>
        {
            DocumentTarget? target = Parse<DocumentTargetKind>(entityType) is { } k && entityId is { } id ? new DocumentTarget(k, id) : null;
            return (await svc.GetDocumentsAsync(target, Parse<DocumentCategory>(category), productId)).Select(DocumentDto.From).ToList();
        });

        // Feature comparisons: every category's summary, and one category's matrix (features × applications with ratings).
        api.MapGet("/capabilities", async (PortfolioService svc) => await svc.GetCapabilityOverviewAsync());
        api.MapGet("/capabilities/{category}", async (string category, PortfolioService svc, bool? retired) =>
            Parse<AppCategory>(category) is { } c ? Results.Ok(CapabilityDto.From(await svc.GetCapabilityMatrixAsync(c, retired == true))) : Results.NotFound());

        // The integration map as SVG. ?focus={applicationId} or ?partner={partnerId} with depth=1–5 draws a dependency view;
        // type, tier (1–3: that tier and more critical), retired=true and partners=false (leave out trading partners)
        // filter; download=true saves it as a file.
        api.MapGet("/map.svg", async (PortfolioService svc, int? focus, int? partner, int? depth, string? type, string? tier, bool? retired, bool? partners, bool? download) =>
        {
            NodeRef? node = partner is { } pid ? NodeRef.Partner(pid) : focus is { } aid ? NodeRef.App(aid) : null;
            var options = new MapOptions(node, depth ?? 2, Parse<IntegrationType>(type), ParseTier(tier), retired == true, HidePartners: partners == false);
            var apps = await svc.GetApplicationsAsync();
            var partnerList = await svc.GetPartnersAsync();
            var name = node is { IsPartner: true } pn ? partnerList.FirstOrDefault(p => p.Id == pn.Id)?.Name
                : node is { } an ? apps.FirstOrDefault(a => a.Id == an.Id)?.Name : null;
            var title = name is null ? "Integration map" : $"{name}: dependencies";
            var picture = IntegrationMapSvg.Render(apps, partnerList, await svc.GetIntegrationsAsync(), options, svc.Today, title);
            var bytes = System.Text.Encoding.UTF8.GetBytes(picture.Svg);
            return download == true ? Results.File(bytes, "image/svg+xml", "integration-map.svg") : Results.Bytes(bytes, "image/svg+xml");
        });
    }

    private static IResult Impact(ImpactReport? report, string? format) => report is null
        ? Results.NotFound()
        : string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
            ? Results.Text(ImpactAnalyzer.ToText(report), "text/plain; charset=utf-8")
            : Results.Ok(ImpactDto.From(report));

    private static T? Parse<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : null;

    private static Criticality? ParseTier(string? value) =>
        int.TryParse(value, out var n) && Enum.IsDefined((Criticality)n) ? (Criticality)n : Parse<Criticality>(value);
}

public record Ref(int Id, string Name)
{
    public static Ref? Of(Contact? c) => c is null ? null : new(c.Id, c.Name);
    public static Ref? Of(Vendor? v) => v is null ? null : new(v.Id, v.Name);
    public static Ref? Of(Product? p) => p is null ? null : new(p.Id, p.Name);
}

public record ContractDto(int Id, int VendorId, string Name, string? ContractNumber, DateOnly? StartDate, DateOnly? EndDate,
    DateOnly? RenewalDate, int? NoticePeriodDays, DateOnly? NoticeDeadline, bool AutoRenews, decimal? AnnualValue)
{
    public static ContractDto? FromOrNull(Contract? c) => c is null ? null : From(c);

    public static ContractDto From(Contract c) => new(c.Id, c.VendorId, c.Name, c.ContractNumber, c.StartDate, c.EndDate,
        c.RenewalDate, c.NoticePeriodDays, c.NoticeDeadline, c.AutoRenews, c.AnnualValue);
}

public record ApplicationDto(int Id, string Name, string? Description, AppCategory Category, Criticality Criticality,
    LifecycleStatus Status, Hosting Hosting, Ref? Vendor, ContractDto? Contract, DateOnly? RenewalDate, DateOnly? NoticeDeadline,
    Ref? BusinessOwner, Ref? TechnicalOwner, Ref? Roadmap, decimal? AnnualCost, int? BusinessFit, int? TechnicalFit,
    TimeQuadrant? TimeQuadrant, DateOnly? GoLiveDate, DateOnly? RetireDate, int LinkedItemCount)
{
    public static ApplicationDto From(Application a) => new(a.Id, a.Name, a.Description, a.Category, a.Criticality, a.Status, a.Hosting,
        Ref.Of(a.Vendor), ContractDto.FromOrNull(a.Contract), a.RenewalDate, a.NoticeDeadline, Ref.Of(a.BusinessOwner), Ref.Of(a.TechnicalOwner),
        Ref.Of(a.Product), a.AnnualCost, a.BusinessFit, a.TechnicalFit, a.TimeQuadrant, a.GoLiveDate, a.RetireDate, a.Initiatives.Count);
}

public record SupportContactDto(Ref Contact, SupportRole Role, string? Note);

public record ApplicationDetailDto(ApplicationDto Application, string? Notes, IReadOnlyList<SupportContactDto> SupportContacts,
    IReadOnlyList<LinkedItem> LinkedItems)
{
    public static ApplicationDetailDto From(Application a, IReadOnlyList<LinkedItem> items) =>
        new(ApplicationDto.From(a) with { LinkedItemCount = items.Count }, a.Notes,
            a.SupportContacts.Select(s => new SupportContactDto(Ref.Of(s.Contact)!, s.Role, s.Note)).ToList(), items);
}

public record VendorDto(int Id, string Name, string? Website, Ref? AccountManager, Ref? TechnicalContact,
    DateOnly? ContractStart, DateOnly? ContractEnd, ContractDto? NextRenewal, decimal? AnnualContractValue, IReadOnlyList<Ref> Applications)
{
    public static VendorDto From(Vendor v, DateOnly today) => new(v.Id, v.Name, v.Website, Ref.Of(v.AccountManager), Ref.Of(v.TechnicalContact),
        PortfolioRules.ContractStart(v.Contracts), PortfolioRules.ContractEnd(v.Contracts),
        ContractDto.FromOrNull(PortfolioRules.NextRenewal(v.Contracts, today)),
        v.Contracts.Any(c => c.AnnualValue is not null) ? v.Contracts.Sum(c => c.AnnualValue ?? 0) : null,
        v.Applications.OrderBy(a => a.Name).Select(a => new Ref(a.Id, a.Name)).ToList());
}

public record EscalationDto(int Level, Ref Contact, string? Title, string? Email, string? Phone, string? Note);

public record VendorDetailDto(VendorDto Vendor, string? SupportPortalUrl, string? SupportPhone, string? Notes,
    IReadOnlyList<ContractDto> Contracts, IReadOnlyList<EscalationDto> EscalationPath, IReadOnlyList<ContactDto> Staff)
{
    public static VendorDetailDto From(Vendor v, DateOnly today) => new(VendorDto.From(v, today), v.SupportPortalUrl, v.SupportPhone, v.Notes,
        v.Contracts.Select(ContractDto.From).ToList(),
        v.EscalationContacts.OrderBy(x => x.Level)
            .Select(x => new EscalationDto(x.Level, Ref.Of(x.Contact)!, x.Contact?.Title, x.Contact?.Email, x.Contact?.Phone, x.Note)).ToList(),
        v.Staff.Select(ContactDto.From).ToList());
}

public record ContactDto(int Id, string Name, string? Title, string? Email, string? Phone, Ref? Vendor, string? Department, bool IsActive)
{
    public static ContactDto From(Contact c) => new(c.Id, c.Name, c.Title, c.Email, c.Phone, Ref.Of(c.Vendor), c.Department, c.IsActive);
}

public record RenewalDto(ContractDto Contract, string VendorName, IReadOnlyList<string> Applications);

public record RiskDto(int Id, string Description, int Likelihood, int Impact, int Score, RiskStatus Status, string? Mitigation, Ref? Owner, DateOnly? RaisedOn)
{
    public static RiskDto From(IntegrationRisk r) =>
        new(r.Id, r.Description, r.Likelihood, r.Impact, r.Score, r.Status, r.Mitigation, Ref.Of(r.Owner), r.RaisedOn);
}

/// <summary>One end of an integration. Kind is "Application" or "Partner".</summary>
public record EndpointDto(NodeKind Kind, int Id, string Name)
{
    public static EndpointDto Of(NodeRef node, string name) => new(node.Kind, node.Id, name);
}

public record PartnerDto(int Id, string Name, PartnerType Type, PartnerStatus Status, PartnerImportance Importance, string? AccountCode,
    string? EdiQualifier, string? EdiId, string? GsId, Ref? RelationshipOwner)
{
    public static PartnerDto From(TradingPartner p) => new(p.Id, p.Name, p.Type, p.Status, p.Importance, p.AccountCode,
        p.EdiQualifier, p.EdiId, p.GsId, Ref.Of(p.RelationshipOwner));
}

public record PartnerDetailDto(PartnerDto Partner, string? Notes, IReadOnlyList<ContactDto> People, IReadOnlyList<IntegrationDto> Integrations);

public record IntegrationDto(int Id, string Name, EndpointDto Source, EndpointDto Target, Ref? Via, IntegrationType Type, FlowDirection Direction,
    IntegrationFrequency Frequency, string? MessageFormat, string BusinessPurpose, Criticality Criticality, LifecycleStatus Status,
    Ref? Owner, DateOnly? LastReviewDate, DateOnly? NextReviewDate, bool ReviewOverdue, RiskLevel RiskLevel, int TopRiskScore, int OpenRiskCount)
{
    public static IntegrationDto From(Integration i, DateOnly today) => new(i.Id, i.Name,
        EndpointDto.Of(i.SourceNode, i.SourceName), EndpointDto.Of(i.TargetNode, i.TargetName),
        i.Via is { } v ? new Ref(v.Id, v.Name) : null, i.Type, i.Direction, i.Frequency, i.MessageFormat, i.BusinessPurpose,
        i.Criticality, i.Status, Ref.Of(i.Owner), i.LastReviewDate, i.NextReviewDate, PortfolioRules.ReviewOverdue(i, today),
        PortfolioRules.RiskLevel(i, today), PortfolioRules.TopRiskScore(i), i.Risks.Count(r => r.IsActive));
}

public record IntegrationDetailDto(IntegrationDto Integration, int ReviewIntervalMonths, string? Notes, IReadOnlyList<RiskDto> Risks)
{
    public static IntegrationDetailDto From(Integration i, DateOnly today) => new(IntegrationDto.From(i, today), i.ReviewIntervalMonths, i.Notes,
        i.Risks.OrderByDescending(r => r.IsActive).ThenByDescending(r => r.Score).Select(RiskDto.From).ToList());
}

/// <summary>An application (with Category and Criticality) or a trading partner (with PartnerType and Importance).</summary>
public record DependencyRowDto(EndpointDto Node, AppCategory? Category, Criticality? Criticality, PartnerType? PartnerType,
    PartnerImportance? Importance, int Distance, IReadOnlyList<Ref> Integrations);

public record DependencyDto(EndpointDto Node, int Depth, IReadOnlyList<IntegrationDto> Direct,
    IReadOnlyList<DependencyRowDto> Upstream, IReadOnlyList<DependencyRowDto> Downstream)
{
    public static DependencyDto From(DependencyView v, DateOnly today)
    {
        static DependencyRowDto Row(DependencyRow r) => new(EndpointDto.Of(r.Node, r.Name), r.Application?.Category,
            r.Application?.Criticality, r.Partner?.Type, r.Partner?.Importance, r.Distance,
            r.Integrations.Select(i => new Ref(i.Id, i.Name)).ToList());
        return new(EndpointDto.Of(v.Node, v.Name), v.Depth, v.Direct.Select(i => IntegrationDto.From(i, today)).ToList(),
            v.Upstream.Select(Row).ToList(), v.Downstream.Select(Row).ToList());
    }
}

public record ProcessDto(int Id, string Name, string? Description, ProcessDomain Domain, Ref? Owner, Ref? Parent,
    IReadOnlyList<ProcessApplicationDto> Applications, IReadOnlyList<Ref> Integrations, int OpenPainPoints, int Kpis, int KpisOnTarget)
{
    public static ProcessDto From(BusinessProcess p) => new(p.Id, p.Name, p.Description, p.Domain, Ref.Of(p.ProcessOwner),
        p.ParentProcess is { } parent ? new Ref(parent.Id, parent.Name) : null,
        p.Applications.OrderByDescending(a => a.IsPrimary).ThenBy(a => a.Application?.Name)
            .Select(a => new ProcessApplicationDto(new Ref(a.ApplicationId, a.Application?.Name ?? ""), a.Usage, a.IsPrimary)).ToList(),
        p.Integrations.Select(i => new Ref(i.IntegrationId, i.Integration?.Name ?? "")).OrderBy(r => r.Name).ToList(),
        p.PainPoints.Count(pp => pp.Status == PainPointStatus.Open), p.Kpis.Count, p.Kpis.Count(k => k.OnTarget == true));
}

public record ProcessApplicationDto(Ref Application, string? Usage, bool IsPrimary);

public record PainPointDto(int Id, string Description, PainSeverity Severity, PainPointStatus Status, Ref? Application, Ref? AddressedBy)
{
    public static PainPointDto From(ProcessPainPoint p) => new(p.Id, p.Description, p.Severity, p.Status,
        p.Application is { } a ? new Ref(a.Id, a.Name) : null,
        p.AddressedBy is { } i ? new Ref(i.Id, i.Title) : null);
}

public record KpiDto(int Id, string Name, string? Unit, KpiDirection Direction, decimal? Baseline, decimal? Target, decimal? Current,
    DateOnly? MeasuredOn, int? ProgressPct, bool? OnTarget)
{
    public static KpiDto From(ProcessKpi k) => new(k.Id, k.Name, k.Unit, k.Direction, k.Baseline, k.Target, k.Current, k.MeasuredOn, k.ProgressPct, k.OnTarget);
}

public record ProcessDetailDto(ProcessDto Process, IReadOnlyList<Ref> SubProcesses, IReadOnlyList<IntegrationDto> Integrations,
    IReadOnlyList<PainPointDto> PainPoints, IReadOnlyList<KpiDto> Kpis)
{
    public static ProcessDetailDto From(BusinessProcess p, DateOnly today) => new(ProcessDto.From(p),
        p.Children.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new Ref(c.Id, c.Name)).ToList(),
        p.Integrations.Select(i => IntegrationDto.From(i.Integration!, today)).OrderBy(i => i.Name).ToList(),
        p.PainPoints.OrderBy(pp => pp.Status).ThenByDescending(pp => pp.Severity).Select(PainPointDto.From).ToList(),
        p.Kpis.OrderBy(k => k.Name).Select(KpiDto.From).ToList());
}

public record ImpactIntegrationDto(Ref Integration, bool Direct, int Distance, EndpointDto Source, EndpointDto Target, Ref? Via,
    IntegrationType Type, Criticality Criticality, RiskLevel RiskLevel);

public record ImpactNodeDto(EndpointDto Node, int Distance, Criticality? Criticality, PartnerImportance? Importance);

public record ImpactProcessDto(Ref Process, bool TopLevel, ProcessImpactKind Kind, bool Direct, IReadOnlyList<string> Reasons, int OpenPainPoints);

public record ImpactInitiativeDto(int ItemId, Ref Product, ItemType ItemType, string Title, ItemStatus Status, int ProgressPct,
    DateOnly? StartDate, DateOnly? EndDate, string? Owner, bool InFlight, bool OnSubject, IReadOnlyList<string> Applications);

public record ImpactVendorDto(Ref Vendor, bool SuppliesSubject, IReadOnlyList<Ref> Applications, string? SupportPhone,
    IReadOnlyList<EscalationDto> Escalation, Ref? NextRenewalContract, DateOnly? NoticeDeadline);

public record ImpactDto(EndpointDto Subject, int Depth, DateOnly AsOf, ImpactSeverity Severity, IReadOnlyList<string> SeverityReasons, string Summary,
    IReadOnlyList<ImpactIntegrationDto> Integrations, IReadOnlyList<ImpactNodeDto> Applications, IReadOnlyList<ImpactNodeDto> Partners,
    IReadOnlyList<ImpactProcessDto> Processes, IReadOnlyList<ImpactInitiativeDto> Initiatives, IReadOnlyList<ImpactVendorDto> Vendors,
    IReadOnlyList<RiskDto> OpenRisks, IReadOnlyList<PainPointDto> OpenPainPoints, IReadOnlyList<Ref> Decisions)
{
    public static ImpactDto From(ImpactReport r) => new(EndpointDto.Of(r.Subject, r.SubjectName), r.Depth, r.AsOf, r.Severity, r.SeverityReasons, r.Summary,
        r.Integrations.Select(ii => new ImpactIntegrationDto(new Ref(ii.Integration.Id, ii.Integration.Name), ii.Direct, ii.Distance,
            EndpointDto.Of(ii.Integration.SourceNode, ii.Integration.SourceName), EndpointDto.Of(ii.Integration.TargetNode, ii.Integration.TargetName),
            ii.Integration.Via is { } v ? new Ref(v.Id, v.Name) : null, ii.Integration.Type, ii.Integration.Criticality, ii.Risk)).ToList(),
        r.Applications.Select(Node).ToList(),
        r.Partners.Select(Node).ToList(),
        r.Processes.Select(p => new ImpactProcessDto(new Ref(p.Process.Id, p.Process.Name), p.IsTopLevel, p.Kind, p.Direct, p.Reasons, p.OpenPainPoints)).ToList(),
        r.Initiatives.Select(i => new ImpactInitiativeDto(i.Item.ItemId, new Ref(i.Item.ProductId, i.Item.ProductName), i.Item.ItemType, i.Item.Title,
            i.Item.Status, i.Item.ProgressPct, i.Item.StartDate, i.Item.EndDate, i.Item.Owner, i.Item.InFlight, i.OnSubject, i.Applications)).ToList(),
        r.Vendors.Select(v => new ImpactVendorDto(new Ref(v.Vendor.Id, v.Vendor.Name), v.SuppliesSubject,
            v.Applications.Select(a => new Ref(a.Id, a.Name)).ToList(), v.Vendor.SupportPhone,
            v.Escalation.Where(e => e.Contact is not null)
                .Select(e => new EscalationDto(e.Level, new Ref(e.ContactId, e.Contact!.Name), e.Contact.Title, e.Contact.Email, e.Contact.Phone, e.Note)).ToList(),
            v.NextRenewal is { } c ? new Ref(c.Id, c.Name) : null, v.NextRenewal?.NoticeDeadline)).ToList(),
        r.OpenRisks.Select(x => RiskDto.From(x.Risk)).ToList(),
        r.OpenPainPoints.Select(x => PainPointDto.From(x.PainPoint)).ToList(),
        r.Decisions.Select(d => new Ref(d.Id, d.Title)).ToList());

    private static ImpactNodeDto Node(ImpactedNode n) => new(EndpointDto.Of(n.Node, n.Name), n.Distance, n.Application?.Criticality, n.Partner?.Importance);
}

public record MeasurementDto(DateOnly MeasuredOn, decimal Value, string? Source, string? Note);

public record BenefitDto(int Id, string Description, BenefitCategory Category, BenefitUnit Unit, Ref RoadmapItem, Ref Product, Ref? Application,
    Ref? Kpi, decimal? Baseline, decimal? ExpectedValue, DateOnly? ExpectedBy, decimal? ActualValue, DateOnly? ActualOn, int? RealizationPct,
    BenefitStatus Status, decimal? ExpectedUsd, decimal? RealizedUsd, string? Notes, IReadOnlyList<MeasurementDto> Measurements)
{
    public static BenefitDto From(Benefit b, decimal? laborRate, DateOnly today) => new(b.Id, b.Description, b.Category, b.Unit,
        new Ref(b.RoadmapItemId, b.RoadmapItem?.Title ?? ""), new Ref(b.RoadmapItem?.ProductId ?? 0, b.RoadmapItem?.Product?.Name ?? ""),
        b.Application is { } a ? new Ref(a.Id, a.Name) : null, b.Kpi is { } k ? new Ref(k.Id, k.Name) : null,
        BenefitMath.Baseline(b), b.ExpectedValue, b.ExpectedBy, BenefitMath.Actual(b), BenefitMath.ActualOn(b), BenefitMath.RealizationPct(b),
        BenefitMath.Status(b, today), BenefitMath.ExpectedUsd(b, laborRate), BenefitMath.RealizedUsd(b, laborRate), b.Notes,
        b.Measurements.OrderBy(m => m.MeasuredOn).Select(m => new MeasurementDto(m.MeasuredOn, m.Value, m.Source, m.Note)).ToList());
}

public record BenefitsDto(decimal? LaborRate, BenefitTotals Totals, IReadOnlyList<BenefitDto> Benefits);

public record DecisionLinkDto(DecisionTargetKind Kind, int Id, string Name);

public record AlternativeDto(string Option, string? Pros, string? Cons, string? WhyRejected);

public record DecisionDto(int Id, string Title, DateOnly DecidedOn, DecisionStatus Status, string? DecidedBy, string? Context, string Decision,
    string? Outcome, Ref? SupersededBy, IReadOnlyList<AlternativeDto> Alternatives, IReadOnlyList<DecisionLinkDto> LinkedTo)
{
    public static DecisionDto From(Decision d) => new(d.Id, d.Title, d.DecidedOn, d.Status, d.DecidedBy, d.Context, d.DecisionText, d.Outcome,
        d.SupersededBy is { } s ? new Ref(s.Id, s.Title) : null,
        d.Alternatives.OrderBy(a => a.SortOrder).Select(a => new AlternativeDto(a.Option, a.Pros, a.Cons, a.WhyRejected)).ToList(),
        d.Links.Select(l => new DecisionLinkDto(l.Target.Kind, l.Target.Id, l.TargetName)).ToList());
}

public record HealthDto(Ref Application, Criticality Criticality, int Score, HealthRating Rating, IReadOnlyList<HealthFactor> Factors);

public record StrategicItemDto(int ItemId, Ref Product, ItemType ItemType, string Title, ItemStatus Status, int ProgressPct, DateOnly? EndDate,
    bool Overdue, IReadOnlyList<string> Applications, int Benefits);

public record DashboardDto(DateOnly AsOf, IReadOnlyDictionary<HealthRating, int> Health, IReadOnlyList<HealthDto> Applications,
    decimal AnnualRunCost, IReadOnlyDictionary<TimeQuadrant, int> Time, IReadOnlyList<RenewalDto> Renewals, int LiveIntegrations,
    IReadOnlyList<Ref> HighRiskIntegrations, IReadOnlyList<Ref> MediumRiskIntegrations, BenefitTotals Benefits,
    IReadOnlyList<CategoryValue> ValueByCategory, IReadOnlyList<StrategicItemDto> StrategicItems, int OpenPainPoints, IReadOnlyList<Ref> RecentDecisions)
{
    public static DashboardDto From(PortfolioDashboard d) => new(d.AsOf,
        Enum.GetValues<HealthRating>().ToDictionary(r => r, d.Count),
        d.Applications.Where(a => a.Health is not null)
            .Select(a => new HealthDto(new Ref(a.Application.Id, a.Application.Name), a.Application.Criticality, a.Health!.Score, a.Health.Rating, a.Health.Factors)).ToList(),
        d.AnnualRunCost, d.Time,
        d.Renewals.Select(r => new RenewalDto(ContractDto.From(r.Contract), r.VendorName, r.Applications)).ToList(),
        d.LiveIntegrations,
        d.RiskyIntegrations.Where(r => r.Risk == RiskLevel.High).Select(r => new Ref(r.Integration.Id, r.Integration.Name)).ToList(),
        d.RiskyIntegrations.Where(r => r.Risk == RiskLevel.Medium).Select(r => new Ref(r.Integration.Id, r.Integration.Name)).ToList(),
        d.Benefits, d.ValueByCategory,
        d.Initiatives.Select(i => new StrategicItemDto(i.ItemId, new Ref(i.ProductId, i.ProductName), i.ItemType, i.Title, i.Status, i.ProgressPct,
            i.EndDate, i.Overdue, i.Applications, i.Benefits)).ToList(),
        d.OpenPainPoints,
        d.RecentDecisions.Select(x => new Ref(x.Id, x.Title)).ToList());
}

public record DocumentDto(int Id, string Title, DocumentCategory Category, string? Url, string? FileName, string? LocalPath, long? SizeBytes,
    DateOnly? DocumentDate, string? Notes, Ref? Product, DocumentTargetKind? AttachedTo, int? AttachedId, string? AttachedName, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static DocumentDto From(PortfolioDocument d) => new(d.Id, d.Title, d.Category, d.Url, d.FileName, d.LocalPath, d.SizeBytes,
        d.DocumentDate, d.Notes, Ref.Of(d.Product), d.Target?.Kind, d.Target?.Id, d.TargetName, d.CreatedAt, d.UpdatedAt);
}

public record CapabilityDto(AppCategory Category, IReadOnlyList<CapabilityDto.FeatureRow> Features, IReadOnlyList<CapabilityDto.AppColumn> Applications)
{
    public record AppColumn(int Id, string Name, AppCategory Category, LifecycleStatus Status, bool Added, int Yes, int Partial, int No, int NotRated);
    public record Rating(int ApplicationId, FeatureAvailability? Availability, FeatureStrength? Strength, string? Note);
    public record FeatureRow(int Id, string Name, string? Description, int Yes, int Partial, bool Gap, bool Overlap, IReadOnlyList<Rating> Ratings);

    public static CapabilityDto From(CapabilityMatrix m) => new(m.Category,
        m.Features.Select(f =>
        {
            var c = m.ByFeature[f.Id];
            return new FeatureRow(f.Id, f.Name, f.Description, c.Yes, c.Partial, c.IsGap, c.IsOverlap,
                m.Apps.Select(a => m.Cell(f.Id, a.Id)).Where(x => x is not null)
                    .Select(x => new Rating(x!.ApplicationId, x.Availability, x.Strength, x.Note)).ToList());
        }).ToList(),
        m.Apps.Select(a =>
        {
            var c = m.ByApp[a.Id];
            return new AppColumn(a.Id, a.Name, a.Application.Category, a.Application.Status, a.Added, c.Yes, c.Partial, c.No, c.Unassessed);
        }).ToList());
}
