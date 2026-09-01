using EggIncognito.Services.Events;

namespace EggIncognito.Tests.Predictions;

public class CadenceRegressionTests {
    private const double Day = 86400d;
    private const double Week = 7 * 86400d;

    [Fact]
    public void Fit_FewerThanFourSamples_ReturnsNull() =>
        Assert.Null(CadenceRegression.Fit([0, Week, 2 * Week]));

    [Fact]
    public void Fit_PerfectlyRegularCadence_SlopeMatchesInterval() {
        double[] starts = [0, Week, 2 * Week, 3 * Week, 4 * Week];

        var fit = CadenceRegression.Fit(starts);

        Assert.NotNull(fit);
        Assert.Equal(Week, fit.SlopeSeconds, 6);
        Assert.Equal(0, fit.InterceptSeconds, 6);
        Assert.Equal(starts[^1] + Week, fit.NextEstimate, 6);
        Assert.Equal(5, fit.Samples);
        Assert.Equal(0, fit.ResidualMadSeconds, 6);
        Assert.Equal(0, fit.Goodness, 6);
    }

    [Fact]
    public void Fit_NearPerfectLine_YieldsSmallGoodness() {
        double[] starts = [0, Week - 3600, 2 * Week + 1800, 3 * Week - 900, 4 * Week + 2700];

        var fit = CadenceRegression.Fit(starts);

        Assert.NotNull(fit);
        Assert.True(fit.Goodness < 0.05);
    }

    [Fact]
    public void Fit_ScatteredStarts_YieldsGoodnessAboveThreshold() {
        double[] starts = [0, 3 * Day, 4 * Day, 21 * Day, 22 * Day, 50 * Day];

        var fit = CadenceRegression.Fit(starts);

        Assert.NotNull(fit);
        Assert.True(fit.Goodness > 0.35);
    }

    [Fact]
    public void Fit_NoisyCadence_NextEstimateWithinReasonableBounds() {
        double[] starts = [0, Week - 3600, 2 * Week + 1800, 3 * Week - 900, 4 * Week + 2700];

        var fit = CadenceRegression.Fit(starts);

        Assert.NotNull(fit);
        Assert.InRange(fit.NextEstimate, 4 * Week + Week - 2 * 3600, 4 * Week + Week + 2 * 3600);
    }
}
