using System.Globalization;
using System.Text;

namespace Roadmapper.Core.Portfolio;

/// <summary>A roadmap item linked to an application, as the impact analysis sees it. Read live from the roadmap.</summary>
public sealed record ImpactInitiative(int ApplicationId, int ItemId, int ProductId, string ProductName, ItemType ItemType, string Title,
    ItemStatus Status, int ProgressPct, DateOnly? StartDate, DateOnly? EndDate, string? Owner, InitiativeImpact Impact)
{
    /// <summary>Planned or in progress: work that a change could collide with.</summary>
    public bool InFlight => Status is ItemStatus.Planned or ItemStatus.InProgress;
}

/// <summary>
/// Everything the analysis reads. Applications need <see cref="Application.Vendor"/> loaded, with its contracts and
/// escalation contacts; integrations need their risks; processes need their applications, integrations and pain points.
/// </summary>
public sealed class ImpactInput
{
    public required IReadOnlyList<Application> Applications { get; init; }
    public IReadOnlyList<TradingPartner> Partners { get; init; } = [];
    /// <summary>All integrations; retired ones are ignored because they no longer move data.</summary>
    public required IReadOnlyList<Integration> Integrations { get; init; }
    public IReadOnlyList<BusinessProcess> Processes { get; init; } = [];
    public IReadOnlyList<ImpactInitiative> Initiatives { get; init; } = [];
    /// <summary>Decisions with their links.</summary>
    public IReadOnlyList<Decision> Decisions { get; init; } = [];
}

/// <summary>An affected integration. Direct: the subject is its source, target or middle hop. Indirect: it carries the subject's data further on.</summary>
public sealed record ImpactedIntegration(Integration Integration, bool Direct, int Distance, RiskLevel Risk);

/// <summary>An application or partner at the other end of an affected integration, <see cref="Distance"/> integrations from the subject.</summary>
public sealed record ImpactedNode(NodeRef Node, string Name, Application? Application, TradingPartner? Partner, int Distance)
{
    public bool IsKeyPartner => Partner?.Importance == PartnerImportance.Key;
}

/// <summary>An affected business process and why. Direct: it uses the subject or relies on a directly affected integration.</summary>
public sealed record ImpactedProcess(BusinessProcess Process, ProcessImpactKind Kind, bool Direct, IReadOnlyList<string> Reasons, int OpenPainPoints)
{
    public bool IsTopLevel => Process.ParentProcessId is null;
}

/// <summary>A roadmap item linked to the subject or to an application connected to it.</summary>
public sealed record ImpactedInitiative(ImpactInitiative Item, IReadOnlyList<string> Applications, bool OnSubject);

/// <summary>A vendor to coordinate with: it supplies the subject or an application connected to it.</summary>
public sealed record ImpactedVendor(Vendor Vendor, IReadOnlyList<Application> Applications, bool SuppliesSubject,
    Contract? NextRenewal, IReadOnlyList<VendorEscalationContact> Escalation);

public sealed record ImpactedRisk(IntegrationRisk Risk, Integration Integration);

public sealed record ImpactedPainPoint(ProcessPainPoint PainPoint, BusinessProcess Process);

