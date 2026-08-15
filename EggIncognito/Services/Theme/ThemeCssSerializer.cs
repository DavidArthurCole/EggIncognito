using System.Buffers;
using System.Globalization;
using System.Text;

namespace EggIncognito.Services.Theme;

public enum ThemeScope {
    Live,
    Preview
}

public sealed class ThemeCssSerializer(IWebHostEnvironment env, ILogger<ThemeCssSerializer> logger) {
    public const string LivePrefix = "html[data-egi-theme=\"u\"]";
    public const string PreviewPrefix = ".theme-preview-scope";
    public const int MaxLane2OutputBytes = 8 * 1024;

    private static readonly string[] AllowedAtPrefixes = ["@keyframes", "@media"];
    private static readonly SearchValues<char> Lane2Forbidden = SearchValues.Create("<\\@&");

    private bool IsStaging => string.Equals(env.EnvironmentName, "Staging", StringComparison.OrdinalIgnoreCase);

    public static bool UsesHueRotation(ThemeModel model) => model.Chroma.HueRotate is { Enabled: true };

    public string Serialize(ThemeModel model, ThemeScope scope, bool customCssAllowed) {
        string root = scope == ThemeScope.Live ? LivePrefix : PreviewPrefix;
        var sb = new StringBuilder();
        WriteLane1(sb, model, root);

        if (customCssAllowed && !IsStaging && !string.IsNullOrWhiteSpace(model.Css)) {
            string lane2 = SerializeLane2(model.Css, root);
            if (Encoding.UTF8.GetByteCount(lane2) > MaxLane2OutputBytes) {
                logger.LogWarning("theme '{Slug}': lane-2 output over {Cap} KB, dropped", model.Slug,
                    MaxLane2OutputBytes / 1024);
            } else if (!Lane2AlphabetOk(lane2)) {
                logger.LogError("theme '{Slug}': lane-2 self-check failed, output dropped", model.Slug);
                return "";
            } else {
                sb.Append(lane2);
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
            var color = model.ResolveToken(name);
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
                var to = model.ResolveToken("accent").RotateHue(chroma.GradientHueShift);
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

    private static string SerializeLane2(string css, string root) {
        var parsed = ThemeCssParser.Parse(css);
        if (!parsed.Ok) return "";
        var sb = new StringBuilder();
        foreach (var rule in parsed.Rules) {
            sb.Append(ScopedSelector(root, rule.Entry.Selector)).Append(" {\n");
            foreach (var decl in rule.Declarations) {
                sb.Append("  ").Append(decl.Property).Append(": ");
                for (int g = 0; g < decl.Groups.Count; g++) {
                    if (g > 0) sb.Append(", ");
                    AppendParts(sb, decl.Groups[g]);
                }

                sb.Append(";\n");
            }

            sb.Append("}\n");
        }

        return sb.ToString();
    }

    private static string ScopedSelector(string root, string canonical) {
        var parts = canonical.Split(", ");
        return string.Join(", ", parts.Select(p => $"{root} {p}"));
    }

    private static void AppendParts(StringBuilder sb, IReadOnlyList<CssPart> parts) {
        for (int i = 0; i < parts.Count; i++) {
            if (i > 0) sb.Append(' ');
            AppendPart(sb, parts[i]);
        }
    }

    private static void AppendPart(StringBuilder sb, CssPart part) {
        switch (part) {
            case CssKeyword kw:
                sb.Append(kw.Text);
                break;
            case CssNumber num:
                sb.Append(ThemeCssParser.FormatNumber(num.Value)).Append(num.Unit);
                break;
            case CssHex hex:
                sb.Append('#').Append(hex.R.ToString("x2", CultureInfo.InvariantCulture))
                    .Append(hex.G.ToString("x2", CultureInfo.InvariantCulture))
                    .Append(hex.B.ToString("x2", CultureInfo.InvariantCulture));
                if (hex.A is { } a) sb.Append(a.ToString("x2", CultureInfo.InvariantCulture));
                break;
            case CssFunc fn:
                sb.Append(fn.Name).Append('(');
                for (int i = 0; i < fn.Args.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    AppendParts(sb, fn.Args[i]);
                }

                sb.Append(')');
                break;
        }
    }

    private static bool Lane2AlphabetOk(string output) =>
        !output.AsSpan().ContainsAny(Lane2Forbidden);

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
