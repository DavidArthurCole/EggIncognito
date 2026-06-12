using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Guards the Phase 3 unification: neither Inspector nor Capture links a bespoke per-tab sheet anymore;
// the single compiled Tailwind sheet defines the canonical component classes BOTH tabs depend on; and
// .btn-primary resolves to the accent (orange) color - proving Inspector's old blue primary converged
// to the canonical orange. A regression (re-added sheet, missing component, drifted primary) fails here.
public class UnifiedStyleTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    public UnifiedStyleTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Theory]
    [InlineData("/inspector")]
    [InlineData("/capture")]
    public async Task Page_DoesNotLinkBespokeSheet_AndLinksTailwind(string path)
    {
        var c = _f.CreateClient();
        var html = await c.GetStringAsync(path);
        Assert.DoesNotContain("href=\"styles.css\"", html);
        Assert.Contains("/tailwind.css", html);
    }

    [Fact]
    public async Task CompiledSheet_DefinesUnifiedComponentVocabulary()
    {
        var c = _f.CreateClient();
        var css = await c.GetStringAsync("/tailwind.css");
        foreach (var cls in new[] { ".panel", ".btn-primary", ".icon-btn", ".settings-menu",
            ".pill", ".status-badge", ".flow-row", ".jtree-root", ".stage-head", ".device-card",
            ".toast", ".modal-overlay", ".known-card", ".tab-btn", ".notif-item", ".perk-chip",
            ".card-link", ".rail-dot", ".support-hero", ".perk-grid", ".rail", ".connect-card",
            ".faq-grid" })
        {
            Assert.Contains(cls, css);
        }
    }

    [Fact]
    public async Task BtnPrimary_IsAccentOrange_NotAccent2Blue()
    {
        var c = _f.CreateClient();
        var css = await c.GetStringAsync("/tailwind.css");
        // accent = #ef7559 -> rgb(239 117 89). accent2 = #5aa9e6 -> rgb(90 169 230).
        // The .btn-primary rule must carry the orange background, proving the unification.
        var m = System.Text.RegularExpressions.Regex.Match(css, @"\.btn-primary\{[^}]*\}");
        Assert.True(m.Success, ".btn-primary rule not found");
        Assert.Contains("239 117 89", m.Value); // orange background present
        Assert.DoesNotContain("90 169 230", m.Value); // not blue
    }
}
