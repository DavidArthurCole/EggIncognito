using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// /terms and /privacy render in all modes and both carry the not-affiliated disclaimer. The /about
// page holds the disclaimer + license + the Terms/Privacy links (moved off the site-wide footer so it
// no longer stretches every page's scroll).
[Collection(SharedAppCollection.Name)]
public class LegalPagesTests
{
    private const string Disclaimer =
        "EggIncognito is an independent, fan-made tool and is not affiliated with, endorsed by, or";

    private readonly WebApplicationFactory<Program> _factory;
    public LegalPagesTests(SharedAppFactory f) => _factory = f;

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
    public async Task AboutPage_HoldsDisclaimer_AndLegalLinks()
    {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/about");
        Assert.Contains("href=\"/terms\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task Home_DoesNotPlasterTheFooter()
    {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.DoesNotContain("site-footer", html);
    }
}
