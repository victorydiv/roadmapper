using Roadmapper.Core;
using Roadmapper.Core.Portfolio;

namespace Roadmapper.Tests;

/// <summary>Change impact analysis over a small, hand-built 3PL portfolio (no database).</summary>
public class ImpactAnalyzerTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    // Applications: 2 EDI, 3 WMS (Tier 1), 4 TMS, 5 ERP, 6 Portal. Partners: 1 Acme (key customer), 7 carrier, 8 Globex.
    // Integrations:
    //   10 Acme → WMS via EDI (Tier 2)       16 Globex → WMS via EDI (Tier 2)
    //   11 WMS → TMS (Tier 1)                12 TMS → carrier (Tier 2)
    //   13 WMS → ERP (Tier 2)                14 ERP → Portal (Tier 3)
    //   17 WMS → Portal (retired)
    // Processes: 100 Order to Cash › 101 Order intake (uses EDI, relies on 16), 102 Shipping (uses WMS, primary),
    //            103 Billing (relies on 14).
    private sealed class World
    {
        public readonly Dictionary<int, Application> Apps = new();
        public readonly Dictionary<int, TradingPartner> Partners = new();
        public readonly List<Integration> Integrations = [];
        public readonly List<BusinessProcess> Processes = [];
        public readonly List<ImpactInitiative> Initiatives = [];

        public ImpactInput Input => new()
        {
            Applications = Apps.Values.ToList(), Partners = Partners.Values.ToList(), Integrations = Integrations,
            Processes = Processes, Initiatives = Initiatives,
        };

        public ImpactReport Analyze(NodeRef subject, int depth) => ImpactAnalyzer.Analyze(Input, subject, depth, Today);

        public Vendor Vendor(int id, string name, int noticeInDays = 60, params string[] escalation)
        {
            var v = new Vendor { Id = id, Name = name, SupportPhone = "555-0100" };
            v.Contracts.Add(new Contract { Id = id * 10, VendorId = id, Name = $"{name} contract", RenewalDate = Today.AddDays(noticeInDays + 30), NoticePeriodDays = 30 });
            for (var n = 0; n < escalation.Length; n++)
                v.EscalationContacts.Add(new VendorEscalationContact { VendorId = id, ContactId = id * 100 + n, Level = escalation.Length - n,
                    Contact = new Contact { Id = id * 100 + n, Name = escalation[n] } });
            return v;
        }

        public Application App(int id, string name, Criticality tier = Criticality.Tier2, Vendor? vendor = null) =>
            Apps[id] = new Application { Id = id, Name = name, Criticality = tier, Vendor = vendor, VendorId = vendor?.Id };

        public TradingPartner Partner(int id, string name, PartnerImportance importance = PartnerImportance.Standard) =>
            Partners[id] = new TradingPartner { Id = id, Name = name, Importance = importance };

        public Integration Flow(int id, NodeRef source, NodeRef target, Criticality tier, int? via = null, LifecycleStatus status = LifecycleStatus.Active)
        {
            var i = new Integration
            {
                Id = id, Name = $"Flow {id}", Criticality = tier, Status = status, LastReviewDate = Today, ReviewIntervalMonths = 12,
                SourceApplicationId = source.IsPartner ? null : source.Id, SourcePartnerId = source.IsPartner ? source.Id : null,
                TargetApplicationId = target.IsPartner ? null : target.Id, TargetPartnerId = target.IsPartner ? target.Id : null,
                ViaApplicationId = via,
            };
            i.Source = source.IsPartner ? null : Apps[source.Id];
            i.SourcePartner = source.IsPartner ? Partners[source.Id] : null;
            i.Target = target.IsPartner ? null : Apps[target.Id];
            i.TargetPartner = target.IsPartner ? Partners[target.Id] : null;
            i.Via = via is { } v ? Apps[v] : null;
            Integrations.Add(i);
            return i;
        }

        public BusinessProcess Process(int id, string name, int? parent = null) =>
            Processes.AddAndReturn(new BusinessProcess { Id = id, Name = name, ParentProcessId = parent });
    }

    private static NodeRef A(int id) => NodeRef.App(id);
    private static NodeRef P(int id) => NodeRef.Partner(id);

    private static World Sample()
    {
        var w = new World();
        var contoso = w.Vendor(1, "Contoso EDI Services", 60, "Jamie Fox");
        var northwind = w.Vendor(2, "Northwind", 20, "VP Avery", "Taylor AM"); // Taylor is level 1
        var fabrikam = w.Vendor(3, "Fabrikam");
        w.App(2, "EDI", vendor: contoso);
        w.App(3, "WMS", Criticality.Tier1, northwind);
        w.App(4, "TMS", vendor: fabrikam);
        w.App(5, "ERP");
        w.App(6, "Portal", Criticality.Tier3);
        w.Partner(1, "Acme", PartnerImportance.Key);
        w.Partner(7, "Carrier");
        w.Partner(8, "Globex");

        var acme = w.Flow(10, P(1), A(3), Criticality.Tier2, via: 2);
        w.Flow(11, A(3), A(4), Criticality.Tier1);
        w.Flow(12, A(4), P(7), Criticality.Tier2);
        w.Flow(13, A(3), A(5), Criticality.Tier2);
        w.Flow(14, A(5), A(6), Criticality.Tier3);
        w.Flow(16, P(8), A(3), Criticality.Tier2, via: 2);
        w.Flow(17, A(3), A(6), Criticality.Tier1, status: LifecycleStatus.Retired);
        acme.Risks.Add(new IntegrationRisk { Id = 1, Description = "Map changes untested", Likelihood = 4, Impact = 5 });
        w.Integrations.Single(i => i.Id == 11).Risks.Add(new IntegrationRisk { Id = 2, Description = "Old", Likelihood = 5, Impact = 5, Status = RiskStatus.Closed });

        w.Process(100, "Order to Cash");
        var intake = w.Process(101, "Order intake", 100);
        intake.Applications.Add(new ProcessApplication { ProcessId = 101, ApplicationId = 2, Usage = "Translates 940s" });
        intake.Integrations.Add(new ProcessIntegration { ProcessId = 101, IntegrationId = 16 });
        var shipping = w.Process(102, "Shipping", 100);
        shipping.Applications.Add(new ProcessApplication { ProcessId = 102, ApplicationId = 3, Usage = "Waves", IsPrimary = true });
        shipping.PainPoints.Add(new ProcessPainPoint { Id = 1, ProcessId = 102, Description = "Manual waves", Severity = PainSeverity.High });
        shipping.PainPoints.Add(new ProcessPainPoint { Id = 2, ProcessId = 102, Description = "Fixed already", Status = PainPointStatus.Addressed });
        var billing = w.Process(103, "Billing", 100);
        billing.Integrations.Add(new ProcessIntegration { ProcessId = 103, IntegrationId = 14 });

        ImpactInitiative Item(int appId, int itemId, string title, ItemStatus status) =>
            new(appId, itemId, 1, "Roadmap", ItemType.Initiative, title, status, 0, null, null, null, InitiativeImpact.Enhances);
        w.Initiatives.Add(Item(3, 500, "WMS upgrade", ItemStatus.InProgress));
        w.Initiatives.Add(Item(5, 501, "ERP idea", ItemStatus.Idea));
        w.Initiatives.Add(Item(6, 502, "Portal redesign", ItemStatus.Planned));
        w.Initiatives.Add(Item(4, 500, "WMS upgrade", ItemStatus.InProgress)); // also linked to the TMS
        return w;
    }

    [Fact]
    public void Direct_and_indirect_integrations_follow_the_data_downstream_and_skip_retired_ones()
    {
        var r = Sample().Analyze(A(3), 2);
        var byId = r.Integrations.ToDictionary(i => i.Integration.Id);

        Assert.Equal([10, 11, 13, 16], byId.Values.Where(i => i.Direct).Select(i => i.Integration.Id).Order());
        Assert.False(byId[12].Direct); // WMS → TMS → carrier
        Assert.Equal(2, byId[12].Distance);
        Assert.False(byId[14].Direct); // WMS → ERP → Portal
        Assert.DoesNotContain(17, byId.Keys);
        Assert.Equal(11, r.Integrations[0].Integration.Id); // direct Tier 1 first
    }

    [Fact]
    public void Depth_limits_the_indirect_integrations()
    {
        var r = Sample().Analyze(A(3), 1);
        Assert.Equal([10, 11, 13, 16], r.Integrations.Select(i => i.Integration.Id).Order());
        Assert.All(r.Integrations, i => Assert.True(i.Direct));
    }

    [Fact]
    public void Applications_and_partners_at_the_other_ends_are_listed_with_key_partners_flagged()
    {
        var r = Sample().Analyze(A(3), 2);

        Assert.Equal(["EDI", "ERP", "TMS", "Portal"], r.Applications.Select(a => a.Name));
        Assert.Equal(2, r.Applications.Single(a => a.Name == "Portal").Distance);
        Assert.Equal(["Acme", "Globex", "Carrier"], r.Partners.Select(p => p.Name)); // key partners first
        Assert.True(r.Partners[0].IsKeyPartner);
        Assert.Equal(1, r.KeyPartners);
    }

    [Fact]
    public void A_partner_change_stays_on_that_partners_flows()
    {
        var r = Sample().Analyze(P(8), 2);
        var ids = r.Integrations.Select(i => i.Integration.Id).ToHashSet();

        Assert.Contains(16, ids);
        Assert.DoesNotContain(10, ids); // Acme's orders are not Globex's data
        Assert.Contains(11, ids);      // but the WMS passes Globex's orders on to the TMS
        Assert.DoesNotContain(r.Partners, p => p.Name == "Acme");
        Assert.Equal(["Order intake", "Order to Cash"], r.Processes.Select(p => p.Process.Name)); // relies on Globex's 940
    }

    [Fact]
    public void Processes_using_the_application_or_relying_on_affected_integrations_roll_up_to_their_parents()
    {
        var r = Sample().Analyze(A(3), 2);
        var byName = r.Processes.ToDictionary(p => p.Process.Name);

        Assert.Equal(ProcessImpactKind.UsesApplication, byName["Shipping"].Kind);
        Assert.Contains("Uses WMS (primary system, Waves)", byName["Shipping"].Reasons);
        Assert.True(byName["Shipping"].Direct);
        Assert.Equal(1, byName["Shipping"].OpenPainPoints);

        Assert.Equal(ProcessImpactKind.DependsOnIntegration, byName["Order intake"].Kind);
        Assert.True(byName["Order intake"].Direct); // relies on Globex 940, which ends at the WMS

        Assert.False(byName["Billing"].Direct); // relies on ERP → Portal, two steps downstream
        Assert.Contains("Relies on Flow 14 (indirectly affected)", byName["Billing"].Reasons);

        Assert.Equal(ProcessImpactKind.SubProcessAffected, byName["Order to Cash"].Kind);
        Assert.True(byName["Order to Cash"].IsTopLevel);
        Assert.Equal(3, byName["Order to Cash"].Reasons.Count);
    }

    [Fact]
    public void Roadmap_items_on_the_subject_and_its_direct_neighbors_are_listed_in_flight_first()
    {
        var r = Sample().Analyze(A(3), 2);

        Assert.Equal([500, 501], r.Initiatives.Select(i => i.Item.ItemId)); // Portal is two steps away: its item is left out
        Assert.True(r.Initiatives[0].Item.InFlight);
        Assert.True(r.Initiatives[0].OnSubject);
        Assert.Equal(["TMS", "WMS"], r.Initiatives[0].Applications);
        Assert.False(r.Initiatives[1].Item.InFlight);
        Assert.Equal(1, r.InFlightInitiatives);
    }

    [Fact]
    public void Vendors_of_the_subject_and_its_direct_neighbors_come_with_escalation_and_renewal()
    {
        var r = Sample().Analyze(A(3), 2);

        Assert.Equal(["Northwind", "Contoso EDI Services", "Fabrikam"], r.Vendors.Select(v => v.Vendor.Name));
        var northwind = r.Vendors[0];
        Assert.True(northwind.SuppliesSubject);
        Assert.Equal(["Taylor AM", "VP Avery"], northwind.Escalation.Select(e => e.Contact!.Name)); // level order
        Assert.Equal(Today.AddDays(20), northwind.NextRenewal!.NoticeDeadline);
    }

    [Fact]
    public void Open_risks_and_pain_points_come_along()
    {
        var r = Sample().Analyze(A(3), 2);

        Assert.Equal([1], r.OpenRisks.Select(x => x.Risk.Id)); // the closed one is left out
        Assert.Equal([1], r.OpenPainPoints.Select(x => x.PainPoint.Id));
    }

    [Theory]
    [InlineData("WMS", ImpactSeverity.High)]    // Tier 1 application
    [InlineData("TMS", ImpactSeverity.High)]    // a Tier 1 integration (WMS → TMS) is directly affected
    [InlineData("EDI", ImpactSeverity.High)]    // carries a key partner's flows
    [InlineData("ERP", ImpactSeverity.Medium)]  // Tier 2 integration, Billing process
    public void Severity_reflects_tiers_key_partners_and_processes(string app, ImpactSeverity expected)
    {
        var w = Sample();
        var id = w.Apps.Values.Single(a => a.Name == app).Id;
        var r = w.Analyze(A(id), 1);
        Assert.Equal(expected, r.Severity);
        Assert.NotEmpty(r.SeverityReasons);
    }

    [Fact]
    public void Only_tier_3_flows_and_nothing_else_is_low_impact()
    {
        var w = Sample();
        w.Processes.Clear();
        w.Initiatives.Clear();
        var r = w.Analyze(A(6), 1);
        Assert.Equal([14], r.Integrations.Select(i => i.Integration.Id));
        Assert.Equal(ImpactSeverity.Low, r.Severity);
    }

    [Fact]
    public void An_application_with_no_live_integrations_has_an_empty_low_report()
    {
        var w = Sample();
        w.App(9, "Spreadsheet", Criticality.Tier3);
        var r = w.Analyze(A(9), 2);
        Assert.Empty(r.Integrations);
        Assert.Empty(r.Applications);
        Assert.Equal(ImpactSeverity.Low, r.Severity);
        Assert.Equal("Spreadsheet", r.SubjectName);
    }

    [Fact]
    public void The_text_report_has_every_section()
    {
        var text = ImpactAnalyzer.ToText(Sample().Analyze(A(3), 2));

        Assert.StartsWith("Change impact: WMS", text);
        Assert.Contains("HIGH IMPACT: WMS is a Tier 1 application", text);
        foreach (var section in new[] { "Integrations (6)", "Trading partners (3)", "Applications (4)", "Business processes (4)",
                     "Roadmap items (2)", "Vendors to coordinate with (3)", "Open risks (1)", "Open pain points (1)" })
            Assert.Contains(section, text);
        Assert.Contains("Acme [Key partner]", text);
        Assert.Contains("(in flight)", text);
        Assert.Contains("Flow 10 [Tier 2, API, High risk] Acme → EDI → WMS", text);
    }
}

internal static class ListExtensions
{
    public static T AddAndReturn<T>(this List<T> list, T item)
    {
        list.Add(item);
        return item;
    }
}
