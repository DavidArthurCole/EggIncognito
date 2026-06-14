using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// /terms and /privacy render in all modes and both carry the not-affiliated disclaimer. The footer
// (added site-wide in MainLayout) links both pages and appears on a normal shell page too.
public class LegalPagesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Disclaimer =
        "EggIncognito is an independent, fan-made tool and is not affiliated with, endorsed by, or";

    private readonly WebApplicationFactory<Program> _factory;
    public LegalPagesTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task TermsPage_Renders_WithDisclaimer()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/terms");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Terms of Service", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task PrivacyPage_Renders_WithDisclaimer()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/privacy");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Privacy", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task Footer_RendersSiteWide_WithLegalLinks()
    {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("site-footer", html);
        Assert.Contains("href=\"/terms\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.Contains(Disclaimer, html);
    }
}
