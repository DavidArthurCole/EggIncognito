using System.Globalization;
using System.Text;
using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public sealed class ThemeCssEmitter(IWebHostEnvironment env, ILogger<ThemeCssEmitter> logger) {
    private static readonly string[] AllowedAtPrefixes = ["@keyframes", "@media"];

    private bool IsStaging => string.Equals(env.EnvironmentName, "Staging", StringComparison.OrdinalIgnoreCase);

    public static bool UsesHueRotation(ThemeModel model) => model.Chroma.HueRotate is { Enabled: true };

    public string Serialize(ThemeModel model, ThemeScope scope, bool customCssAllowed) {
        string root = scope == ThemeScope.Live ? ThemeCssSerializer.LivePrefix : ThemeCssSerializer.PreviewPrefix;
        var sb = new StringBuilder();
        WriteLane1(sb, model, root);

        if (customCssAllowed && !IsStaging && !string.IsNullOrWhiteSpace(model.Css) && ThemeCss.Parse(model.Css).Ok) {
            var lane2 = ThemeCssSerializer.SerializeLane2(model.Css, scope, ThemeTokens.Catalog, ThemeTokens.Registry,
                ThemeModel.MaxCssSourceBytes);
            if (lane2.Ok) {
                sb.Append(lane2.Output);
            } else if (lane2.Reason == "lane-2 self-check failed") {
                logger.LogError("theme '{Slug}': lane-2 self-check failed, output dropped", model.Slug);
                return "";
            } else if (lane2.Reason == "lane-2 output over size cap") {
                logger.LogWarning("theme '{Slug}': lane-2 output over {Cap} KB, dropped", model.Slug,
                    ThemeCssSerializer.MaxLane2OutputBytes / 1024);
            }
        }

        string output = sb.ToString();
        if (!OutputAlphabetOk(output)) {
            logger.LogError("theme '{Slug}': serializer self-check failed, theme dropped", model.Slug);
            return "";
        }

        return output;
    }

    private void WriteLane1(StringBuilder sb, ThemeModel model, string root) {
        bool staging = IsStaging;
        var chroma = model.Chroma;
        bool hueRotate = !staging && chroma.HueRotate is { Enabled: true };

        sb.Append(root).Append(" {\n");
        foreach (string name in ThemeTokens.Settable) {
            if (staging && name == "accent") continue;
            if (!model.Tokens.ContainsKey(name)) continue;
            var color = model.TokenOrDefault(name);
            if (name == "accent" && hueRotate) {
                sb.Append("  --color-accent: oklch(")
                    .Append(Num(Math.Round(color.L * 100.0, 1))).Append("% ")
                    .Append(Num(Math.Round(color.C, 3))).Append(" calc(")
                    .Append(Num(Math.Round(color.H, 1))).Append("deg + var(--egi-hue-shift)));\n");
                continue;
            }

            sb.Append("  --color-").Append(name).Append(": ").Append(color.ToCss()).Append(";\n");
        }

        if (!staging) {
            if (chroma is { GlowRadius: > 0, GlowAlpha: > 0 }) {
                sb.Append("  --egi-glow: 0 0 ").Append(Num(Math.Round(chroma.GlowRadius, 1)))
                    .Append("px color-mix(in oklab, var(--color-accent) ")
                    .Append(Num(Math.Round(chroma.GlowAlpha, 1))).Append("%, transparent),;\n");
            }

            if (chroma.SurfaceTint > 0) {
                sb.Append("  --egi-panel-tint: color-mix(in oklab, var(--color-panel), var(--color-accent) ")
                    .Append(Num(Math.Round(chroma.SurfaceTint, 1))).Append("%);\n");
            }

            if (chroma.GradientHueShift != 0) {
                var to = model.TokenOrDefault("accent").RotateHue(chroma.GradientHueShift);
                sb.Append("  --egi-accent-grad-to: ").Append(to.ToCss()).Append(";\n");
            }

            if (hueRotate) {
                sb.Append("  animation: egi-hue ").Append(Num(Math.Round(chroma.HueRotate!.Seconds, 1)))
                    .Append("s linear infinite;\n");
            }
        }

        sb.Append("}\n");

        if (!staging && chroma.GradientHueShift != 0) {
            sb.Append(root).Append(" .btn-primary { background-image: linear-gradient(135deg, ")
                .Append("var(--color-accent), var(--egi-accent-grad-to)); }\n");
        }

        if (hueRotate) {
            sb.Append("@keyframes egi-hue { to { --egi-hue-shift: 360deg; } }\n");
            sb.Append("@media (prefers-reduced-motion: reduce) { ").Append(root)
                .Append(" { animation: none; } }\n");
        }
    }

    private static bool OutputAlphabetOk(string output) {
        var span = output.AsSpan();
        if (span.ContainsAny('<', '\\', '&')) return false;
        for (int i = 0; i < span.Length; i++) {
            if (span[i] != '@') continue;
            bool allowed = false;
            foreach (string prefix in AllowedAtPrefixes) {
                if (span[i..].StartsWith(prefix, StringComparison.Ordinal)) {
                    allowed = true;
                    break;
                }
            }

            if (!allowed) return false;
        }

        return true;
    }

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
