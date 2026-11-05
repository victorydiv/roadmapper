using Roadmapper.Core;

namespace Roadmapper.Tests;

public class PriorityMatrixTests
{
    private static RoadmapItem Rice(int id, double reach, double effort) =>
        new() { Id = id, Score = new ItemScore { Reach = reach, Impact = 1, Confidence = 100, Effort = effort } };

    [Fact]
    public void Splits_quadrants_at_the_medians()
    {
        var result = PriorityMatrix.Build(
            [Rice(1, 100, 1), Rice(2, 100, 10), Rice(3, 10, 1), Rice(4, 10, 10), new RoadmapItem { Id = 5, Score = new ItemScore() }],
            new RiceStrategy());

        Assert.Equal(55, result.ValueSplit);
        Assert.Equal(5.5, result.EffortSplit);
        var byId = result.Points.ToDictionary(p => p.Item.Id, p => p.Quadrant);
        Assert.Equal(Quadrant.QuickWin, byId[1]);
        Assert.Equal(Quadrant.BigBet, byId[2]);
        Assert.Equal(Quadrant.FillIn, byId[3]);
        Assert.Equal(Quadrant.TimeSink, byId[4]);
        Assert.Equal(5, Assert.Single(result.Unplotted).Id);
    }

    [Fact]
    public void Each_model_supplies_value_and_effort()
    {
        Assert.Equal((640, 2), new RiceStrategy().MatrixPoint(new ItemScore { Reach = 800, Impact = 2, Confidence = 40, Effort = 2 }));
        Assert.Equal((16, 4), new WsjfStrategy().MatrixPoint(new ItemScore { BusinessValue = 8, TimeCriticality = 5, RiskReduction = 3, JobSize = 4 }));
        Assert.Equal((4, 3), new MoscowStrategy().MatrixPoint(new ItemScore { Moscow = MoscowPriority.MustHave, Effort = 3 }));
        Assert.Null(new MoscowStrategy().MatrixPoint(new ItemScore { Moscow = MoscowPriority.MustHave }));
    }

    [Fact]
    public void Median_handles_odd_even_and_empty() =>
        Assert.Equal([2.0, 2.5, 0.0], [PriorityMatrix.Median([3, 1, 2]), PriorityMatrix.Median([4, 1, 2, 3]), PriorityMatrix.Median([])]);
}
