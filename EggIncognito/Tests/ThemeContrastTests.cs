using EggIncognito.Services.Theme;

namespace EggIncognito.Tests;

public class ThemeContrastTests {
    [Fact]
    public void EveryShippedPreset_PassesValidation() {
        foreach (var preset in ThemePresets.All) {
            var result = ThemeContrast.Validate(preset);
            Assert.True(result.Passes, preset.Slug + ": " + string.Join("; ",
                result.Failures.Select(f => $"{f.Check} {f.A}/{f.B} {f.Measured} < {f.Required}")));
        }
    }

    [Fact]
    public void LowContrastTheme_IsRejected() {
        var tokens = new Dictionary<string, ThemeTokenValue> {
            ["bg"] = new(Hex: "#808080"),
            ["fg"] = new(Hex: "#8a8a8a")
        };
        var model = ThemePresets.Default with { Tokens = Merge(tokens) };
        var result = ThemeContrast.Validate(model);
        Assert.False(result.Passes);
        Assert.Contains(result.Failures, f => f is { Check: "contrast", A: "fg", B: "bg" });
    }

    [Fact]
    public void AccentTooCloseToErr_FailsDistinguishability() {
        var tokens = new Dictionary<string, ThemeTokenValue> { ["accent"] = new(Hex: "#e0685f") };
        var model = ThemePresets.Default with { Tokens = Merge(tokens) };
        var result = ThemeContrast.Validate(model);
        Assert.False(result.Passes);
        Assert.Contains(result.Failures, f => f.Check == "distinguish");
    }

    [Fact]
    public void ShippedDefault_HoldsTheCalibratedFloors() {
        var result = ThemeContrast.Validate(ThemePresets.Default);
        Assert.True(result.Passes,
            string.Join("; ", result.Failures.Select(f => $"{f.Check} {f.A}/{f.B} {f.Measured}")));
    }

    [Fact]
    public void HueRotation_IsJudgedAtTheWorstHue() {
        var staticModel = ThemePresets.Default;
        Assert.True(ThemeContrast.Validate(staticModel).Passes);

        var rotating = staticModel with {
            Chroma = new ThemeChroma(HueRotate: new ThemeHueRotate(true, 30)).Clamped()
        };
        var result = ThemeContrast.Validate(rotating);
        Assert.False(result.Passes);
        Assert.Contains(result.Failures, f => f is { Check: "distinguish", AtHue: not null });
    }

    [Fact]
    public void PrismPreset_SurvivesItsOwnSweep() {
        var result = ThemeContrast.Validate(ThemePresets.Prism);
        Assert.True(result.Passes, string.Join("; ",
            result.Failures.Select(f => $"{f.Check} {f.A}/{f.B} {f.Measured} at {f.AtHue}")));
    }

    [Fact]
    public void FailureRows_CarryMeasurementAndRequirement() {
        var tokens = new Dictionary<string, ThemeTokenValue> {
            ["muted"] = new(Hex: "#3a3a44")
        };
        var model = ThemePresets.Default with { Tokens = Merge(tokens) };
        var result = ThemeContrast.Validate(model);
        Assert.False(result.Passes);
        foreach (var failure in result.Failures) {
            Assert.True(failure.Measured < failure.Required);
            Assert.True(failure.Measured > 0);
        }
    }

    [Fact]
    public void SubProdGreen_IsNotRejected() {
        Assert.True(ThemeContrast.Validate(ThemePresets.Forest).Passes);
    }

    private static Dictionary<string, ThemeTokenValue> Merge(Dictionary<string, ThemeTokenValue> overrides) {
        var tokens = new Dictionary<string, ThemeTokenValue>(ThemePresets.Default.Tokens, StringComparer.Ordinal);
        foreach (var (key, value) in overrides) tokens[key] = value;
        return tokens;
    }
}
