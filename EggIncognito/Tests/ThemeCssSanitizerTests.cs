using EggIdentity.Styles.Theming;
using EggIncognito.Services.Theme;

namespace EggIncognito.Tests;

public class ThemeCssSanitizerTests {
    [Theory]
    [InlineData("@import url(evil.css);")]
    [InlineData("@media (min-width: 1px) { panel { color: red } }")]
    [InlineData("panel { background-image: url(https://x/y.png) }")]
    [InlineData("panel { width: expression(alert(1)) }")]
    [InlineData("panel { -moz-binding: url(x) }")]
    [InlineData("panel { behavior: url(x.htc) }")]
    [InlineData("panel { color: red }</style><script>alert(1)</script>")]
    [InlineData("panel { content: attr(data-secret) }")]
    [InlineData("panel { position: fixed }")]
    [InlineData("panel { z-index: 99999 }")]
    [InlineData("panel { pointer-events: none }")]
    [InlineData("panel { background-image: linear-gradient(url(x), red) }")]
    [InlineData("panel { color: red !important }")]
    [InlineData("panel { /* comment")]
    [InlineData("panel { color: red")]
    [InlineData("panel { background-image: u\\72 l(x) }")]
    [InlineData("panel { col\\6fr: red }")]
    [InlineData("panel { colo\u00A0r: red }")]
    [InlineData("pa\u200Bnel { color: red }")]
    [InlineData("panel { color: red, }")]
    [InlineData("login { color: red }")]
    [InlineData("auth-name { color: red }")]
    [InlineData("body { color: red }")]
    [InlineData("* { color: red }")]
    [InlineData("panel:hover { color: red }")]
    [InlineData(".panel { color: red }")]
    [InlineData("panel { box-shadow: 0 0 2px red, 0 0 2px blue, 0 0 2px green }")]
    [InlineData("scrollbar-thumb { box-shadow: 0 0 4px red }")]
    [InlineData("panel { color: var(--color-red-500) }")]
    [InlineData("panel { color: var(--egi-glow) }")]
    [InlineData("panel { color: var(--color-accent2) }")]
    [InlineData("panel { transition-property: all }")]
    [InlineData("panel { opacity: red }")]
    [InlineData("panel { font-weight: url(x) }")]
    [InlineData("panel { background-image: image-set(url(x) 1x) }")]
    [InlineData("panel { color: env(secret) }")]
    [InlineData("panel { color: element(#x) }")]
    public void HostileInput_IsRejectedWhole(string input) {
        var result = ThemeCss.Parse(input);
        Assert.False(result.Ok);
        Assert.Empty(result.Rules);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void OversizedSource_IsRejected() {
        string input = "panel { color: red }" + new string(' ', ThemeModel.MaxCssSourceBytes);
        var result = ThemeCss.Parse(input);
        Assert.False(result.Ok);
    }

    [Theory]
    [InlineData("panel { opacity: 0 }", "opacity: 0.35")]
    [InlineData("panel { opacity: 0.01 }", "opacity: 0.35")]
    [InlineData("panel { border-radius: 9999px }", "border-radius: 32px")]
    [InlineData("panel { border-width: 100px }", "border-width: 4px")]
    [InlineData("panel { color: rgb(999, 0, 0) }", "color: rgb(255, 0, 0)")]
    [InlineData("panel { font-weight: 9000 }", "font-weight: 900")]
    [InlineData("panel { letter-spacing: 5em }", "letter-spacing: 0.2em")]
    [InlineData("panel { transition-duration: 90s }", "transition-duration: 1000ms")]
    public void OutOfRangeValues_AreClamped(string input, string expectedFragment) {
        var result = ThemeCss.Parse(FullGroupWrap(input));
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(e => e.Message)));
        string css = Serialize(result);
        Assert.Contains(expectedFragment, css);
    }

    [Fact]
    public void UppercaseInput_IsCaseFolded() {
        var result = ThemeCss.Parse("PANEL { COLOR: RED }");
        Assert.True(result.Ok);
        string css = Serialize(result);
        Assert.Contains(".panel", css);
        Assert.Contains("color: red", css);
    }

    [Fact]
    public void SettableVar_IsCanonicalized() {
        var result = ThemeCss.Parse("panel { color: var(--color-accent) }");
        Assert.True(result.Ok);
        Assert.Contains("var(--color-accent)", Serialize(result));
    }

    [Fact]
    public void CommentsAreDropped_NeverEmitted() {
        var result = ThemeCss.Parse("panel { /* a comment */ color: red }");
        Assert.True(result.Ok);
        Assert.DoesNotContain("comment", Serialize(result));
    }

    [Fact]
    public void TwoShadows_Parse_AndThreeDoNot() {
        Assert.True(ThemeCss.Parse("panel { box-shadow: 0 0 2px red, inset 0 1px 4px 2px #001122 }").Ok);
        Assert.False(ThemeCss.Parse("panel { box-shadow: 0 0 2px red, 0 0 2px blue, 0 0 2px green }").Ok);
    }

    [Fact]
    public void Gradients_ParseWithinTheGrammar() {
        Assert.True(ThemeCss.Parse(
            "panel { background-image: linear-gradient(135deg, var(--color-panel) 0%, var(--color-accent) 100%) }").Ok);
        Assert.True(ThemeCss.Parse(
            "panel { background-image: radial-gradient(circle, #112233, transparent) }").Ok);
        Assert.False(ThemeCss.Parse(
            "panel { background-image: conic-gradient(red, blue) }").Ok);
    }

    [Fact]
    public void ColorMix_OnlyInOklab() {
        Assert.True(ThemeCss.Parse(
            "panel { color: color-mix(in oklab, var(--color-accent) 40%, transparent) }").Ok);
        Assert.False(ThemeCss.Parse(
            "panel { color: color-mix(in srgb, red 40%, blue) }").Ok);
    }

    [Fact]
    public void RetiredNavSurfaces_AreUnknown() {
        var nav = ThemeCss.Parse("nav { color: red }");
        Assert.False(nav.Ok);
        Assert.Contains(nav.Errors, e => e.Message.Contains("unknown surface", StringComparison.Ordinal));
        Assert.False(ThemeCss.Parse("nav-item { color: red }").Ok);
    }

    [Fact]
    public void EveryCatalogGroup_EnforcesItsPropertyFloor() {
        Assert.False(ThemeCss.Parse("scrollbar-thumb { font-weight: 700 }").Ok);
        Assert.False(ThemeCss.Parse("table-row { font-weight: 700 }").Ok);
        Assert.True(ThemeCss.Parse("table-header { font-weight: 700 }").Ok);
        Assert.False(ThemeCss.Parse("table-header { opacity: 0.5 }").Ok);
        Assert.True(ThemeCss.Parse("button { opacity: 0.5 }").Ok);
    }

    private static string FullGroupWrap(string input) => input.Replace("panel {", "button {");

    private static string FormatNumber(double v) {
        double rounded = Math.Round(v, 4);
        return rounded.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Serialize(CssParseResult parsed) {
        Assert.True(parsed.Ok);
        string source = string.Join("\n", parsed.Rules.Select(RuleSource));
        return ThemeTestSupport.Serializer()
            .Serialize(ThemePresets.Default.WithCss(source), ThemeScope.Preview, true);
    }

    private static string RuleSource(CssRule rule) {
        var sb = new System.Text.StringBuilder();
        sb.Append(rule.Entry.Name).Append(" { ");
        foreach (var decl in rule.Declarations) {
            sb.Append(decl.Property).Append(": ");
            for (int g = 0; g < decl.Groups.Count; g++) {
                if (g > 0) sb.Append(", ");
                AppendParts(sb, decl.Groups[g]);
            }

            sb.Append("; ");
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendParts(System.Text.StringBuilder sb, IReadOnlyList<CssPart> parts) {
        for (int i = 0; i < parts.Count; i++) {
            if (i > 0) sb.Append(' ');
            switch (parts[i]) {
                case CssKeyword kw:
                    sb.Append(kw.Text);
                    break;
                case CssNumber num:
                    sb.Append(FormatNumber(num.Value)).Append(num.Unit);
                    break;
                case CssHex hex:
                    sb.Append('#').Append(hex.R.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
                        .Append(hex.G.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
                        .Append(hex.B.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case CssFunc fn:
                    sb.Append(fn.Name).Append('(');
                    for (int a = 0; a < fn.Args.Count; a++) {
                        if (a > 0) sb.Append(", ");
                        AppendParts(sb, fn.Args[a]);
                    }

                    sb.Append(')');
                    break;
            }
        }
    }
}
