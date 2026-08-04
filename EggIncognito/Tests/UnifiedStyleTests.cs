using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public partial class UnifiedStyleTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Theory]
    [InlineData("/inspector")]
    [InlineData("/capture")]
    public async Task Page_LinksCompiledSheet_NotTailwind(string path) {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync(path);
        Assert.Contains("/styles.css", html);
        Assert.DoesNotContain("/tailwind.css", html);
    }

    [Fact]
    public async Task CompiledSheet_DefinesUnifiedComponentVocabulary() {
        var c = _f.CreateClient();
        string css = await c.GetStringAsync("/styles.css");
        foreach (string cls in new[] {
                     ".panel", ".btn-primary", ".icon-btn", ".settings-menu",
                     ".pill", ".status-badge", ".flow-row", ".jtree-root", ".stage-head", ".cap-stat",
                     ".toast", ".modal-overlay", ".known-card", ".tab-btn", ".notif-item",
                     ".perk-list", ".rail", ".connect-card", ".faq-list",
                     ".data-table", ".stat-tile", ".reg-row", ".reg-version", ".reg-sha", ".reg-empty",
                     ".reg-filter-input", ".reg-edit-btn", ".sub-form", ".sub-item",
                     ".prose-legal", ".prose-legal-disclaimer", ".prose-legal-section",
                     ".popover", ".popover-combo", ".popover-combo-opt",
                     ".pg-menubar", ".pg-menu-btn", ".pg-menu", ".pg-menu-item", ".pg-popover",
                     ".pg-popover-head", ".pg-palette", ".pg-palette-head"
                 }) {
            Assert.Contains(cls, css);
        }
    }

    [Fact]
    public async Task CompiledSheet_KeepsDynamicallyReferencedClasses() {
        var c = _f.CreateClient();
        string css = await c.GetStringAsync("/styles.css");
        foreach (string cls in new[] {
                     ".status-2xx", ".status-3xx", ".status-4xx", ".status-5xx",
                     ".jv-string", ".jv-number", ".jv-boolean", ".jv-null",
                     ".toast-info", ".perk-chip", ".admin-panel", ".sub-section",
                     ".bg-picker-input", ".picker"
                 }) {
            Assert.Contains(cls, css);
        }
    }

    [Fact]
    public async Task BtnPrimary_IsAccentOrange_NotAccent2Blue() {
        var c = _f.CreateClient();
        string css = await c.GetStringAsync("/styles.css");

        Assert.Contains("#ef7559", css);

        var m = BtnPrimaryRegex().Match(css);
        Assert.True(m.Success, ".btn-primary rule not found");
        Assert.Contains("var(--color-accent)", m.Value);
        Assert.DoesNotContain("accent2", m.Value);
        Assert.DoesNotContain("#5aa9e6", m.Value);
    }

    [GeneratedRegex(@"^\s*\.btn-primary\s*\{[^}]*\}", RegexOptions.Multiline)]
    private static partial Regex BtnPrimaryRegex();
}
