using Roadmapper.Core.Portfolio;

namespace Roadmapper.Tests;

public class PortfolioGraphTests
{
    private static NodeRef A(int id) => NodeRef.App(id);
    private static NodeRef P(int id) => NodeRef.Partner(id);

    // Partner 1 customer, partner 7 carrier; apps 2 EDI, 3 WMS, 4 TMS, 5 ERP, 6 Portal
    private static PortfolioGraph Sample() => new([
        new GraphEdge(10, P(1), A(3), Via: A(2)),           // customer → WMS via EDI
        new GraphEdge(11, A(3), A(4)),                      // WMS → TMS
        new GraphEdge(12, A(4), P(7)),                      // TMS → carrier
        new GraphEdge(13, A(3), A(5)),                      // WMS → ERP
        new GraphEdge(14, A(5), A(6)),                      // ERP → portal
        new GraphEdge(15, A(6), A(3), Bidirectional: true), // portal ⇄ WMS
    ]);

    [Fact]
    public void Downstream_follows_flows_by_distance()
    {
        var down = Sample().Downstream(A(3), 5).ToDictionary(r => r.Node);
        Assert.Equal(1, down[A(4)].Distance);
        Assert.Equal(1, down[A(5)].Distance);
        Assert.Equal(1, down[A(6)].Distance); // two-way: WMS also sends to the portal
        Assert.Equal(2, down[P(7)].Distance);
        Assert.False(down.ContainsKey(A(3)));
        Assert.False(down.ContainsKey(P(1)));
    }

    [Fact]
    public void Depth_limits_how_far_the_walk_goes()
    {
        var down = Sample().Downstream(A(3), 1).Select(r => r.Node).ToHashSet();
        Assert.Equal([A(4), A(5), A(6)], down.Order());
    }

    [Fact]
    public void A_middle_hop_is_both_a_direct_dependent_and_upstream_of_the_target()
    {
        var g = Sample();
        var fromCustomer = g.Downstream(P(1), 1).ToDictionary(r => r.Node);
        Assert.Equal(1, fromCustomer[A(2)].Distance);
        Assert.Equal(1, fromCustomer[A(3)].Distance);
        Assert.Equal([10], fromCustomer[A(3)].IntegrationIds);

        var intoWms = g.Upstream(A(3), 1).Select(r => r.Node).ToHashSet();
        Assert.Contains(P(1), intoWms);
        Assert.Contains(A(2), intoWms);
        Assert.Contains(A(6), intoWms); // two-way flow
    }

    [Fact]
    public void Cycles_terminate_and_each_node_appears_once()
    {
        var g = new PortfolioGraph([new GraphEdge(1, A(1), A(2)), new GraphEdge(2, A(2), A(3)), new GraphEdge(3, A(3), A(1))]);
        var down = g.Downstream(A(1), 10);
        Assert.Equal([A(2), A(3)], down.Select(r => r.Node));
        Assert.Equal([1, 2], down.Select(r => r.Distance));
    }

    [Fact]
    public void Parallel_integrations_are_all_reported()
    {
        var g = new PortfolioGraph([new GraphEdge(7, A(1), A(2)), new GraphEdge(8, A(1), A(2))]);
        Assert.Equal([7, 8], g.Downstream(A(1), 1).Single().IntegrationIds);
    }

    [Fact]
    public void Touching_includes_source_target_and_via()
    {
        var g = Sample();
        Assert.Equal([10], g.IntegrationsTouching(A(2)));
        Assert.Equal([10, 11, 13, 15], g.IntegrationsTouching(A(3)).Order());
        Assert.Equal([12], g.IntegrationsTouching(P(7)));
        Assert.Empty(g.IntegrationsTouching(A(99)));
    }

    [Fact]
    public void Applications_and_partners_with_the_same_id_are_different_nodes()
    {
        var g = new PortfolioGraph([new GraphEdge(1, P(5), A(5))]);
        Assert.Equal([A(5)], g.Downstream(P(5), 2).Select(r => r.Node));
        Assert.Equal([P(5)], g.Upstream(A(5), 2).Select(r => r.Node));
    }

