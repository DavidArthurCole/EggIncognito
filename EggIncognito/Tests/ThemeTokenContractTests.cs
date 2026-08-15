using System.Text.RegularExpressions;
using EggIncognito.Services.Theme;

namespace EggIncognito.Tests;

public partial class ThemeTokenContractTests {
    [Fact]
    public void EveryThemeColorToken_LandsInExactlyOneBucket() {
        var themeTokens = ThemeBlockColorTokens();
        Assert.NotEmpty(themeTokens);
        var settable = ThemeTokens.Settable.ToHashSet(StringComparer.Ordinal);
        var locked = ThemeTokens.Locked.ToHashSet(StringComparer.Ordinal);
        var unclassified = new List<string>();
        var doubled = new List<string>();
        foreach (string token in themeTokens) {
            bool inSettable = settable.Contains(token);
            bool inLocked = locked.Contains(token);
            if (!inSettable && !inLocked) unclassified.Add(token);
            if (inSettable && inLocked) doubled.Add(token);
        }

        Assert.True(unclassified.Count == 0,
            "unclassified @theme color tokens, add each to ThemeTokens.Settable or ThemeTokens.Locked: " +
            string.Join(", ", unclassified));
        Assert.True(doubled.Count == 0,
            "tokens in two buckets: " + string.Join(", ", doubled));
    }

    [Fact]
    public void SettableSet_IsExactlyTheFrozenEleven() {
        string[] frozen = ["bg", "panel0", "panel", "panel2", "fg", "muted", "accent", "info", "ok", "err", "border"];
        Assert.Equal(frozen.OrderBy(x => x, StringComparer.Ordinal),
            ThemeTokens.Settable.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void NoAppToken_UsesATailwindPaletteName() {
        var palettePattern = TailwindPaletteRegex();
        foreach (string token in ThemeBlockColorTokens())
            Assert.False(palettePattern.IsMatch(token), $"app token '{token}' collides with the Tailwind palette");
    }

    [Fact]
    public void DerivedTokens_AreTheFourEgiTokens() {
        string[] expected = ["--egi-glow", "--egi-panel-tint", "--egi-accent-grad-to", "--egi-hue-shift"];
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal),
            ThemeTokens.Derived.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static IReadOnlyList<string> ThemeBlockColorTokens() {
        string css = File.ReadAllText(Path.Combine(FindRepoRoot(), "EggIncognito", "Styles", "app.v4.css"));
        int start = css.IndexOf("@theme", StringComparison.Ordinal);
        Assert.True(start >= 0, "@theme block not found in app.v4.css");
        int open = css.IndexOf('{', start);
        int close = css.IndexOf('}', open);
        string block = css[(open + 1)..close];
        return ColorTokenRegex().Matches(block).Select(m => m.Groups[1].Value).ToList();
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }

    [GeneratedRegex(@"--color-([a-z0-9-]+)\s*:")]
    private static partial Regex ColorTokenRegex();

    [GeneratedRegex(@"^(red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|slate|gray|zinc|neutral|stone)-(50|[1-9]50|[1-9]00)$")]
    private static partial Regex TailwindPaletteRegex();
}