/// <summary>What a change to an application or trading partner touches. See <see cref="ImpactAnalyzer"/>.</summary>
public sealed record ImpactReport(
    NodeRef Subject,
    string SubjectName,
    int Depth,
    DateOnly AsOf,
    ImpactSeverity Severity,
    IReadOnlyList<string> SeverityReasons,
    IReadOnlyList<ImpactedIntegration> Integrations,
    IReadOnlyList<ImpactedNode> Applications,
    IReadOnlyList<ImpactedNode> Partners,
    IReadOnlyList<ImpactedProcess> Processes,
    IReadOnlyList<ImpactedInitiative> Initiatives,
    IReadOnlyList<ImpactedVendor> Vendors,
    IReadOnlyList<ImpactedRisk> OpenRisks,
    IReadOnlyList<ImpactedPainPoint> OpenPainPoints,
    IReadOnlyList<Decision> Decisions)
{
    public int Tier1Integrations => Integrations.Count(i => i.Integration.Criticality == Criticality.Tier1);
    public int KeyPartners => Partners.Count(p => p.IsKeyPartner);
    public int InFlightInitiatives => Initiatives.Count(i => i.Item.InFlight);

    /// <summary>"5 integrations (3 Tier 1) · 2 trading partners (1 key) · 4 processes · 2 in-flight roadmap items · 3 vendors".</summary>
    public string Summary
    {
        get
        {
            static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
            var parts = new List<string>
            {
                N(Integrations.Count, "integration", "integrations") + (Tier1Integrations > 0 ? $" ({Tier1Integrations} Tier 1)" : ""),
            };
            if (Partners.Count > 0) parts.Add(N(Partners.Count, "trading partner", "trading partners") + (KeyPartners > 0 ? $" ({KeyPartners} key)" : ""));
            parts.Add(N(Processes.Count, "process", "processes"));
            parts.Add(InFlightInitiatives > 0
                ? N(InFlightInitiatives, "in-flight roadmap item", "in-flight roadmap items")
                : N(Initiatives.Count, "roadmap item", "roadmap items"));
            parts.Add(N(Vendors.Count, "vendor", "vendors"));
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Change impact analysis: if an application changes (upgrade, outage, replacement) or a trading partner changes
/// (new mapping, onboarding, offboarding), what else is touched?
/// <list type="number">
/// <item><b>Integrations</b>: every live integration in which the subject is the source, target or middle hop
/// (direct), plus the integrations that carry its data further downstream, up to <c>depth</c> integrations away
/// (indirect). The dependency walk's rules apply: partners are end points and a partner's data stays on its own flows.</item>
/// <item><b>Applications and trading partners</b> at the other ends of those integrations. Key partners are flagged.</item>
/// <item><b>Business processes</b> that use the subject application, or rely on an affected integration, plus the
/// processes above them (rolled up).</item>
/// <item><b>Roadmap items</b> linked to the subject or to an application it exchanges data with directly; planned and
/// in-progress ones are in flight.</item>
/// <item><b>Vendors</b> of the subject and of the applications it exchanges data with directly: they need to coordinate
/// and test. Their escalation path and next renewal come along.</item>
/// <item>Open risks on affected integrations and open pain points on affected processes.</item>
/// </list>
/// Severity is High when the subject is a Tier 1 application or key partner, a directly affected integration is
/// Tier 1 or high risk, or a key partner's flows are affected; Medium when anything else that matters is touched (a
/// Tier 1–2 integration, a process, in-flight work); Low otherwise.
/// </summary>
public static class ImpactAnalyzer
{
    public const int DefaultDepth = 2;

    public static ImpactReport Analyze(ImpactInput input, NodeRef subject, int depth, DateOnly today)
    {
        depth = Math.Max(1, depth);
        var apps = input.Applications.ToDictionary(a => a.Id);
        var partners = input.Partners.ToDictionary(p => p.Id);
        var live = input.Integrations.Where(i => i.Status != LifecycleStatus.Retired).ToList();
        var byId = live.ToDictionary(i => i.Id);
        var graph = PortfolioGraph.From(live);
        string NameOf(NodeRef n) => n.IsPartner ? partners.GetValueOrDefault(n.Id)?.Name ?? $"Partner #{n.Id}" : apps.GetValueOrDefault(n.Id)?.Name ?? $"Application #{n.Id}";
        var subjectApp = subject.IsPartner ? null : apps.GetValueOrDefault(subject.Id);
        var subjectPartner = subject.IsPartner ? partners.GetValueOrDefault(subject.Id) : null;

        // 1. Integrations: direct ones, then the ones carrying the subject's data downstream.
        var distance = new Dictionary<int, int>();
        foreach (var id in graph.IntegrationsTouching(subject)) distance[id] = 1;
        foreach (var reach in graph.Downstream(subject, depth))
            foreach (var id in reach.IntegrationIds)
                if (!distance.ContainsKey(id)) distance[id] = reach.Distance;
        var integrations = distance
            .Select(kv => new ImpactedIntegration(byId[kv.Key], graph.IntegrationsTouching(subject).Contains(kv.Key), kv.Value,
                PortfolioRules.RiskLevel(byId[kv.Key], today)))
            .OrderByDescending(i => i.Direct).ThenBy(i => i.Integration.Criticality).ThenByDescending(i => i.Risk)
            .ThenBy(i => i.Integration.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var directIds = integrations.Where(i => i.Direct).Select(i => i.Integration.Id).ToHashSet();

        // 2. The applications and partners at the other ends, at the distance of the nearest affected integration.
        var nodeDistance = new Dictionary<NodeRef, int>();
        foreach (var ii in integrations)
        {
            var i = ii.Integration;
            var ends = new List<NodeRef> { i.SourceNode, i.TargetNode };
            if (i.ViaNode is { } via) ends.Add(via);
            foreach (var n in ends.Where(n => n != subject))
                nodeDistance[n] = Math.Min(nodeDistance.GetValueOrDefault(n, int.MaxValue), ii.Distance);
        }
        ImpactedNode Node(KeyValuePair<NodeRef, int> kv) => new(kv.Key, NameOf(kv.Key),
            kv.Key.IsPartner ? null : apps.GetValueOrDefault(kv.Key.Id), kv.Key.IsPartner ? partners.GetValueOrDefault(kv.Key.Id) : null, kv.Value);
        var affectedApps = nodeDistance.Where(kv => !kv.Key.IsPartner).Select(Node)
            .OrderBy(n => n.Distance).ThenBy(n => n.Application?.Criticality).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var affectedPartners = nodeDistance.Where(kv => kv.Key.IsPartner).Select(Node)
            .OrderBy(n => n.Partner?.Importance).ThenBy(n => n.Distance).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
        // Applications that exchange data with the subject directly: their roadmaps and vendors are in play.
        var neighbors = affectedApps.Where(n => n.Distance == 1).Select(n => n.Node.Id).ToHashSet();

        // 3. Business processes.
        var processes = AffectedProcesses(input.Processes, subjectApp, byId, integrations, directIds);

        // 4. Roadmap items on the subject and its direct neighbors.
        var initiatives = input.Initiatives
            .Where(l => subjectApp is not null && l.ApplicationId == subjectApp.Id || neighbors.Contains(l.ApplicationId))
            .GroupBy(l => l.ItemId)
            .Select(g =>
            {
                var onSubject = subjectApp is not null && g.Any(l => l.ApplicationId == subjectApp.Id);
                var first = g.FirstOrDefault(l => subjectApp is not null && l.ApplicationId == subjectApp.Id) ?? g.First();
                var names = g.Select(l => NameOf(NodeRef.App(l.ApplicationId))).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
                return new ImpactedInitiative(first, names, onSubject);
            })
            .OrderByDescending(i => i.Item.InFlight).ThenByDescending(i => i.OnSubject)
            .ThenBy(i => i.Item.ProductName, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Item.ItemType).ThenBy(i => i.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 5. Vendors to coordinate with.
        var vendorApps = new List<Application>();
        if (subjectApp is not null) vendorApps.Add(subjectApp);
        vendorApps.AddRange(neighbors.Select(id => apps.GetValueOrDefault(id)).OfType<Application>());
        var vendors = vendorApps.Where(a => a.Vendor is not null)
            .GroupBy(a => a.Vendor!.Id)
            .Select(g =>
            {
                var vendor = g.First().Vendor!;
                return new ImpactedVendor(vendor, g.DistinctBy(a => a.Id).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                    subjectApp is not null && g.Any(a => a.Id == subjectApp.Id),
                    PortfolioRules.NextRenewal(vendor.Contracts, today),
                    vendor.EscalationContacts.OrderBy(e => e.Level).ToList());
            })
            .OrderByDescending(v => v.SuppliesSubject).ThenBy(v => v.Vendor.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 6. Open risks and pain points.
        var risks = integrations.SelectMany(ii => ii.Integration.Risks.Where(r => r.IsActive).Select(r => new ImpactedRisk(r, ii.Integration)))
            .OrderByDescending(r => r.Risk.Score).ThenBy(r => r.Integration.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var painProcesses = processes.Where(p => p.Kind != ProcessImpactKind.SubProcessAffected).Select(p => p.Process).ToList();
        var pains = painProcesses.SelectMany(p => p.PainPoints.Select(pp => (pp, p)))
            .Concat(subjectApp is null ? [] : input.Processes.SelectMany(p => p.PainPoints.Where(pp => pp.ApplicationId == subjectApp.Id).Select(pp => (pp, p))))
            .Where(x => x.pp.Status == PainPointStatus.Open)
            .DistinctBy(x => x.pp.Id)
            .Select(x => new ImpactedPainPoint(x.pp, x.p))
            .OrderByDescending(x => x.PainPoint.Severity).ThenBy(x => x.Process.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // 7. Decisions about the subject, its direct integrations, its vendor or contract, or an affected process.
        bool Relevant(DecisionLink l) =>
            (subjectApp is not null && (l.ApplicationId == subjectApp.Id || (l.VendorId is { } v && v == subjectApp.VendorId)
                || (l.ContractId is { } c && c == subjectApp.ContractId)))
            || (subjectPartner is not null && l.TradingPartnerId == subjectPartner.Id)
            || (l.IntegrationId is { } i && directIds.Contains(i))
            || (l.ProcessId is { } p && painProcesses.Any(pp => pp.Id == p));
        var decisions = input.Decisions.Where(d => d.Links.Any(Relevant))
            .OrderBy(d => d.Status is DecisionStatus.Superseded or DecisionStatus.Reversed).ThenByDescending(d => d.DecidedOn).ThenBy(d => d.Title)
            .ToList();

        var (severity, reasons) = Rate(subjectApp, subjectPartner, integrations, affectedPartners, processes, initiatives);
        return new ImpactReport(subject, NameOf(subject), depth, today, severity, reasons, integrations, affectedApps, affectedPartners,
            processes, initiatives, vendors, risks, pains, decisions);
    }

    private static List<ImpactedProcess> AffectedProcesses(IReadOnlyList<BusinessProcess> all, Application? subjectApp,
        Dictionary<int, Integration> byId, List<ImpactedIntegration> integrations, HashSet<int> directIds)
    {
        var affected = integrations.Select(i => i.Integration.Id).ToHashSet();
        var found = new Dictionary<int, (BusinessProcess P, ProcessImpactKind Kind, bool Direct, List<string> Reasons)>();
        foreach (var p in all)
        {
            var reasons = new List<string>();
            var kind = (ProcessImpactKind?)null;
            var direct = false;
            if (subjectApp is not null && p.Applications.FirstOrDefault(pa => pa.ApplicationId == subjectApp.Id) is { } use)
            {
                var detail = string.Join(", ", new[] { use.IsPrimary ? "primary system" : null, use.Usage }.OfType<string>());
                reasons.Add($"Uses {subjectApp.Name}{(detail.Length > 0 ? $" ({detail})" : "")}");
                kind = ProcessImpactKind.UsesApplication;
                direct = true;
            }
            foreach (var pi in p.Integrations.Where(pi => affected.Contains(pi.IntegrationId)).OrderBy(pi => byId[pi.IntegrationId].Name, StringComparer.OrdinalIgnoreCase))
            {
                var isDirect = directIds.Contains(pi.IntegrationId);
                reasons.Add($"Relies on {byId[pi.IntegrationId].Name}{(isDirect ? "" : " (indirectly affected)")}");
                kind ??= ProcessImpactKind.DependsOnIntegration;
                direct |= isDirect;
            }
            if (kind is { } k) found[p.Id] = (p, k, direct, reasons);
        }

        // Roll up: a process above an affected one is affected through it.
        var parentOf = all.ToDictionary(p => p.Id, p => p.ParentProcessId);
        var byProcessId = all.ToDictionary(p => p.Id);
        foreach (var (id, hit) in found.ToList())
            foreach (var ancestor in PortfolioRules.SelfAndAncestors(id, parentOf).Skip(1))
            {
                if (!byProcessId.TryGetValue(ancestor, out var parent)) break;
                if (found.TryGetValue(ancestor, out var existing))
                {
                    if (existing.Kind == ProcessImpactKind.SubProcessAffected && !existing.Reasons.Contains($"Sub-process {hit.P.Name} is affected"))
                        existing.Reasons.Add($"Sub-process {hit.P.Name} is affected");
                    continue;
                }
                found[ancestor] = (parent, ProcessImpactKind.SubProcessAffected, false, [$"Sub-process {hit.P.Name} is affected"]);
            }

        return found.Values
            .Select(f => new ImpactedProcess(f.P, f.Kind, f.Direct, f.Reasons, f.P.PainPoints.Count(pp => pp.Status == PainPointStatus.Open)))
            .OrderByDescending(p => p.Direct).ThenBy(p => p.Kind).ThenBy(p => p.Process.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (ImpactSeverity, List<string>) Rate(Application? subjectApp, TradingPartner? subjectPartner,
        List<ImpactedIntegration> integrations, List<ImpactedNode> partners, List<ImpactedProcess> processes, List<ImpactedInitiative> initiatives)
    {
        var high = new List<string>();
        if (subjectApp?.Criticality == Criticality.Tier1) high.Add($"{subjectApp.Name} is a Tier 1 application");
        if (subjectPartner?.Importance == PartnerImportance.Key) high.Add($"{subjectPartner.Name} is a key partner");
        if (integrations.Count(i => i.Direct && i.Integration.Criticality == Criticality.Tier1) is var t1 and > 0)
            high.Add($"{t1} Tier 1 integration{(t1 == 1 ? " is" : "s are")} directly affected");
        if (integrations.Count(i => i.Direct && i.Risk == RiskLevel.High) is var hr and > 0)
            high.Add($"{hr} directly affected integration{(hr == 1 ? " is" : "s are")} already high risk");
        if (partners.Where(p => p.IsKeyPartner).Select(p => p.Name).ToList() is { Count: > 0 } keys)
            high.Add($"Key partner{(keys.Count == 1 ? "" : "s")} affected: {string.Join(", ", keys)}");
        if (high.Count > 0) return (ImpactSeverity.High, high);

        var medium = new List<string>();
        if (integrations.Count(i => i.Integration.Criticality <= Criticality.Tier2) is var t12 and > 0)
            medium.Add($"{t12} Tier 1–2 integration{(t12 == 1 ? "" : "s")} affected");
        if (processes.Count > 0) medium.Add($"{processes.Count} business process{(processes.Count == 1 ? "" : "es")} affected");
        if (initiatives.Count(i => i.Item.InFlight) is var f and > 0) medium.Add($"{f} roadmap item{(f == 1 ? " is" : "s are")} in flight");
        if (medium.Count > 0) return (ImpactSeverity.Medium, medium);

        return (ImpactSeverity.Low, integrations.Count == 0 ? ["No live integrations touch it"] : ["Only Tier 3 flows, no processes or in-flight work"]);
    }

    /// <summary>The report as plain text, to paste into an email or ticket.</summary>
    public static string ToText(ImpactReport r)
    {
        var us = CultureInfo.GetCultureInfo("en-US");
        string D(DateOnly? d) => d?.ToString("MMM d, yyyy", us) ?? "–";
        var sb = new StringBuilder();
        sb.AppendLine($"Change impact: {r.SubjectName}");
        sb.AppendLine($"As of {D(r.AsOf)} · {r.Depth} step{(r.Depth == 1 ? "" : "s")} downstream");
        sb.AppendLine();
        sb.AppendLine($"{PortfolioText.Label(r.Severity).ToUpperInvariant()}: {string.Join("; ", r.SeverityReasons)}");
        sb.AppendLine(r.Summary);

        void Section(string title, int count)
        {
            sb.AppendLine();
            sb.AppendLine($"{title} ({count})");
        }

        Section("Integrations", r.Integrations.Count);
        foreach (var i in r.Integrations)
        {
            var path = i.Integration.ViaApplicationId is null
                ? $"{i.Integration.SourceName} → {i.Integration.TargetName}"
                : $"{i.Integration.SourceName} → {i.Integration.Via?.Name} → {i.Integration.TargetName}";
            sb.AppendLine($"- {i.Integration.Name} [{PortfolioText.Label(i.Integration.Criticality)}, {PortfolioText.Label(i.Integration.Type)}, " +
                $"{PortfolioText.Label(i.Risk)}] {path}{(i.Direct ? "" : $" (indirect, {i.Distance} steps away)")}");
        }

        if (r.Partners.Count > 0)
        {
            Section("Trading partners", r.Partners.Count);
            foreach (var p in r.Partners)
                sb.AppendLine($"- {p.Name}{(p.IsKeyPartner ? " [Key partner]" : "")}{(p.Partner?.RelationshipOwner is { } o ? $", owner {o.Name}" : "")}");
        }

        Section("Applications", r.Applications.Count);
        foreach (var a in r.Applications)
            sb.AppendLine($"- {a.Name}{(a.Application is { } app ? $" [{PortfolioText.Label(app.Criticality)}]" : "")}{(a.Distance == 1 ? "" : $" ({a.Distance} steps away)")}");

        Section("Business processes", r.Processes.Count);
        foreach (var p in r.Processes)
            sb.AppendLine($"- {p.Process.Name}: {string.Join("; ", p.Reasons)}{(p.OpenPainPoints > 0 ? $" ({p.OpenPainPoints} open pain points)" : "")}");

        Section("Roadmap items", r.Initiatives.Count);
        foreach (var i in r.Initiatives)
            sb.AppendLine($"- {i.Item.ProductName}: {i.Item.Title} [{i.Item.ItemType}, {i.Item.Status}, {i.Item.ProgressPct}%] on {string.Join(", ", i.Applications)}" +
                (i.Item.InFlight ? " (in flight)" : ""));

        Section("Vendors to coordinate with", r.Vendors.Count);
        foreach (var v in r.Vendors)
        {
            var first = v.Escalation.FirstOrDefault()?.Contact ?? v.Vendor.AccountManager;
            sb.AppendLine($"- {v.Vendor.Name} ({string.Join(", ", v.Applications.Select(a => a.Name))})" +
                (first is null ? "" : $", contact {first.Name}{(first.Email is { } e ? $" <{e}>" : "")}") +
                (v.Vendor.SupportPhone is { } ph ? $", support {ph}" : "") +
                (v.NextRenewal?.NoticeDeadline is { } nd ? $", notice deadline {D(nd)}{(nd < r.AsOf ? " (passed)" : "")}" : ""));
        }

        if (r.Decisions.Count > 0)
        {
            Section("Past decisions", r.Decisions.Count);
            foreach (var d in r.Decisions) sb.AppendLine($"- {D(d.DecidedOn)} {d.Title} [{PortfolioText.Label(d.Status)}]: {d.DecisionText}");
        }
        if (r.OpenRisks.Count > 0)
        {
            Section("Open risks", r.OpenRisks.Count);
            foreach (var x in r.OpenRisks) sb.AppendLine($"- [{x.Risk.Score}] {x.Risk.Description} ({x.Integration.Name})");
        }
        if (r.OpenPainPoints.Count > 0)
        {
            Section("Open pain points", r.OpenPainPoints.Count);
            foreach (var x in r.OpenPainPoints) sb.AppendLine($"- [{x.PainPoint.Severity}] {x.PainPoint.Description} ({x.Process.Name})");
        }
        return sb.ToString();
    }
}