    [Fact]
    public void Two_customers_sharing_a_WMS_do_not_depend_on_each_other()
    {
        // Partner 1 ACH and partner 2 PCP, both two-way EDI with the WMS (app 1) through the integration platform (app 2).
        var g = new PortfolioGraph([
            new GraphEdge(1, P(1), A(1), Via: A(2), Bidirectional: true),
            new GraphEdge(3, P(2), A(1), Via: A(2), Bidirectional: true),
        ]);
        foreach (var depth in new[] { 1, 2, 5 })
        {
            Assert.Equal([A(1), A(2)], g.Downstream(P(2), depth).Select(r => r.Node).Order());
            Assert.Equal([A(1), A(2)], g.Upstream(P(2), depth).Select(r => r.Node).Order());
        }
        // From the WMS, both customers are direct neighbors.
        Assert.Equal([P(1), P(2)], g.Downstream(A(1), 2).Where(r => r.Node.IsPartner).Select(r => r.Node).Order());
        Assert.Equal(new[] { A(1), A(2), P(2) }.Order(), MapLayout.FocusColumns(g, P(2), 5).Keys.Order());
    }

    [Fact]
    public void A_partner_ignores_other_partners_flows_through_a_shared_app()
    {
        // Morton (partner 3) ⇄ Synapse (app 1) via Boomi (app 5); PCP (partner 2) ⇄ Synapse via Cleo (app 2);
        // Synapse → TMS (app 4) is an application-to-application flow.
        var g = new PortfolioGraph([
            new GraphEdge(5, P(3), A(1), Via: A(5), Bidirectional: true),
            new GraphEdge(3, P(2), A(1), Via: A(2), Bidirectional: true),
            new GraphEdge(6, A(1), A(4)),
        ]);
        var down = g.Downstream(P(3), 5).Select(r => r.Node).Order();
        Assert.Equal([A(1), A(4), A(5)], down);   // Synapse, TMS, Boomi; not Cleo (PCP's flow)
        Assert.Equal([A(1), A(5)], g.Upstream(P(3), 5).Select(r => r.Node).Order());
        Assert.DoesNotContain(A(2), MapLayout.FocusColumns(g, P(3), 5).Keys);

        // From Synapse itself, everything it exchanges data with still counts.
        Assert.Equal([A(2), A(4), A(5), P(2), P(3)], g.Downstream(A(1), 2).Select(r => r.Node).Order());
    }

    [Fact]
    public void Walks_stop_at_partners_instead_of_passing_through_them()
    {
        // TMS → carrier and carrier → reporting: the carrier doesn't relay TMS data onward.
        var g = new PortfolioGraph([new GraphEdge(1, A(1), P(9)), new GraphEdge(2, P(9), A(2))]);
        Assert.Equal([P(9)], g.Downstream(A(1), 5).Select(r => r.Node));
        Assert.Equal([A(2)], g.Downstream(P(9), 5).Select(r => r.Node));
    }

