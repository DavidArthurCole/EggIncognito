using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

public class RobustStatsTests {
    [Fact]
    public void Median_EmptyList_ReturnsZero() => Assert.Equal(0, RobustStats.Median([]));

    [Fact]
    public void Median_OddCount_ReturnsMiddleValue() =>
        Assert.Equal(3, RobustStats.Median([5, 1, 3, 2, 4]));

    [Fact]
    public void Median_EvenCount_AveragesMiddlePair() =>
        Assert.Equal(2.5, RobustStats.Median([1, 2, 3, 4]));

    [Fact]
    public void Mad_BasicCase_ReturnsMedianOfAbsoluteDeviations() {
        double[] xs = [1, 2, 3, 4, 100];
        double median = RobustStats.Median(xs);
        Assert.Equal(1, RobustStats.Mad(xs, median));
    }
}
