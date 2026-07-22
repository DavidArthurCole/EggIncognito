using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;


[Collection(SharedAppCollection.Name)]
public partial class UnifiedStyleTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Theory]
    [InlineData("/inspector")]
    [InlineData("/capture")]
    public async Task Page_DoesNotLinkBespokeSheet_AndLinksTailwind(string path) {
        var c = _f.CreateClient();
        var html = await c.GetStringAsync(path);
        Assert.DoesNotContain("href=\"styles.css\"", html);
        Assert.Contains("/tailwind.css", html);
    }

    [Fact]
    public async Task CompiledSheet_DefinesUnifiedComponentVocabulary() {
        var c = _f.CreateClient();
        var css = await c.GetStringAsync("/tailwind.css");
        foreach (var cls in new[] { ".panel", ".btn-primary", ".icon-btn", ".settings-menu",
            ".pill", ".status-badge", ".flow-row", ".jtree-root", ".stage-head", ".cap-stat",
            ".toast", ".modal-overlay", ".known-card", ".tab-btn", ".notif-item",
            ".perk-list", ".rail", ".connect-card", ".faq-list",
            ".reg-table", ".reg-row", ".reg-version", ".reg-sha", ".reg-empty",
            ".reg-filter-input", ".reg-edit-btn", ".sub-form", ".sub-item",
            ".legal-disclaimer", ".legal-section",
            ".pg-menubar", ".pg-menu-btn", ".pg-menu", ".pg-menu-item", ".pg-popover",
            ".pg-popover-head", ".pg-palette", ".pg-palette-head" }) {
            Assert.Contains(cls, css);
        }
    }

    [Fact]
    public async Task BtnPrimary_IsAccentOrange_NotAccent2Blue() {
        var c = _f.CreateClient();
        var css = await c.GetStringAsync("/tailwind.css");


        var m = BtnPrimaryRegex().Match(css);
        Assert.True(m.Success, ".btn-primary rule not found");
        Assert.Contains("239 117 89", m.Value);
        Assert.DoesNotContain("90 169 230", m.Value);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\.btn-primary\{[^}]*\}")]
    private static partial System.Text.RegularExpressions.Regex BtnPrimaryRegex();
}