    [Theory]
    [InlineData("a12", NodeKind.Application, 12)]
    [InlineData("p3", NodeKind.Partner, 3)]
    public void Node_keys_round_trip(string key, NodeKind kind, int id)
    {
        Assert.Equal(new NodeRef(kind, id), NodeRef.Parse(key));
        Assert.Equal(key, new NodeRef(kind, id).Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x5")]
    [InlineData("a")]
    [InlineData("a0")]
    public void Bad_node_keys_parse_to_null(string? key) => Assert.Null(NodeRef.Parse(key));
}

public class MapLayoutTests
{
    [Fact]
    public void Yard_management_sits_with_warehouse_and_transport()
    {
        Assert.Equal(MapLayout.CategoryColumn(AppCategory.WMS), MapLayout.CategoryColumn(AppCategory.YMS));
        Assert.Equal("YMS", PortfolioText.Label(AppCategory.YMS));
    }

    private static NodeRef A(int id) => NodeRef.App(id);
    private static NodeRef P(int id) => NodeRef.Partner(id);

    [Fact]
    public void Layout_is_deterministic_and_keeps_columns_in_order()
    {
        var nodes = new[] { new MapNode(A(1), "B", 3), new MapNode(A(2), "A", 0), new MapNode(A(3), "C", 3), new MapNode(A(4), "D", 5) };
        var edges = new[] { new MapEdge(A(2), A(3)), new MapEdge(A(3), A(4)) };
        var first = MapLayout.Layout(nodes, edges);
        var second = MapLayout.Layout(nodes.Reverse(), edges.Reverse());

        Assert.Equal([0, 3, 5], first.ColumnKeys);
        Assert.Equal(first.Width, second.Width);
        foreach (var id in new[] { 1, 2, 3, 4 })
            Assert.Equal(first.Boxes[A(id)], second.Boxes[A(id)]);
        Assert.True(first.Boxes[A(2)].X < first.Boxes[A(1)].X && first.Boxes[A(1)].X < first.Boxes[A(4)].X);
    }

    [Fact]
    public void Neighbors_pull_nodes_level_to_avoid_crossings()
    {
        // Left column A (top), B (bottom). Right column: X connects to B, Y connects to A. Alphabetical order would cross.
        var nodes = new[] { new MapNode(A(1), "A", 0), new MapNode(A(2), "B", 0), new MapNode(A(3), "X", 1), new MapNode(A(4), "Y", 1) };
        var layout = MapLayout.Layout(nodes, [new MapEdge(A(2), A(3)), new MapEdge(A(1), A(4))]);
        Assert.True(layout.Boxes[A(4)].Y < layout.Boxes[A(3)].Y);
    }

    [Fact]
    public void Focus_columns_put_upstream_left_and_downstream_right()
    {
        var g = new PortfolioGraph([new GraphEdge(1, P(1), A(2)), new GraphEdge(2, A(2), A(3)), new GraphEdge(3, A(3), A(4)),
            new GraphEdge(4, A(2), A(5), Bidirectional: true)]);
        var columns = MapLayout.FocusColumns(g, A(2), 2);
        Assert.Equal(0, columns[A(2)]);
        Assert.Equal(-1, columns[P(1)]);
        Assert.Equal(1, columns[A(3)]);
        Assert.Equal(2, columns[A(4)]);
        Assert.Equal(1, columns[A(5)]); // two-way, recorded as app 2 → app 5: downstream
    }

    [Fact]
    public void An_integration_platform_sees_only_the_flows_it_carries_in_their_recorded_direction()
    {
        // Morton (partner 3) ⇄ Synapse (app 1) via Boomi (app 5); PCP (partner 2) ⇄ Synapse via Cleo (app 2);
        // ACH (partner 1) ⇄ Synapse via Cleo; Synapse → PCP by SFTP.
        var g = new PortfolioGraph([
            new GraphEdge(5, P(3), A(1), Via: A(5), Bidirectional: true),
            new GraphEdge(3, P(2), A(1), Via: A(2), Bidirectional: true),
            new GraphEdge(1, P(1), A(1), Via: A(2), Bidirectional: true),
            new GraphEdge(4, A(1), P(2)),
        ]);

        // Boomi carries only Morton's data: Morton | Boomi | Synapse, and nothing of Cleo's customers.
        var boomi = MapLayout.FocusColumns(g, A(5), 5);
        Assert.Equal(new[] { A(1), A(5), P(3) }.Order(), boomi.Keys.Order());
        Assert.Equal(-1, boomi[P(3)]);
        Assert.Equal(1, boomi[A(1)]);
        Assert.Equal([A(1), P(3)], g.Downstream(A(5), 5).Select(r => r.Node).Order());

        // Cleo carries ACH's and PCP's data, not Morton's.
        Assert.DoesNotContain(P(3), g.Downstream(A(2), 5).Select(r => r.Node));
        Assert.DoesNotContain(A(5), g.Downstream(A(2), 5).Select(r => r.Node));
    }

    [Fact]
    public void A_routed_integration_lines_up_source_hop_and_target()
    {
        // Customer ⇄ WMS (app 1) via the integration platform (app 2): customer | platform | WMS.
        var g = new PortfolioGraph([new GraphEdge(1, P(1), A(1), Via: A(2), Bidirectional: true), new GraphEdge(2, A(1), A(3))]);
        var columns = MapLayout.FocusColumns(g, P(1), 1);
        Assert.Equal(1, columns[A(2)]);
        Assert.Equal(2, columns[A(1)]);
        Assert.False(columns.ContainsKey(A(3))); // two integrations away: outside depth 1

        // From the WMS's side, the flow is recorded customer → WMS: customer | platform | WMS, with app 3 downstream.
        var fromWms = MapLayout.FocusColumns(g, A(1), 1);
        Assert.Equal(-1, fromWms[A(2)]);
        Assert.Equal(-2, fromWms[P(1)]);
        Assert.Equal(1, fromWms[A(3)]);
    }

    [Fact]
    public void A_direct_return_flow_does_not_pull_the_target_out_of_line()
    {
        // PCP ⇄ Synapse (app 1) via Cleo (app 2), plus a one-way SFTP straight from Synapse to PCP.
        var g = new PortfolioGraph([
            new GraphEdge(3, P(2), A(1), Via: A(2), Bidirectional: true),
            new GraphEdge(4, A(1), P(2)),
        ]);
        var columns = MapLayout.FocusColumns(g, P(2), 2);
        Assert.Equal(1, columns[A(2)]); // Cleo
        Assert.Equal(2, columns[A(1)]); // Synapse, beyond Cleo; the SFTP curves back to PCP
    }

    [Fact]
    public void Nodes_only_upstream_still_go_left()
    {
        var g = new PortfolioGraph([new GraphEdge(1, A(1), A(2)), new GraphEdge(2, A(3), A(1), Via: A(4))]);
        var columns = MapLayout.FocusColumns(g, A(1), 2);
        Assert.Equal(1, columns[A(2)]);
        Assert.Equal(-1, columns[A(4)]);
        Assert.Equal(-2, columns[A(3)]);
    }

    [Fact]
    public void Empty_input_gives_an_empty_layout()
    {
        var layout = MapLayout.Layout([], []);
        Assert.Empty(layout.Boxes);
        Assert.Empty(layout.ColumnKeys);
    }
}

public class IntegrationRiskRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static Integration With(Criticality tier, DateOnly? reviewed, params (int L, int I, RiskStatus S)[] risks) => new()
    {
        Criticality = tier,
        LastReviewDate = reviewed,
        ReviewIntervalMonths = 12,
        Risks = risks.Select(r => new IntegrationRisk { Likelihood = r.L, Impact = r.I, Status = r.S }).ToList(),
    };

    [Fact]
    public void Score_of_15_is_high_on_any_tier() =>
        Assert.Equal(RiskLevel.High, PortfolioRules.RiskLevel(With(Criticality.Tier3, Today, (3, 5, RiskStatus.Open)), Today));

    [Fact]
    public void Score_of_8_is_high_only_on_tier_1()
    {
        Assert.Equal(RiskLevel.High, PortfolioRules.RiskLevel(With(Criticality.Tier1, Today, (2, 4, RiskStatus.Open)), Today));
        Assert.Equal(RiskLevel.Medium, PortfolioRules.RiskLevel(With(Criticality.Tier2, Today, (2, 4, RiskStatus.Open)), Today));
    }

    [Fact]
    public void Mitigated_and_closed_risks_do_not_count_but_accepted_ones_do()
    {
        Assert.Equal(RiskLevel.Low, PortfolioRules.RiskLevel(With(Criticality.Tier1, Today, (5, 5, RiskStatus.Mitigated), (5, 5, RiskStatus.Closed)), Today));
        Assert.Equal(RiskLevel.High, PortfolioRules.RiskLevel(With(Criticality.Tier3, Today, (5, 5, RiskStatus.Accepted)), Today));
    }

    [Fact]
    public void An_overdue_or_missing_review_makes_it_at_least_medium()
    {
        Assert.Equal(RiskLevel.Medium, PortfolioRules.RiskLevel(With(Criticality.Tier3, Today.AddMonths(-13)), Today));
        Assert.Equal(RiskLevel.Medium, PortfolioRules.RiskLevel(With(Criticality.Tier3, null), Today));
        Assert.Equal(RiskLevel.Low, PortfolioRules.RiskLevel(With(Criticality.Tier3, Today.AddMonths(-11)), Today));
    }

    [Fact]
    public void Retired_integrations_are_low_and_never_overdue()
    {
        var retired = With(Criticality.Tier1, null, (5, 5, RiskStatus.Open));
        retired.Status = LifecycleStatus.Retired;
        Assert.Equal(RiskLevel.Low, PortfolioRules.RiskLevel(retired, Today));
        Assert.False(PortfolioRules.ReviewOverdue(retired, Today));
    }
}
