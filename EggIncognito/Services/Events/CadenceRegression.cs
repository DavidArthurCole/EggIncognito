using EggIncognito.Models.Events;

namespace EggIncognito.Services.Events;

public static class CadenceRegression {
    public static CadenceFit? Fit(IReadOnlyList<double> starts) {
        var n = starts.Count;
        if (n < 4) return null;
        var xBar = (n - 1) / 2.0;
        var yBar = starts.Average();
        double num = 0, den = 0;
        for (var i = 0; i < n; i++) {
            var dx = i - xBar;
            num += dx * (starts[i] - yBar);
            den += dx * dx;
        }

        var slope = den == 0 ? 0 : num / den;
        var intercept = yBar - slope * xBar;
        var residualMad = ResidualMad(starts, slope, intercept);
        var goodness = slope > 0 ? residualMad / slope : double.PositiveInfinity;
        return new CadenceFit(slope, intercept, slope * n + intercept, n, residualMad, goodness);
    }

    private static double ResidualMad(IReadOnlyList<double> starts, double slope, double intercept) {
        var abs = new double[starts.Count];
        for (var i = 0; i < abs.Length; i++) abs[i] = Math.Abs(starts[i] - (slope * i + intercept));
        Array.Sort(abs);
        var mid = abs.Length / 2;
        return abs.Length % 2 == 1 ? abs[mid] : (abs[mid - 1] + abs[mid]) / 2;
    }
}
