using EggIdentity.UI;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Theme;

public sealed class ThemeDraft {
    public string Name { get; set; } = "My theme";
    public string Slug { get; set; } = "my-theme";
#pragma warning disable IDE0028
    public Dictionary<string, string> TokenHex { get; } = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    public double? AccentL { get; set; }
    public double? AccentC { get; set; }
    public double? AccentH { get; set; }
    public double SurfaceTint { get; set; }
    public double GradientHueShift { get; set; }
    public double GlowRadius { get; set; }
    public double GlowAlpha { get; set; }
    public bool HueRotateEnabled { get; set; }
    public double HueRotateSeconds { get; set; } = 30;
    public string Css { get; set; } = "";

    public void LoadFrom(ThemeModel model) {
        Name = model.Name;
        Slug = model.Slug;
        TokenHex.Clear();
        AccentL = null;
        AccentC = null;
        AccentH = null;
        foreach (string token in ThemeTokens.Settable) {
            var color = model.ResolveToken(token);
            if (token == "accent" && color.Hex is null) {
                AccentL = color.L;
                AccentC = color.C;
                AccentH = color.H;
            }

            TokenHex[token] = color.Hex ?? OklchToHexFallback(color);
        }

        SurfaceTint = model.Chroma.SurfaceTint;
        GradientHueShift = model.Chroma.GradientHueShift;
        GlowRadius = model.Chroma.GlowRadius;
        GlowAlpha = model.Chroma.GlowAlpha;
        HueRotateEnabled = model.Chroma.HueRotate?.Enabled ?? false;
        HueRotateSeconds = model.Chroma.HueRotate?.Seconds ?? 30;
        Css = model.Css;
    }

    public ThemeModel ToModel() {
        var tokens = new Dictionary<string, ThemeTokenValue>(StringComparer.Ordinal);
        foreach (string token in ThemeTokens.Settable) {
            if (token == "accent" && AccentL is { } l && AccentC is { } c && AccentH is { } h) {
                tokens[token] = new ThemeTokenValue(L: l, C: c, H: h);
                continue;
            }

            if (TokenHex.TryGetValue(token, out string? hex) && ThemeColor.FromHex(hex) is not null)
                tokens[token] = new ThemeTokenValue(Hex: hex.ToLowerInvariant());
        }

        var chroma = new ThemeChroma(SurfaceTint, GradientHueShift, GlowRadius, GlowAlpha,
            new ThemeHueRotate(HueRotateEnabled, HueRotateSeconds)).Clamped();
        return new ThemeModel(ThemeModel.SchemaId, Name, Slug, ThemeModel.CurrentSchemaVersion, tokens, chroma, Css);
    }

    private static string OklchToHexFallback(ThemeColor color) {
        var (r, g, b, _) = color.ToLinearSrgb();
        return $"#{Channel(r):x2}{Channel(g):x2}{Channel(b):x2}";
    }

    private static int Channel(double linear) {
        double srgb = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        return Math.Clamp((int)Math.Round(srgb * 255.0), 0, 255);
    }
}

public sealed class ThemeWorkbenchState : WorkbenchStateBase {
    public const string ModeTokens = "tokens";
    public const string ModeChroma = "chroma";
    public const string ModeCss = "css";
    public const string ModeJson = "json";

    private static readonly IReadOnlyList<WorkbenchMode> RawModes = [
        new(ModeTokens, "Tokens"),
        new(ModeChroma, "Chroma"),
        new(ModeCss, "Custom CSS"),
        new(ModeJson, "JSON")
    ];

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } =
        [.. RawModes.Select(m => (m.Key, m.Label, m.Count))];

    public ThemeDraft Draft { get; } = new();
    public string? SelectedSlug { get; set; }
    public bool Open { get; set; }

    public void LoadPreset(ThemeModel preset) {
        Draft.LoadFrom(preset);
        SelectedSlug = null;
    }
}
