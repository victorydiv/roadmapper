using Roadmapper.Core.Portfolio;

namespace Roadmapper.Tests;

public class PortfolioGraphTests
{
    // 1 Customers, 2 EDI, 3 WMS, 4 TMS, 5 ERP, 6 Portal, 7 Carriers
    private static PortfolioGraph Sample() => new([
        new GraphEdge(10, 1, 3, ViaId: 2),        // customers → WMS via EDI
        new GraphEdge(11, 3, 4),                  // WMS → TMS
        new GraphEdge(12, 4, 7),                  // TMS → carriers
        new GraphEdge(13, 3, 5),                  // WMS → ERP
        new GraphEdge(14, 5, 6),                  // ERP → portal
        new GraphEdge(15, 6, 3, Bidirectional: true), // portal ⇄ WMS
    ]);

    [Fact]
    public void Downstream_follows_flows_by_distance()
    {
        var down = Sample().Downstream(3, 5).ToDictionary(r => r.ApplicationId);
        Assert.Equal(1, down[4].Distance);
        Assert.Equal(1, down[5].Distance);
        Assert.Equal(1, down[6].Distance); // bidirectional: WMS also sends to the portal
        Assert.Equal(2, down[7].Distance);
        Assert.False(down.ContainsKey(3));
        Assert.False(down.ContainsKey(1));
    }

    [Fact]
    public void Depth_limits_how_far_the_walk_goes()
    {
        var down = Sample().Downstream(3, 1).Select(r => r.ApplicationId).ToHashSet();
        Assert.Equal([4, 5, 6], down.Order());
    }

    [Fact]
    public void A_middle_hop_is_both_a_direct_dependent_and_upstream_of_the_target()
    {
        var g = Sample();
        var fromCustomers = g.Downstream(1, 1).ToDictionary(r => r.ApplicationId);
        Assert.Equal(1, fromCustomers[2].Distance);
        Assert.Equal(1, fromCustomers[3].Distance);
        Assert.Equal([10], fromCustomers[3].IntegrationIds);

        var intoWms = g.Upstream(3, 1).Select(r => r.ApplicationId).ToHashSet();
        Assert.Contains(1, intoWms);
        Assert.Contains(2, intoWms);
        Assert.Contains(6, intoWms); // bidirectional partner
    }

    [Fact]
    public void Cycles_terminate_and_each_application_appears_once()
    {
        var g = new PortfolioGraph([new GraphEdge(1, 1, 2), new GraphEdge(2, 2, 3), new GraphEdge(3, 3, 1)]);
        var down = g.Downstream(1, 10);
        Assert.Equal([2, 3], down.Select(r => r.ApplicationId));
        Assert.Equal([1, 2], down.Select(r => r.Distance));
    }

    [Fact]
    public void Parallel_integrations_are_all_reported()
    {
        var g = new PortfolioGraph([new GraphEdge(7, 1, 2), new GraphEdge(8, 1, 2)]);
        Assert.Equal([7, 8], g.Downstream(1, 1).Single().IntegrationIds);
    }

    [Fact]
    public void Touching_includes_source_target_and_via()
    {
        var g = Sample();
        Assert.Equal([10], g.IntegrationsTouching(2));
        Assert.Equal([10, 11, 13, 15], g.IntegrationsTouching(3).Order());
        Assert.Empty(g.IntegrationsTouching(99));
    }
}

public class MapLayoutTests
{
    [Fact]
    public void Layout_is_deterministic_and_keeps_columns_in_order()
    {
        var nodes = new[] { new MapNode(1, "B", 3), new MapNode(2, "A", 0), new MapNode(3, "C", 3), new MapNode(4, "D", 5) };
        var edges = new[] { new MapEdge(2, 3), new MapEdge(3, 4) };
        var first = MapLayout.Layout(nodes, edges);
        var second = MapLayout.Layout(nodes.Reverse(), edges.Reverse());

        Assert.Equal([0, 3, 5], first.ColumnKeys);
        Assert.Equal(first.Width, second.Width);
        foreach (var id in new[] { 1, 2, 3, 4 })
            Assert.Equal(first.Boxes[id], second.Boxes[id]);
        Assert.True(first.Boxes[2].X < first.Boxes[1].X && first.Boxes[1].X < first.Boxes[4].X);
    }

    [Fact]
    public void Neighbors_pull_nodes_level_to_avoid_crossings()
    {
        // Left column A (top), B (bottom). Right column: X connects to B, Y connects to A. Alphabetical order would cross.
        var nodes = new[] { new MapNode(1, "A", 0), new MapNode(2, "B", 0), new MapNode(3, "X", 1), new MapNode(4, "Y", 1) };
        var layout = MapLayout.Layout(nodes, [new MapEdge(2, 3), new MapEdge(1, 4)]);
        Assert.True(layout.Boxes[4].Y < layout.Boxes[3].Y);
    }

    [Fact]
    public void Focus_columns_put_upstream_left_and_downstream_right()
    {
        var g = new PortfolioGraph([new GraphEdge(1, 1, 2), new GraphEdge(2, 2, 3), new GraphEdge(3, 3, 4), new GraphEdge(4, 2, 5, Bidirectional: true)]);
        var columns = MapLayout.FocusColumns(g, 2, 2);
        Assert.Equal(0, columns[2]);
        Assert.Equal(-1, columns[1]);
        Assert.Equal(1, columns[3]);
        Assert.Equal(2, columns[4]);
        Assert.Equal(1, columns[5]); // both up- and downstream: ties go downstream
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
