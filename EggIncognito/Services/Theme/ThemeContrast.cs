namespace EggIncognito.Services.Theme;

public sealed record ContrastFailure(string Check, string A, string B, double Measured, double Required, double? AtHue);

public sealed record ContrastResult(bool Passes, IReadOnlyList<ContrastFailure> Failures);

public static class ThemeContrast {
    public const double FgFloor = 4.5;
    public const double MutedFloor = 3.0;
    public const double StatusFloor = 3.0;
    public const double BorderFloor = 1.15;
    public const double DistinguishFloor = 0.04;
    private const int HueSteps = 24;

    private static readonly string[] Surfaces = ["bg", "panel0", "panel", "panel2"];
    private static readonly string[] StatusTokens = ["accent", "ok", "err", "info"];

    public static ContrastResult Validate(ThemeModel model) {
        var failures = new List<ContrastFailure>();
        var colors = new Dictionary<string, ThemeColor>(StringComparer.Ordinal);
        foreach (string name in ThemeTokens.Settable) colors[name] = model.ResolveToken(name);

        foreach (string surface in Surfaces) {
            Check(failures, "contrast", "fg", surface, ThemeColor.Contrast(colors["fg"], colors[surface]), FgFloor);
            Check(failures, "contrast", "muted", surface, ThemeColor.Contrast(colors["muted"], colors[surface]),
                MutedFloor);
            Check(failures, "contrast", "border", surface, ThemeColor.Contrast(colors["border"], colors[surface]),
                BorderFloor);
            foreach (string status in StatusTokens) {
                Check(failures, "contrast", status, surface, ThemeColor.Contrast(colors[status], colors[surface]),
                    StatusFloor);
            }
        }

        for (int i = 0; i < StatusTokens.Length; i++) {
            for (int j = i + 1; j < StatusTokens.Length; j++) {
                double d = ThemeColor.DeltaE(colors[StatusTokens[i]], colors[StatusTokens[j]]);
                Check(failures, "distinguish", StatusTokens[i], StatusTokens[j], d, DistinguishFloor);
            }
        }

        if (model.Chroma.HueRotate is { Enabled: true }) SweepHues(failures, colors);

        return new ContrastResult(failures.Count == 0, failures);
    }

    private static void SweepHues(List<ContrastFailure> failures, Dictionary<string, ThemeColor> colors) {
        var worstContrast = new Dictionary<string, (double Value, double Hue)>(StringComparer.Ordinal);
        var worstDelta = new Dictionary<string, (double Value, double Hue)>(StringComparer.Ordinal);
        for (int i = 0; i < HueSteps; i++) {
            var rotated = colors["accent"].RotateHue(i * (360.0 / HueSteps));
            foreach (string surface in Surfaces) {
                double c = ThemeColor.Contrast(rotated, colors[surface]);
                if (!worstContrast.TryGetValue(surface, out var cur) || c < cur.Value)
                    worstContrast[surface] = (c, rotated.H);
            }

            foreach (string status in StatusTokens.Where(s => s != "accent")) {
                double d = ThemeColor.DeltaE(rotated, colors[status]);
                if (!worstDelta.TryGetValue(status, out var cur) || d < cur.Value)
                    worstDelta[status] = (d, rotated.H);
            }
        }

        foreach (var (surface, (value, hue)) in worstContrast) {
            if (value < StatusFloor)
                failures.Add(new ContrastFailure("contrast", "accent", surface, Round(value), StatusFloor, hue));
        }

        foreach (var (status, (value, hue)) in worstDelta) {
            if (value < DistinguishFloor)
                failures.Add(new ContrastFailure("distinguish", "accent", status, Round(value), DistinguishFloor, hue));
        }
    }

    private static void Check(List<ContrastFailure> failures, string check, string a, string b,
        double measured, double required) {
        if (measured < required) failures.Add(new ContrastFailure(check, a, b, Round(measured), required, null));
    }

    private static double Round(double v) => Math.Round(v, 4);
}
